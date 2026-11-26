import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// 単体公開は相対パス './'、mukai.works 配下に載せるときは HELPIA_BASE=/helpia/ でビルドする
export default defineConfig({
  plugins: [vue()],
  base: process.env.HELPIA_BASE ?? './',
})
