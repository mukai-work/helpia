using System.Diagnostics;
using System.Text;

namespace Helpia.Core;

/// <summary>1つのコミットで変わった1ファイル。バイナリは行数が取れないので 0 として数える。</summary>
public sealed record FileChange(string Path, int Added, int Deleted);

/// <summary>git log から読んだ1コミット（マージは除外済み）。</summary>
public sealed record Commit(string Hash, DateTimeOffset Date, string Message, IReadOnlyList<FileChange> Files);

/// <summary>git を子プロセスで呼んで履歴を読む。LibGit2Sharp などの依存は増やさない。</summary>
public static class GitHistory
{
    private const char RecordSep = '\u001e';
    private const char FieldSep = '\u001f';
    private const char BodyEnd = '\u001d';

    public static IReadOnlyList<Commit> Read(string repo, int maxCommits = 2000)
    {
        var output = RunGit(repo,
            "log", "--no-merges", $"-n{maxCommits}", "--numstat", "--no-renames",
            $"--format={RecordSep}%H{FieldSep}%aI{FieldSep}%B{BodyEnd}");
        return Parse(output);
    }

    /// <summary>git log の出力を読む。テストから直接呼べるよう分けている。</summary>
    public static IReadOnlyList<Commit> Parse(string output)
    {
        var commits = new List<Commit>();
        foreach (var record in output.Split(RecordSep, StringSplitOptions.RemoveEmptyEntries))
        {
            var bodyEnd = record.IndexOf(BodyEnd);
            if (bodyEnd < 0) continue;
            var head = record[..bodyEnd].Split(FieldSep, 3);
            if (head.Length < 3) continue;

            var files = new List<FileChange>();
            foreach (var line in record[(bodyEnd + 1)..].Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var cols = line.TrimEnd('\r').Split('\t');
                if (cols.Length < 3) continue;
                int.TryParse(cols[0], out var add); // バイナリは "-" なので 0 になる
                int.TryParse(cols[1], out var del);
                files.Add(new FileChange(cols[2].Replace('\\', '/'), add, del));
            }
            commits.Add(new Commit(head[0].Trim(), DateTimeOffset.Parse(head[1].Trim()), head[2].Trim(), files));
        }
        return commits;
    }

    public static string RunGit(string repo, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(repo);
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("core.quotepath=false"); // 日本語のファイル名をエスケープさせない
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("git を起動できません");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"git が失敗しました: {stderr.Result}");
        return stdout.Result;
    }

    /// <summary>追跡されているファイルの一覧（準備度の点検に使う）。</summary>
    public static IReadOnlyList<string> TrackedFiles(string repo) =>
        RunGit(repo, "ls-files").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim().Replace('\\', '/')).ToList();
}
