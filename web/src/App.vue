<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import type { GroupStats, Report } from './types'

const BASE = import.meta.env.BASE_URL
const CONTACT = 'info@mukai.works'

const reports = ref<Report[]>([])
const selected = ref('')
const error = ref('')

onMounted(async () => {
  try {
    const res = await fetch(`${BASE}data/index.json`)
    if (!res.ok) throw new Error(`index.json を読めません（${res.status}）`)
    const files: string[] = await res.json()
    reports.value = await Promise.all(files.map(async (f) => (await fetch(`${BASE}data/${f}`)).json()))
    // AI の印付きコミットが多いものを先に
    reports.value.sort((a, b) => (b.history.ai.commits ?? 0) - (a.history.ai.commits ?? 0))
    selected.value = reports.value[0]?.id ?? ''
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  }
})

const r = computed(() => reports.value.find((x) => x.id === selected.value))

const pct = (v: number | null) => (v === null ? '—' : `${Math.round(v * 100)}%`)
const num = (v: number | null) => (v === null ? '—' : v.toLocaleString('ja-JP'))
const date = (s: string | null) => (s ? new Date(s).toLocaleDateString('ja-JP') : '—')

/** 件数が少ないときは比率の比較に意味が薄いので注記する */
const SMALL = 10
const smallNote = (g: GroupStats) => (g.codeCommits > 0 && g.codeCommits < SMALL ? `（${g.codeCommits}件だけ）` : '')

const metrics = computed(() => {
  const h = r.value?.history
  if (!h) return []
  return [
    {
      key: 'test',
      title: 'テストも一緒に変えた割合',
      note: 'コードを変えたコミットのうち、テストのファイルも変えたもの。高いほどよい',
      ai: pct(h.ai.testTouchRate),
      human: pct(h.human.testTouchRate),
    },
    {
      key: 'rework',
      title: `手戻り率（${h.reworkWindowDays}日以内）`,
      note: `変えたコードのファイルを、${h.reworkWindowDays}日以内に別のコミットがまた変えた割合。低いほど一度で決まっている。期間の終わり${h.reworkWindowDays}日は判定しない`,
      ai: pct(h.ai.reworkRate),
      human: pct(h.human.reworkRate),
    },
    {
      key: 'size',
      title: '1コミットの変更行数（中央値）',
      note: `大きいほどレビューが重い。${h.largeCommitLines}行以上のコミット: AI ${h.ai.largeCommits}件・人 ${h.human.largeCommits}件`,
      ai: num(h.ai.medianLinesChanged),
      human: num(h.human.medianLinesChanged),
    },
  ]
})

const statusLabel = { Ok: 'できている', Warn: '確認したい', Missing: '無い' } as const
const readyCount = computed(() => r.value?.readiness.filter((c) => c.status === 'Ok').length ?? 0)
</script>

<template>
  <header class="top">
    <div class="wrap top-inner">
      <div class="brand">
        <span class="logo" aria-hidden="true">ヘ</span>
        <span><strong>ヘルピア</strong><small>AI駆動開発の現場点検</small></span>
      </div>
      <nav class="studio-nav" aria-label="作った人">
        <a class="studio" href="https://mukai.works/products">ほかの製品</a>
        <a class="studio" href="https://mukai.works/engineer">開発者のプロフィール</a>
      </nav>
    </div>
  </header>

  <main class="wrap">
    <section class="hero">
      <h1>AIが書いたコードが、<br />チームの負担になっていないかを数字で見る。</h1>
      <p class="lead">
        Gitの履歴から、AIが関わったコミット（Co-Authored-By などの印）を見分け、人のコミットと並べて「テストも一緒に変えているか」「数日以内に直し直していないか」「1回の変更が大きすぎないか」を数えます。あわせて、リポジトリがAIに任せられる状態か（指示書・テスト・CIなど）を点検します。
      </p>
      <p class="note">
        判定にAIは使っていません。決まった規則で数えるので、誰が回しても同じ数字になります。下の例は、作者が自分のリポジトリを点検した結果です（2026-11-27）。堺市立斎場ガイドはコードを公開していないリポジトリで、数字だけを載せています。
      </p>
    </section>

    <p v-if="error" class="error">結果を読み込めませんでした: {{ error }}</p>

    <section v-if="reports.length" class="block">
      <div class="tabs" role="tablist" aria-label="点検したリポジトリ">
        <button
          v-for="x in reports"
          :key="x.id"
          role="tab"
          :aria-selected="x.id === selected"
          @click="selected = x.id"
        >
          {{ x.name }}
        </button>
      </div>
    </section>

    <template v-if="r">
      <section class="block">
        <h2>AIが関わったコミット</h2>
        <div class="share">
          <div class="share-num">
            <strong>{{ pct(r.history.aiShare) }}</strong>
            <span>{{ r.history.ai.commits }} / {{ r.history.totalCommits }} コミット</span>
          </div>
          <p class="share-note">
            期間 {{ date(r.history.from) }} 〜 {{ date(r.history.to) }}（マージを除く）。
            <template v-if="Object.keys(r.history.ai.tools).length">
              印の内訳: <span v-for="(n, t) in r.history.ai.tools" :key="t" class="tool">{{ t }} {{ n }}件</span>
            </template>
            <br />
            印を付けない運用のコミットは見分けられないので、この割合は<strong>下限</strong>です。
            <template v-if="r.history.ai.commits === 0">
              このリポジトリは AI を使って開発していますが、コミットに印を付けない運用のため 0件になっています。
            </template>
          </p>
        </div>
      </section>

      <section class="block">
        <h2>AIのコミットと人のコミットを比べる</h2>
        <div class="table-scroll">
          <table class="cmp">
            <thead>
              <tr>
                <th scope="col">指標</th>
                <th scope="col">AIが関わったコミット{{ smallNote(r.history.ai) }}</th>
                <th scope="col">人だけのコミット{{ smallNote(r.history.human) }}</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="m in metrics" :key="m.key">
                <th scope="row">
                  {{ m.title }}
                  <small>{{ m.note }}</small>
                </th>
                <td>{{ m.ai }}</td>
                <td>{{ m.human }}</td>
              </tr>
            </tbody>
          </table>
        </div>
        <p class="caption">
          分母はコードの変更を含むコミットだけです（AI {{ r.history.ai.codeCommits }}件・人 {{ r.history.human.codeCommits }}件）。文書だけの変更は外しています。
          件数が{{ SMALL }}件未満の側は、比率を比べても差があるとは言えません。「—」は数えられる件数が無いことを表します。
        </p>
      </section>

      <section class="block">
        <h2>AIに任せられる状態か（{{ readyCount }} / {{ r.readiness.length }} 項目ができている）</h2>
        <ul class="checks">
          <li v-for="c in r.readiness" :key="c.id" :class="`c-${c.status.toLowerCase()}`">
            <span class="badge">{{ statusLabel[c.status] }}</span>
            <div>
              <strong>{{ c.title }}</strong>
              <p class="detail">{{ c.detail }}</p>
              <p v-if="c.advice" class="advice">{{ c.advice }}</p>
            </div>
          </li>
        </ul>
        <p class="caption">点数は付けていません。項目ごとの重みに根拠が無いためです。秘密情報は、よく漏れる鍵の形で探しているだけで、見つからなくても安全とは言えません。</p>
      </section>
    </template>

    <section class="block two">
      <div>
        <h2>数えていること</h2>
        <ul>
          <li>AIの印: コミットの Co-Authored-By に Claude・Copilot・Cursor・Codex・Gemini などがある、または「Generated with Claude Code」などの記載がある</li>
          <li>テスト: test・tests・spec・__tests__ のフォルダや、*.test.ts・*_test.go・*Tests.cs などのファイル</li>
          <li>手戻り: 同じコードのファイルを、7日以内に別のコミットが変えたら1件</li>
        </ul>
      </div>
      <div>
        <h2>数えていないこと</h2>
        <ul>
          <li>印の無いコミットにAIが関わったかどうか</li>
          <li>手戻りの理由（不具合の修正か、予定どおりの続きの作業か）は見分けていません</li>
          <li>コードの質そのもの。数字はレビューの重さや確かめ方の目安です</li>
        </ul>
      </div>
    </section>

    <section class="block cta">
      <h2>御社のリポジトリで点検しませんか</h2>
      <p>
        点検は御社の手元で動かせます（コードを外に送りません）。数字を見たうえで、AIへの指示書・テスト・CIの整え方や、AIの変更を人がどこまで読むかの決め方まで一緒に考えます。
      </p>
      <div class="cta-row">
        <a class="button" :href="`mailto:${CONTACT}?subject=${encodeURIComponent('ヘルピアについて')}`">{{ CONTACT }} に相談する</a>
        <a class="sub-link" href="https://mukai.works/engineer">作った人の経歴を見る</a>
      </div>
    </section>
  </main>

  <footer class="foot">
    <div class="wrap">
      ヘルピア — <a href="https://mukai.works">ムカイクリエイトスタジオ（ムカイ）</a> ／
      <a href="https://github.com/mukai-work/helpia">コード（GitHub）</a> ／
      解析エンジンは C#（.NET 10）、この画面は Vue 3 で作っています。画面はあらかじめ数えた結果を表示するだけです。
    </div>
  </footer>
</template>
