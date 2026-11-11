import { defineConfig } from 'vite'
import { svelte } from '@sveltejs/vite-plugin-svelte'

// base 用相对路径：产品里由 WebView2 宿主从本地目录加载构建产物
export default defineConfig({
  plugins: [svelte()],
  base: './',
  server: { port: 5178 },
})
