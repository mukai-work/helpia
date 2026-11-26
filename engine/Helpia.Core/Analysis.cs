using System.Text.Json.Serialization;

namespace Helpia.Core;

/// <summary>AI あり／なしの片方の集計。比率は分母が 0 のとき null（「測れない」を 0% と混同しない）。</summary>
public sealed record GroupStats(
    int Commits,
    /// <summary>コードの変更を含むコミット数（テスト同伴率・手戻り率の分母）。</summary>
    int CodeCommits,
    double? TestTouchRate,
    double? ReworkRate,
    double? MedianLinesChanged,
    int LargeCommits,
    IReadOnlyDictionary<string, int> Tools);

public sealed record HistoryReport(
    int TotalCommits,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int ReworkWindowDays,
    int LargeCommitLines,
    GroupStats Ai,
    GroupStats Human,
    /// <summary>AI の印付きコミットの割合。印を付けない運用は数えられないので下限値。</summary>
    double? AiShare);

public static class Analysis
{
    public const int DefaultReworkDays = 7;
    public const int DefaultLargeLines = 500;

    /// <summary>
    /// 手戻り: コミット C が変えたコードファイルを、C より後・7日以内の別コミットが変えたら「手戻りあり」。
    /// 期間の終わりから7日以内のコミットは、まだ判定できないので手戻り率の分母から外す。
    /// </summary>
    public static HistoryReport Analyze(IReadOnlyList<Commit> commits, int reworkDays = DefaultReworkDays,
        int largeLines = DefaultLargeLines, DateTimeOffset? now = null)
    {
        var ordered = commits.OrderBy(c => c.Date).ToList();
        var end = now ?? (ordered.Count > 0 ? ordered[^1].Date : DateTimeOffset.Now);
        var window = TimeSpan.FromDays(reworkDays);

        // ファイルごとに「いつ変わったか」の一覧
        var touches = new Dictionary<string, List<(DateTimeOffset Date, string Hash)>>();
        foreach (var c in ordered)
            foreach (var f in c.Files.Where(f => Classify.IsCode(f.Path)))
                (touches.TryGetValue(f.Path, out var l) ? l : touches[f.Path] = []).Add((c.Date, c.Hash));

        var rows = ordered.Select(c =>
        {
            var code = c.Files.Where(f => Classify.IsCode(f.Path)).ToList();
            bool? rework = null;
            if (code.Count > 0 && end - c.Date >= window)
                rework = code.Any(f => touches[f.Path].Any(t => t.Hash != c.Hash && t.Date > c.Date && t.Date - c.Date <= window));
            return new
            {
                Tool = Classify.AiTool(c.Message),
                HasCode = code.Count > 0,
                TouchesTest = code.Any(f => Classify.IsTest(f.Path)),
                Rework = rework,
                Lines = code.Sum(f => f.Added + f.Deleted),
            };
        }).ToList();

        GroupStats Stats(bool ai)
        {
            var g = rows.Where(r => (r.Tool is not null) == ai).ToList();
            var code = g.Where(r => r.HasCode).ToList();
            var judged = code.Where(r => r.Rework is not null).ToList();
            return new GroupStats(
                g.Count,
                code.Count,
                Rate(code.Count(r => r.TouchesTest), code.Count),
                Rate(judged.Count(r => r.Rework == true), judged.Count),
                Median(code.Select(r => (double)r.Lines).ToList()),
                code.Count(r => r.Lines >= largeLines),
                g.Where(r => r.Tool is not null).GroupBy(r => r.Tool!).ToDictionary(x => x.Key, x => x.Count()));
        }

        return new HistoryReport(
            ordered.Count,
            ordered.Count > 0 ? ordered[0].Date : null,
            ordered.Count > 0 ? ordered[^1].Date : null,
            reworkDays, largeLines,
            Stats(true), Stats(false),
            Rate(rows.Count(r => r.Tool is not null), rows.Count));
    }

    public static double? Rate(int num, int den) => den == 0 ? null : Math.Round((double)num / den, 4);

    public static double? Median(List<double> xs)
    {
        if (xs.Count == 0) return null;
        xs.Sort();
        var m = xs.Count / 2;
        return xs.Count % 2 == 1 ? xs[m] : (xs[m - 1] + xs[m]) / 2;
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<CheckStatus>))]
public enum CheckStatus { Ok, Warn, Missing }

public sealed record ReadinessCheck(string Id, string Title, CheckStatus Status, string Detail, string Advice);
