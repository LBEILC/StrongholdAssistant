import { defineConfig, normalizePath } from 'vite'
import { svelte } from '@sveltejs/vite-plugin-svelte'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

const assistantCss = normalizePath(fileURLToPath(new URL('../../ui/src/app.css', import.meta.url)))

// 阵容广场网站。游戏素材直接用助手界面的 public/assets；本地开发时 /api 转发到线上后端，
// 去掉 Origin 头，免得被后端的来源校验拦下
export default defineConfig({
  plugins: [{
    name: 'plaza-font-subsets',
    enforce: 'pre',
    transform(code, id) {
      if (normalizePath(id.split('?')[0]) !== assistantCss) return
      // 只移除网站引用到的整包声明，助手源文件与其他字体不变。
      return code.replace(/@font-face\s*\{[^}]*\}/g, (face) =>
        /font-family:\s*['"]MiSans['"]/.test(face) ? '' : face)
    },
  }, svelte(), {
    name: 'plaza-headers',
    generateBundle() {
      // publicDir 复用助手素材；网站独有的 Pages 规则另行加入产物。
      this.emitFile({ type: 'asset', fileName: '_headers', source: readFileSync(new URL('./public/_headers', import.meta.url), 'utf8') })
      // 验证码邮件里的 logo，要固定地址（不带哈希）
      this.emitFile({ type: 'asset', fileName: 'mail/logo.png', source: readFileSync(new URL('./public/mail/logo.png', import.meta.url)) })
    },
  }],
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
