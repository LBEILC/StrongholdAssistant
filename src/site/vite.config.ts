import { defineConfig, normalizePath, type Plugin } from 'vite'
import { svelte } from '@sveltejs/vite-plugin-svelte'
import { cpSync, existsSync, readFileSync, readdirSync, statSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

// 官网：纯静态多页。设计令牌、游戏素材、字体切片都直接用助手界面和广场网站的，不另存一份。
const here = (p: string) => fileURLToPath(new URL(p, import.meta.url))
const assistantCss = normalizePath(here('../ui/src/app.css'))
const uiDist = here('../ui/dist')

// 首页数字和盟约图标排：构建时从助手的图鉴数据算出来，免得把整份图鉴打进页面
const catalog = JSON.parse(readFileSync(here('../ui/src/lib/mock/catalog.json'), 'utf8'))
const stats = {
  operators: catalog.operators.filter((o: { obtain: unknown; diy: boolean }) => !o.obtain && !o.diy).length,
  equipment: catalog.equipment.length,
  covenants: catalog.covenants.length,
  strategies: catalog.strategies.length,
  bonds: catalog.covenants.map((c: { id: string; name: string; kind: string }) => ({ id: c.id, name: c.name, kind: c.kind })),
}

// 网页里的侧栏演示 = 助手界面的构建产物（浏览器里自动用假数据），放在 /demo/。
// 开发时直接把 src/ui/dist 挂到 /demo/，构建时整个复制过去；所以先构建一次助手界面。
function demo(): Plugin {
  return {
    name: 'site-demo',
    configureServer(server) {
      server.middlewares.use('/demo', (req, res, next) => {
        const rel = decodeURIComponent((req.url ?? '/').split('?')[0])
        let file = path.join(uiDist, rel)
        if (existsSync(file) && statSync(file).isDirectory()) file = path.join(file, 'index.html')
        if (!existsSync(file)) return next()
        const type = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.woff2': 'font/woff2', '.png': 'image/png', '.webp': 'image/webp', '.json': 'application/json', '.svg': 'image/svg+xml' }[path.extname(file)]
        if (type) res.setHeader('Content-Type', type)
        res.end(readFileSync(file))
      })
    },
    closeBundle() {
      cpSync(uiDist, here('./dist/demo'), { recursive: true })
    },
  }
}

const pages = Object.fromEntries(
  ['index.html', ...readdirSync(here('.')).filter((d) => existsSync(here(`./${d}/index.html`)) && !['dist', 'node_modules', 'public'].includes(d)).map((d) => `${d}/index.html`)].map((f) => [f.replace(/\/?index\.html$/, '') || 'home', here(`./${f}`)]),
)

export default defineConfig({
  plugins: [
    {
      name: 'site-font-subsets',
      enforce: 'pre',
      transform(code, id) {
        if (normalizePath(id.split('?')[0]) !== assistantCss) return
        // 助手打包的是整份 MiSans（2.6MB），网站换成广场的切片字体，只去掉那一条声明
        return code.replace(/@font-face\s*\{[^}]*\}/g, (face) => (/font-family:\s*['"]MiSans['"]/.test(face) ? '' : face))
      },
    },
    svelte(),
    demo(),
  ],
  define: { __STATS__: JSON.stringify(stats) },
  publicDir: '../ui/public',
  build: { outDir: 'dist', emptyOutDir: true, rollupOptions: { input: pages } },
  server: { port: 5182, fs: { allow: ['../..'] } },
})
