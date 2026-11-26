using Helpia.Core;

namespace Helpia.Tests;

public class ClassifyTests
{
    [Theory]
    [InlineData("修正\n\nCo-Authored-By: Claude <noreply@anthropic.com>", "claude")]
    [InlineData("fix\n\nco-authored-by: Copilot <copilot@github.com>", "copilot")]
    [InlineData("feat\n\nGenerated with [Claude Code](https://claude.ai/code)", "claude")]
    [InlineData("feat\n\nCo-authored-by: Cursor Agent <cursoragent@cursor.com>", "cursor")]
    public void AIの印を見分ける(string message, string tool) => Assert.Equal(tool, Classify.AiTool(message));

    [Theory]
    [InlineData("人が書いた修正")]
    [InlineData("Co-Authored-By: Taro Yamada <taro@example.com>")]
    [InlineData("claude の設定を更新")] // 本文にツール名があるだけでは印としない
    public void 印が無ければnull(string message) => Assert.Null(Classify.AiTool(message));

    [Theory]
    [InlineData("engine/Hakaru.Tests/ScoringTests.cs", true)]
    [InlineData("src/utils/date.test.ts", true)]
    [InlineData("tests/test_api.py", true)]
    [InlineData("pkg/api_test.go", true)]
    [InlineData("src/__tests__/App.spec.tsx", true)]
    [InlineData("src/contest/page.tsx", false)]
    [InlineData("src/latest.ts", false)]
    public void テストのパスを見分ける(string path, bool expected) => Assert.Equal(expected, Classify.IsTest(path));

    [Theory]
    [InlineData("src/App.vue", true)]
    [InlineData("README.md", false)]
    [InlineData("package-lock.json", false)]
    [InlineData("web/dist/assets/index.js", false)]
    [InlineData("engine/bin/Debug/x.cs", false)]
    public void コードのファイルを見分ける(string path, bool expected) => Assert.Equal(expected, Classify.IsCode(path));
}

public class AnalysisTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 10, 0, 0, TimeSpan.FromHours(9));
    private const string Ai = "\n\nCo-Authored-By: Claude <noreply@anthropic.com>";

    private static Commit C(string hash, int day, string msg, params (string Path, int Lines)[] files) =>
        new(hash, T0.AddDays(day), msg, files.Select(f => new FileChange(f.Path, f.Lines, 0)).ToList());

    [Fact]
    public void AIと人で分けて数える()
    {
        var commits = new[]
        {
            C("a1", 0, "AIで追加" + Ai, ("src/a.ts", 100), ("src/a.test.ts", 20)),
            C("a2", 1, "AIで追加" + Ai, ("src/b.ts", 600)),
            C("h1", 2, "人が修正", ("src/a.ts", 5)),       // a1 の手戻り（2日後）
            C("h2", 20, "人が文書", ("README.md", 10)),    // コードなし → 分母から外れる
            C("h3", 40, "人", ("src/c.ts", 30)),           // 期間の終わり → 手戻りは判定しない
        };
        var r = Analysis.Analyze(commits);

        Assert.Equal(5, r.TotalCommits);
        Assert.Equal(0.4, r.AiShare);
        Assert.Equal(2, r.Ai.CodeCommits);
        Assert.Equal(0.5, r.Ai.TestTouchRate);
        Assert.Equal(0.5, r.Ai.ReworkRate);      // a1 は手戻りあり、a2 はなし
        Assert.Equal(1, r.Ai.LargeCommits);      // 600行
        Assert.Equal(360, r.Ai.MedianLinesChanged); // (100+20) と 600 の中央値
        Assert.Equal(2, r.Human.CodeCommits);    // h1・h3（h2 は文書だけ）
        Assert.Equal(0.0, r.Human.ReworkRate);   // h1 は後から誰も触っていない、h3 は判定外
        Assert.Equal(2, r.Ai.Tools["claude"]);
    }

    [Fact]
    public void 七日を過ぎた変更は手戻りにしない()
    {
        var commits = new[]
        {
            C("a", 0, "x" + Ai, ("src/a.ts", 10)),
            C("b", 8, "y", ("src/a.ts", 10)),
            C("c", 30, "z", ("src/z.ts", 1)),
        };
        Assert.Equal(0.0, Analysis.Analyze(commits).Ai.ReworkRate);
    }

    [Fact]
    public void 分母が0なら比率はnull()
    {
        var r = Analysis.Analyze([C("h", 0, "人", ("README.md", 1))]);
        Assert.Null(r.Ai.TestTouchRate);
        Assert.Null(r.Human.TestTouchRate);
    }

    [Fact]
    public void gitの出力を読める()
    {
        const string rs = "\u001e", fs = "\u001f", be = "\u001d";
        var output = $"{rs}abc{fs}2026-11-26T10:00:00+09:00{fs}件名\n\nCo-Authored-By: Claude <x>{be}\n\n12\t3\tsrc/a.ts\n-\t-\timg.png\n"
                   + $"{rs}def{fs}2026-11-25T09:00:00+09:00{fs}人{be}\n\n1\t0\tREADME.md\n";
        var commits = GitHistory.Parse(output);
        Assert.Equal(2, commits.Count);
        Assert.Equal("claude", Classify.AiTool(commits[0].Message));
        Assert.Equal(15, commits[0].Files[0].Added + commits[0].Files[0].Deleted);
        Assert.Equal(0, commits[0].Files[1].Added); // バイナリ
    }
}
