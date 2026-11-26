// ../reports の点検結果を public/data に写す（画面は静的JSONを読むだけ）
import { cpSync, mkdirSync, rmSync, existsSync } from 'node:fs'
const src = new URL('../../reports/', import.meta.url)
const dst = new URL('../public/data/', import.meta.url)
if (!existsSync(new URL('index.json', src))) {
  console.error('reports/index.json がありません。先に helpia index を実行してください')
  process.exit(1)
}
rmSync(dst, { recursive: true, force: true })
mkdirSync(dst, { recursive: true })
cpSync(src, dst, { recursive: true })
console.log('点検結果を public/data に写しました')
