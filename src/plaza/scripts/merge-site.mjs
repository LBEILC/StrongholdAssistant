// 把官网（src/site/dist）合进广场的部署产物（site/dist），同一个 Pages 项目、同一个域名（2026-11-14 用户定）：
// 官网占根路径和 /changelog、/faq、/about、/demo；广场首页挪到 /plaza，广场自己的页面由 _redirects 交给 plaza.html。
// 两边共用助手的 public 素材和字体切片，同名文件内容必须一致，不一致就报错停下。
import { cpSync, existsSync, readFileSync, readdirSync, renameSync, statSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

const here = (p) => fileURLToPath(new URL(p, import.meta.url))
const plaza = here('../site/dist')
const site = here('../../site/dist')
if (!existsSync(join(site, 'index.html'))) throw new Error('官网还没构建：先跑 npm --prefix src/site run build')

renameSync(join(plaza, 'index.html'), join(plaza, 'plaza.html'))

const clashes = []
const walk = (dir) => {
  for (const name of readdirSync(join(site, dir))) {
    const rel = join(dir, name)
    if (statSync(join(site, rel)).isDirectory()) walk(rel)
    else if (existsSync(join(plaza, rel)) && !readFileSync(join(plaza, rel)).equals(readFileSync(join(site, rel)))) clashes.push(rel)
  }
}
walk('')
if (clashes.length) throw new Error('官网和广场有同名但内容不同的文件：\n' + clashes.join('\n'))
cpSync(site, plaza, { recursive: true })

// 广场的前端路由（router.svelte.ts）；/api 归 Functions，不用写
const routes = ['/plaza/*', '/p/*', '/new', '/me', '/login', '/privacy', '/admin', '/admin/*', '/rate']
writeFileSync(join(plaza, '_redirects'), routes.map((r) => `${r} /plaza 200`).join('\n') + '\n')
console.log('已合并官网：plaza.html + 官网 ' + readdirSync(site).length + ' 项，_redirects ' + routes.length + ' 条')
