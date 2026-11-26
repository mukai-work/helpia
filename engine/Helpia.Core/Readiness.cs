using System.Text.RegularExpressions;

namespace Helpia.Core;

/// <summary>
/// 「AIに任せられる状態か」の点検。追跡されているファイルの一覧と、いくつかのファイルの中身だけを見る。
/// 点数は付けない（重み付けに根拠が無いため）。満たしている項目と、足すとよいものを並べる。
/// </summary>
public static partial class Readiness
{
    public static IReadOnlyList<ReadinessCheck> Check(string repo, IReadOnlyList<string> tracked)
    {
        bool Has(Func<string, bool> pred) => tracked.Any(pred);
        string Read(string rel) { try { return File.ReadAllText(Path.Combine(repo, rel)); } catch (IOException) { return ""; } }
        var lower = tracked.Select(t => t.ToLowerInvariant()).ToList();
        bool HasLower(Func<string, bool> pred) => lower.Any(pred);

        var list = new List<ReadinessCheck>();

        // 1. AI への指示書
        var guides = tracked.Where(t => Path.GetFileName(t) is "CLAUDE.md" or "AGENTS.md" or ".cursorrules" or "GEMINI.md"
            || t.Equals(".github/copilot-instructions.md", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith(".cursor/rules/", StringComparison.OrdinalIgnoreCase)).ToList();
        list.Add(guides.Count > 0
            ? new("guide", "AIへの指示書", CheckStatus.Ok, string.Join("、", guides.Take(4)), "")
            : new("guide", "AIへの指示書", CheckStatus.Missing, "CLAUDE.md・AGENTS.md・.cursorrules などが無い",
                "ビルド・テストの実行方法と、触ってはいけない場所を書いた AGENTS.md を置く（AIが毎回同じ前提で動ける）"));

        // 2. README のセットアップ手順
        var readme = tracked.FirstOrDefault(t => Regex.IsMatch(t, @"^readme(\.md)?$", RegexOptions.IgnoreCase));
        var readmeText = readme is null ? "" : Read(readme);
        var hasSetup = SetupWords().IsMatch(readmeText);
        list.Add(readme is null
            ? new("readme", "READMEのセットアップ手順", CheckStatus.Missing, "README が無い", "起動とテストのコマンドを README に書く")
            : hasSetup
                ? new("readme", "READMEのセットアップ手順", CheckStatus.Ok, "起動・テストの手順あり", "")
                : new("readme", "READMEのセットアップ手順", CheckStatus.Warn, "README に起動・テストの手順が見当たらない",
                    "install・test などのコマンドをコードブロックで書く"));

        // 3. テストがあるか
        var tests = tracked.Count(t => Classify.IsCode(t) && Classify.IsTest(t));
        list.Add(tests > 0
            ? new("tests", "テスト", CheckStatus.Ok, $"テストのファイル {tests}件", "")
            : new("tests", "テスト", CheckStatus.Missing, "テストのファイルが見当たらない",
                "AIの変更を機械で確かめる手段が無い。まず壊れると困る処理から1本足す"));

        // 4. CI
        var ci = tracked.Where(t => t.StartsWith(".github/workflows/") || t is ".gitlab-ci.yml" or "azure-pipelines.yml"
            || t.StartsWith(".circleci/")).ToList();
        list.Add(ci.Count > 0
            ? new("ci", "CI（自動テスト）", CheckStatus.Ok, string.Join("、", ci.Take(3)), "")
            : new("ci", "CI（自動テスト）", CheckStatus.Missing, "CI の設定が無い",
                "プッシュのたびにテストが走るようにする（AIの変更を人が全部読まなくても止められる）"));

        // 5. 書式・静的解析
        var lint = tracked.Where(t => LintFiles().IsMatch(Path.GetFileName(t))).ToList();
        list.Add(lint.Count > 0
            ? new("lint", "書式・静的解析の設定", CheckStatus.Ok, string.Join("、", lint.Take(3)), "")
            : new("lint", "書式・静的解析の設定", CheckStatus.Warn, "eslint・prettier・.editorconfig などが無い",
                "書式を機械にそろえさせると、AIの差分が読みやすくなる"));

        // 6. 依存のロック
        var lockFile = tracked.FirstOrDefault(t => LockFiles().IsMatch(Path.GetFileName(t)));
        var hasManifest = HasLower(t => t.EndsWith("package.json") || t.EndsWith(".csproj") || t.EndsWith("pyproject.toml")
            || t.EndsWith("requirements.txt") || t.EndsWith("go.mod") || t.EndsWith("cargo.toml"));
        if (hasManifest)
            list.Add(lockFile is not null || HasLower(t => t.EndsWith(".csproj"))
                ? new("lock", "依存のバージョン固定", CheckStatus.Ok, lockFile ?? ".csproj でバージョン指定", "")
                : new("lock", "依存のバージョン固定", CheckStatus.Warn, "ロックファイルが無い",
                    "AIが入れた依存のバージョンが環境ごとに変わらないよう、ロックファイルをコミットする"));

        // 7. 環境変数の見本
        // コードが環境変数を読んでいるときだけ点検する（読んでいないのに「見本が無い」と言わない）
        var hasExample = HasLower(t => t.EndsWith(".env.example") || t.EndsWith(".env.sample") || t.EndsWith(".env.template"));
        var envReaders = tracked.Where(t => Classify.IsCode(t) && !Classify.IsTest(t)).Take(3000)
            .Where(t => EnvRead().IsMatch(Read(t))).Take(3).ToList();
        if (hasExample)
            list.Add(new("envexample", "環境変数の見本", CheckStatus.Ok, ".env.example あり", ""));
        else if (envReaders.Count > 0)
            list.Add(new("envexample", "環境変数の見本", CheckStatus.Warn, "環境変数を読んでいるが見本が無い（例: " + string.Join("、", envReaders) + "）",
                "必要な環境変数の名前だけを書いた見本を置く（AIが値を推測して書き込むのを防ぐ）"));

        // 8. 秘密情報らしき文字列
        var hits = new List<string>();
        foreach (var t in tracked.Where(t => !Classify.IsTest(t) && IsTextCandidate(t)).Take(3000))
        {
            var text = Read(t);
            if (text.Length > 512_000) continue;
            if (SecretPattern().IsMatch(text)) hits.Add(t);
            if (hits.Count >= 5) break;
        }
        if (tracked.Any(t => Path.GetFileName(t) is ".env")) hits.Insert(0, ".env");
        list.Add(hits.Count == 0
            ? new("secrets", "秘密情報の混入", CheckStatus.Ok, "鍵・トークンの形をした文字列は見つからない（形で探しただけ）", "")
            : new("secrets", "秘密情報の混入", CheckStatus.Warn, "要確認: " + string.Join("、", hits.Distinct().Take(5)),
                "本物の鍵なら無効にして作り直し、履歴からも消す。見本の値なら無視してよい"));

        return list;
    }

    private static bool IsTextCandidate(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return Classify.IsCode(path) || ext is ".json" or ".yml" or ".yaml" or ".toml" or ".env" or ".ini" or ".config" or ".txt" or ".md";
    }

    [GeneratedRegex(@"process\.env\.|import\.meta\.env\.[A-Z]|Environment\.GetEnvironmentVariable|os\.environ|os\.getenv|getenv\(|ENV\[")]
    private static partial Regex EnvRead();

    [GeneratedRegex(@"(npm|pnpm|yarn|dotnet|pip|uv|poetry|cargo|go|make|docker)\s+(install|i|ci|run|test|build|restore|sync|up)", RegexOptions.IgnoreCase)]
    private static partial Regex SetupWords();

    [GeneratedRegex(@"^(\.?eslint|\.prettierrc|prettier\.config|biome\.json|\.editorconfig|ruff\.toml|\.ruff\.toml|\.flake8|stylecop|\.stylelintrc|tslint\.json|Directory\.Build\.props)", RegexOptions.IgnoreCase)]
    private static partial Regex LintFiles();

    [GeneratedRegex(@"^(package-lock\.json|pnpm-lock\.yaml|yarn\.lock|bun\.lockb?|poetry\.lock|uv\.lock|Pipfile\.lock|Cargo\.lock|go\.sum|packages\.lock\.json)$", RegexOptions.IgnoreCase)]
    private static partial Regex LockFiles();

    /// <summary>よく漏れる鍵の形だけを探す（誤検知を減らすため、汎用の「password=」などは見ない）。</summary>
    [GeneratedRegex(@"AKIA[0-9A-Z]{16}|-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----|ghp_[A-Za-z0-9]{36}|github_pat_[A-Za-z0-9_]{60,}|sk-ant-[A-Za-z0-9_-]{20,}|sk-(proj-)?[A-Za-z0-9]{32,}|xox[baprs]-[A-Za-z0-9-]{10,}|AIza[0-9A-Za-z_-]{35}")]
    private static partial Regex SecretPattern();
}
