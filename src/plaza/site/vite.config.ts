import { defineConfig } from 'vite'
import { svelte } from '@sveltejs/vite-plugin-svelte'

// 阵容广场网站。游戏素材直接用助手界面的 public/assets；本地开发时 /api 转发到线上后端，
// 去掉 Origin 头，免得被后端的来源校验拦下
export default defineConfig({
  plugins: [svelte()],
  publicDir: '../../ui/public',
  build: { outDir: 'dist', emptyOutDir: true },
  server: {
    port: 5180,
    // 字体、图鉴数据、接口类型在网站目录之外（助手界面和广场后端），开发时放开到 src/
    fs: { allow: ['../..'] },
    proxy: {
      '/api': {
        target: 'https://sa.lubeiluchen.com',
        changeOrigin: true,
        configure: (proxy) => proxy.on('proxyReq', (req) => req.removeHeader('origin')),
      },
    },
  },
})
