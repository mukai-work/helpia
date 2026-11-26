using System.Text.RegularExpressions;

namespace Helpia.Core;

/// <summary>コミットとファイルの分類。規則は PROJECT.md「3. 計測の定義」と揃える。</summary>
public static partial class Classify
{
    /// <summary>見分けられる AI ツール名。Co-Authored-By の行か「Generated with」の記載で判定する。</summary>
    private const string Tools = "claude|anthropic|copilot|cursor|codex|openai|gemini|devin|aider|chatgpt";

    [GeneratedRegex(@"^co-authored-by:.*\b(" + Tools + @")\b", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex CoAuthor();

    [GeneratedRegex(@"generated (with|by) \[?(" + Tools + @"|claude code)", RegexOptions.IgnoreCase)]
    private static partial Regex GeneratedWith();

    /// <summary>AI が関わった印があるか。どのツールかも返す（印が無ければ null）。</summary>
    public static string? AiTool(string message)
    {
        var m = CoAuthor().Match(message);
        if (!m.Success) m = GeneratedWith().Match(message);
        if (!m.Success) return null;
        var name = m.Groups[m.Groups.Count - 1].Value.ToLowerInvariant();
        return name switch
        {
            "anthropic" or "claude code" => "claude",
            "openai" or "chatgpt" => "openai",
            _ => name,
        };
    }

    [GeneratedRegex(@"(^|/)(tests?|specs?|__tests__)/|\.tests?/|[._-](test|spec|tests)\.[a-z0-9]+$|_test\.[a-z0-9]+$|Tests?\.cs$", RegexOptions.IgnoreCase)]
    private static partial Regex TestPath();

    public static bool IsTest(string path) => TestPath().IsMatch(path);

    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".vue", ".svelte", ".astro", ".py", ".go", ".rs",
        ".java", ".kt", ".kts", ".swift", ".rb", ".php", ".c", ".cc", ".cpp", ".h", ".hpp", ".scala", ".dart",
        ".sql", ".sh", ".ps1", ".css", ".scss", ".html",
    };

    /// <summary>
    /// コードのファイルか。文書・設定・ロックファイル・生成物は外す（テストもコードに含める）。
    /// </summary>
    public static bool IsCode(string path)
    {
        var p = path.ToLowerInvariant();
        if (p.Contains("/node_modules/") || p.StartsWith("node_modules/") || p.Contains("/dist/") || p.StartsWith("dist/")
            || p.Contains("/bin/") || p.Contains("/obj/") || p.EndsWith(".min.js") || p.EndsWith(".lock")
            || p.EndsWith("package-lock.json")) return false;
        return CodeExtensions.Contains(Path.GetExtension(p));
    }
}
