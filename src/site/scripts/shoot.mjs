// 给官网拍界面截图：本机 Chrome 无头模式打开开发服务器（假数据），2 倍像素存成 WebP 到 src/assets/shots/。
// 先开助手界面（npm --prefix src/ui run dev，5178）和广场网站（npm --prefix src/plaza/site run dev，5180），
// 再 npm --prefix src/site run shoot [名字…]（不写名字就全拍）。做法同 docs/readme/shoot.mjs。
import { spawn } from 'node:child_process'
import { rmSync, writeFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

const out = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../src/assets/shots')
const UI = 'http://localhost:5178/'
const PLAZA = 'http://localhost:5180/'
const port = 9353
const profile = path.join(process.env.TEMP, 'stronghold-site-shoot-chrome')
const chrome = spawn('C:/Program Files/Google/Chrome/Application/chrome.exe', [
  '--headless=new', '--disable-gpu', '--no-first-run', '--hide-scrollbars',
  `--user-data-dir=${profile}`, `--remote-debugging-port=${port}`, 'about:blank',
])
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))
let target
for (let i = 0; i < 50 && !target; i++) {
  await sleep(200)
  try { target = (await (await fetch(`http://127.0.0.1:${port}/json`)).json()).find((t) => t.type === 'page') } catch {}
}
const ws = new WebSocket(target.webSocketDebuggerUrl)
await new Promise((r) => (ws.onopen = r))
let id = 0
const pending = new Map()
ws.onmessage = (e) => { const m = JSON.parse(e.data); if (pending.has(m.id)) pending.get(m.id)(m.result), pending.delete(m.id) }
const send = (method, params = {}) => new Promise((r) => (pending.set(++id, r), ws.send(JSON.stringify({ id, method, params }))))
const run = async (js) => (await send('Runtime.evaluate', { expression: `(async () => { ${js} })()`, returnByValue: true, awaitPromise: true })).result.value

await send('Page.enable')
// 假数据一打开会弹「已更新到…」卡片，先记为看过
await send('Page.addScriptToEvaluateOnNewDocument', { source: "localStorage.setItem('stronghold.seenVersion', '0.4.2')" })

const helpers = `
  const wait = (ms) => new Promise((r) => setTimeout(r, ms))
  const btn = (text, scope = document) => [...scope.querySelectorAll('button')].find((b) => b.textContent.trim().startsWith(text))
  await document.fonts.ready
  await wait(900)
`
const SIDE = { width: 380, height: 860 }
const shots = {
  match: { url: UI + '?demo&skin=terminal#match' },
  target: { url: UI + '?demo&skin=terminal#lineup' },
  plan: { url: UI + '?demo&skin=terminal#lineup', js: `btn('编辑').click(); await wait(600)` },
  codex: { url: UI + '?demo&skin=terminal#codex' },
  operator: { url: UI + '?demo&skin=terminal#codex', js: `document.querySelectorAll('.card')[1].click(); await wait(600)` },
  'skin-ursus': { url: UI + '?demo&skin=ursus#match' },
  'skin-closure': { url: UI + '?demo&skin=closure#match' },
  // 广场用网站的示例阵容（?demo），不拍线上真实用户的内容
  plaza: { url: PLAZA + '?demo', size: { width: 1180, height: 760 } },
}
const only = process.argv.slice(2)
for (const [name, s] of Object.entries(shots)) {
  if (only.length && !only.includes(name)) continue
  await send('Emulation.setDeviceMetricsOverride', { ...(s.size ?? SIDE), deviceScaleFactor: 2, mobile: false })
  // 每张带不同的查询参数，强制整页重新加载
  await send('Page.navigate', { url: s.url.replace('?', `?shot=${name}&`) })
  await sleep(1800)
  await run(helpers + (s.js ?? ''))
  const shot = await send('Page.captureScreenshot', { format: 'webp', quality: 86 })
  writeFileSync(path.join(out, name + '.webp'), Buffer.from(shot.data, 'base64'))
  console.log(name)
}
ws.close()
chrome.kill()
await new Promise((r) => chrome.once('exit', r))
rmSync(profile, { recursive: true, force: true, maxRetries: 5 })
