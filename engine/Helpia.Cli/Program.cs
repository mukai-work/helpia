using System.Text.Encodings.Web;
using System.Text.Json;
using Helpia.Core;

// ヘルピア — AI駆動開発の現場点検
//   scan  --repo <パス> --name <表示名> [--id 英数字] [--out reports] [--max 2000] [--rework-days 7]
//   index [--dir reports]

Console.OutputEncoding = System.Text.Encoding.UTF8;
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};
var opts = new Dictionary<string, string>();
for (int i = 1; i < args.Length; i++)
    if (args[i].StartsWith("--", StringComparison.Ordinal))
        opts[args[i][2..]] = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "on";
string Opt(string k, string? def = null) =>
    opts.TryGetValue(k, out var v) ? v : def ?? throw new ArgumentException($"--{k} が必要です");

try
{
    switch (args.FirstOrDefault())
    {
        case "scan":
        {
            var repo = Path.GetFullPath(Opt("repo"));
            var name = Opt("name");
            var commits = GitHistory.Read(repo, int.Parse(Opt("max", "2000")));
            var history = Analysis.Analyze(commits, int.Parse(Opt("rework-days", Analysis.DefaultReworkDays.ToString())));
            var checks = Readiness.Check(repo, GitHistory.TrackedFiles(repo));
            var report = new { id = Slug(Opt("id", Path.GetFileName(repo.TrimEnd(Path.DirectorySeparatorChar)))), name, scannedAt = DateTimeOffset.Now, history, readiness = checks };

            var outDir = Opt("out", "reports");
            Directory.CreateDirectory(outDir);
            var id = Slug(Opt("id", Path.GetFileName(repo.TrimEnd(Path.DirectorySeparatorChar))));
            if (id.Length == 0) throw new ArgumentException("--id に英数字の名前を指定してください");
            var path = Path.Combine(outDir, id + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(report, json));

            string P(double? v) => v is null ? "—" : $"{v:P0}";
            Console.WriteLine($"[{name}] コミット {history.TotalCommits}件（AIの印あり {history.Ai.Commits}件 = {P(history.AiShare)}）");
            Console.WriteLine($"  テストも変えた割合   AI {P(history.Ai.TestTouchRate)} / 人 {P(history.Human.TestTouchRate)}");
            Console.WriteLine($"  手戻り率（{history.ReworkWindowDays}日以内） AI {P(history.Ai.ReworkRate)} / 人 {P(history.Human.ReworkRate)}");
            Console.WriteLine($"  変更行数の中央値     AI {history.Ai.MedianLinesChanged?.ToString() ?? "—"} / 人 {history.Human.MedianLinesChanged?.ToString() ?? "—"}");
            foreach (var c in checks) Console.WriteLine($"  [{c.Status,-7}] {c.Title}: {c.Detail}");
            Console.WriteLine("保存: " + Path.GetFullPath(path));
            return 0;
        }
        case "index":
        {
            var dir = Opt("dir", "reports");
            var files = Directory.GetFiles(dir, "*.json").Where(f => Path.GetFileName(f) != "index.json")
                .Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
            File.WriteAllText(Path.Combine(dir, "index.json"), JsonSerializer.Serialize(files, json));
            Console.WriteLine($"index.json に {files.Count}件");
            return 0;
        }
        default:
            Console.WriteLine("ヘルピア — AI駆動開発の現場点検\n  scan  --repo <パス> --name <表示名> [--out reports] [--max 2000] [--rework-days 7]\n  index [--dir reports]");
            return 0;
    }
}
catch (Exception e) when (e is ArgumentException or InvalidOperationException or DirectoryNotFoundException)
{
    Console.Error.WriteLine("エラー: " + e.Message);
    return 2;
}

static string Slug(string s) =>
    new string(s.Select(c => char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray()).Trim('-');
