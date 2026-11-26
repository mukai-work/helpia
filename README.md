# ヘルピア（HELPIA）— AI駆動開発の現場点検

**デモ: https://mukai.works/helpia**（作者の公開リポジトリを点検した結果・ログイン不要）

AIが書いたコードが、チームの負担になっていないかを数字で見るための道具です。

1. **AIのコミットと人のコミットを比べる** — Git の履歴から、AIが関わったコミット（`Co-Authored-By: Claude` などの印）を見分け、人のコミットと並べて次を数えます
   - テストも一緒に変えた割合
   - 手戻り率（変えたコードのファイルを、7日以内に別のコミットがまた変えた割合）
   - 1コミットの変更行数（中央値）と、500行以上の大きなコミットの数
2. **AIに任せられる状態かを点検する** — AIへの指示書（CLAUDE.md・AGENTS.md など）、READMEのセットアップ手順、テスト、CI、書式・静的解析、依存のロック、環境変数の見本、秘密情報らしき文字列

判定に AI は使っていません。決まった規則で数えるので、誰が回しても同じ数字になります。コードを外に送らず、手元で動きます。

## 使い方

```bash
cd engine
dotnet run --project Helpia.Cli -c Release -- scan --repo ../path/to/repo --id myrepo --name "表示名" --out ../reports
dotnet run --project Helpia.Cli -c Release -- index --dir ../reports

cd ../web && npm install && npm run dev   # ../reports を表示
```

## 数えていること・数えていないこと

| 数えている | 数えていない |
|---|---|
| AIの印: Co-Authored-By に Claude / Anthropic / Copilot / Cursor / Codex / OpenAI / Gemini / Devin / Aider / ChatGPT、または「Generated with Claude Code」などの記載 | 印の無いコミットにAIが関わったか（＝AIの割合は下限値） |
| テスト: `test/` `tests/` `spec/` `__tests__/` や `*.test.ts` `*_test.go` `*Tests.cs` など | 手戻りの理由（不具合の修正か、予定どおりの続きか） |
| 手戻り: 同じコードのファイルを7日以内に別のコミットが変えたら1件（期間の終わり7日は判定しない） | コードの質そのもの |
| 分母はコードの変更を含むコミットだけ（文書だけの変更は外す） | |

比率の分母が 0 のときは「—」と表示します（0% と混同しないため）。件数が少ない側の比率は、差があるとは言えません。

## 構成

- `engine/` C#（.NET 10）。`Helpia.Core`（履歴の読み取り・分類・集計・点検）、`Helpia.Cli`、`Helpia.Tests`（xUnit）
- `web/` Vue 3 + Vite の静的ページ。点検結果の JSON を表示するだけ
- `reports/` デモに使っている点検結果

```bash
cd engine && dotnet test
```

---
ムカイクリエイトスタジオ — https://mukai.works ／ ご相談: info@mukai.works
