import { after, before, beforeEach, test } from 'node:test'
import assert from 'node:assert/strict'
import { createHmac } from 'node:crypto'
import { readFile } from 'node:fs/promises'
import { build } from 'esbuild'
import { Miniflare } from 'miniflare'
import { exportJWK, generateKeyPair, SignJWT } from 'jose'

const built = await build({
 stdin: { contents: "export * from './server/api'; export * from './server/catalog'; export { ipSource } from './server/common'; export { signMail } from './server/mail'", resolveDir: process.cwd(), loader: 'ts' },
 bundle: true, platform: 'neutral', format: 'esm', write: false, loader: { '.txt': 'text' },
})
const { handleRequest, catalog, encodeLineup, fingerprint, ipSource, signMail } = await import('data:text/javascript;base64,' + Buffer.from(built.outputFiles[0].text).toString('base64'))
const originalFetch = globalThis.fetch
let mf, db, env, privateKey, jwk
const mails = new Map()
let serial = 0

before(async () => {
 mf = new Miniflare({ modules: true, script: 'export default { fetch() { return new Response() } }', d1Databases: ['DB'], compatibilityDate: '2025-10-11' })
 db = await mf.getD1Database('DB')
 for (const file of ['0001_init.sql', '0002_password.sql', '0003_comments_reports.sql', '0004_sync_favorites_income.sql', '0005_server_privacy.sql']) {
  const sql = await readFile('migrations/' + file, 'utf8')
  // D1 exec 按行分语句；保留触发器整体，迁移一次执行。
  await db.exec(sql.replace(/\r?\n/g, ' '))
 }
 const pair = await generateKeyPair('RS256')
 privateKey = pair.privateKey
 jwk = { ...await exportJWK(pair.publicKey), kid: 'test-key', alg: 'RS256', use: 'sig' }
 env = {
  DB: db, IP_SECRET: 'test-only-ip-secret', CODE_PEPPER: 'test-only-pepper', RESEND_API_KEY: 'test-only-key', MAIL_FROM: 'test@example.com',
  TURNSTILE_SECRET: 'test-only-secret', ADMIN_EMAILS: 'admin@example.com',
  ACCESS_TEAM_DOMAIN: 'test.cloudflareaccess.com', ACCESS_AUD: 'test-audience',
  ALLOWED_ORIGINS: 'https://app.local,http://localhost:5178',
 }
 globalThis.fetch = async (url, options) => {
  const href = String(url)
  if (href === 'https://challenges.cloudflare.com/turnstile/v0/siteverify') {
   assert.equal(options.method, 'POST')
   assert.equal(options.body.get('secret'), env.TURNSTILE_SECRET)
   return Response.json({ success: true })
  }
  if (href === 'https://api.resend.com/emails') {
   const mail = JSON.parse(options.body)
   assert.equal(options.headers.Authorization, 'Bearer ' + env.RESEND_API_KEY)
   assert.match(mail.text, /10 分钟内有效/)
   mails.set(mail.to[0], mail.subject.match(/\d{6}/)[0])
   return Response.json({ id: 'test-email' })
  }
  if (href === 'https://test.cloudflareaccess.com/cdn-cgi/access/certs') return Response.json({ keys: [jwk] })
  throw new Error('测试不应访问外网：' + href)
 }
})
beforeEach(async () => {
 await db.batch(['favorites', 'cloud_lineups', 'cloud_prefs', 'sync_usage', 'income_reports', 'income_samples', 'income_summary', 'reports', 'report_usage', 'comments', 'likes', 'imports', 'posts', 'sessions', 'login_codes', 'password_attempts', 'users'].map(t => db.prepare('DELETE FROM ' + t)))
 mails.clear()
})
after(async () => {
 globalThis.fetch = originalFetch
 await mf?.dispose()
})

async function request(path, { method = 'GET', body, token, headers = {} } = {}) {
 const response = await handleRequest(new Request('https://plaza.example' + path, {
  method, headers: { ...(body && { 'Content-Type': 'application/json' }), ...(token && { Authorization: 'Bearer ' + token }), ...headers },
  ...(body && { body: JSON.stringify(body) }),
 }), env)
 const data = response.status === 204 ? null : await response.json()
 return { status: response.status, data, headers: response.headers }
}
async function ok(path, options = {}) {
 const result = await request(path, options)
 assert.equal(result.status, 200, JSON.stringify(result.data))
 return result.data
}
async function codeFor(email) {
 await ok('/api/auth/code', { method: 'POST', body: { email, turnstileToken: 'test-token' } })
 return mails.get(email.toLowerCase())
}
async function login(nickname = '测试玩家' + ++serial) {
 const email = 'player' + ++serial + '@example.com'
 const code = await codeFor(email)
 const result = await ok('/api/auth/verify', { method: 'POST', body: { email, code } })
 if (nickname) result.me = await ok('/api/me', { method: 'PATCH', token: result.token, body: { nickname } })
 return { ...result, email }
}
function lineup(name = '验收阵容', goals = catalog.covenants.slice(0, 2).map((c, i) => ({ id: c.id, level: i + 1 }))) {
 return {
  id: '', name, goals,
  members: catalog.operators.slice(0, 8).map((o, i) => ({ name: o.name, ...(i === 0 && { key: true }) })),
  equipment: catalog.equipment.slice(0, 2).map(e => e.name), strategyId: catalog.strategies[0].id,
  source: 'user', updatedAt: '',
 }
}
async function publish(user, name, goals) {
 return ok('/api/posts', { method: 'POST', token: user.token, body: { code: encodeLineup(lineup(name, goals), catalog), description: '验收简介' } })
}
async function access(email) {
 return { 'Cf-Access-Jwt-Assertion': await new SignJWT({ email }).setProtectedHeader({ alg: 'RS256', kid: 'test-key' })
  .setIssuer('https://' + env.ACCESS_TEAM_DOMAIN).setAudience(env.ACCESS_AUD).setIssuedAt().setExpirationTime('1h').sign(privateKey) }
}

test('102 签名匹配阿里云官方 POST 示例', async () => {
 // https://help.aliyun.com/zh/direct-mail/signature；固定示例含需按 RFC 3986 编码的特殊字符。
 const params = {
  Version: '2015-11-23', ToAddress: '1@test.com', Timestamp: '2016-10-20T06:27:56Z',
  TagName: '2', Subject: '3', SignatureVersion: '1.0',
  SignatureNonce: 'c1b2c332-4cfb-4a0f-b8cc-ebe622aa0a5c', SignatureMethod: 'HMAC-SHA1',
  ReplyToAddress: 'true', RegionId: 'cn-hangzhou', HtmlBody: '4', Format: 'XML',
  AddressType: '1', Action: 'SingleSendMail', AccountName: "<a%b'>", AccessKeyId: 'testid',
 }
 assert.equal(await signMail(params, 'testsecret'), 'llJfXJjBW3OacrVgxxsITgYaYm0=')
})

const aliyunConfig = {
 ALIYUN_DM_ACCESS_KEY_ID: 'test-only-id', ALIYUN_DM_ACCESS_KEY_SECRET: 'test-only-secret',
 ALIYUN_DM_ACCOUNT: 'noreply@example.com',
}
for (const configured of [false, true]) test('102 配置切换发信服务：' + (configured ? '阿里云' : 'Resend'), async t => {
 const fetch = globalThis.fetch
 let sends = 0
 t.mock.method(globalThis, 'fetch', async (url, options) => {
  if (String(url).includes('turnstile')) return fetch(url, options)
  sends++
  assert.equal(String(url), configured ? 'https://dm.aliyuncs.com/' : 'https://api.resend.com/emails')
  if (!configured) return fetch(url, options)
  assert.equal(options.method, 'POST')
  assert.equal(options.headers['Content-Type'], 'application/x-www-form-urlencoded')
  const params = Object.fromEntries(new URLSearchParams(options.body))
  for (const [key, value] of Object.entries({
   AccessKeyId: aliyunConfig.ALIYUN_DM_ACCESS_KEY_ID, AccountName: aliyunConfig.ALIYUN_DM_ACCOUNT,
   Action: 'SingleSendMail', AddressType: '1', ReplyToAddress: 'false', ToAddress: 'mail@example.com',
   FromAlias: '卫戍协议助手', ClickTrace: '0', Format: 'JSON', Version: '2015-11-23',
   RegionId: 'cn-hangzhou', SignatureMethod: 'HMAC-SHA1', SignatureVersion: '1.0',
  })) assert.equal(params[key], value)
  assert.match(params.Subject, /^卫戍协议助手验证码：\d{6}$/)
  const code = params.Subject.slice(-6)
  assert.equal(params.TextBody, `你的验证码是 ${code}，10 分钟内有效。\n如果不是你本人在登录卫戍协议助手的阵容广场，请忽略这封邮件。`)
  assert.match(params.Timestamp, /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$/)
  assert.match(params.SignatureNonce, /^[\da-f-]{36}$/)
  const { Signature, ...unsigned } = params
  assert.equal(Signature, await signMail(unsigned, aliyunConfig.ALIYUN_DM_ACCESS_KEY_SECRET))
  return Response.json({ EnvId: 'test-mail', RequestId: 'test-request' })
 })
 const response = await handleRequest(new Request('https://plaza.example/api/auth/code', {
  method: 'POST', body: JSON.stringify({ email: 'mail@example.com', turnstileToken: 'test-token' }),
 }), { ...env, ...(configured ? aliyunConfig : {}) })
 assert.equal(response.status, 200)
 assert.deepEqual(await response.json(), { retryAfter: 60 })
 assert.equal(sends, 1)
})

for (const scenario of [
 { code: 'InvalidQuota', status: 429 },
 { code: 'Throttling.User', status: 429 },
 { code: 'InvalidMailAddress.NotFound', status: 424 },
 { code: 'InvalidSendMail.Spam', status: 424 },
]) test('102 阿里云错误映射：' + scenario.code, async t => {
 const fetch = globalThis.fetch
 let sends = 0
 t.mock.method(globalThis, 'fetch', async (url, options) => {
  if (String(url).includes('turnstile')) return fetch(url, options)
  assert.equal(String(url), 'https://dm.aliyuncs.com/')
  sends++
  return Response.json({ Code: scenario.code, Message: '模拟服务端失败' }, { status: 400 })
 })
 const response = await handleRequest(new Request('https://plaza.example/api/auth/code', {
  method: 'POST', body: JSON.stringify({ email: 'error@example.com', turnstileToken: 'test-token' }),
 }), { ...env, ...aliyunConfig })
 assert.equal(response.status, scenario.status)
 const result = await response.json()
 assert.equal(result.error, scenario.status === 429 ? 'mail_quota' : 'mail_failed')
 if (scenario.status === 429) assert.match(result.message, /^今天能发的验证码邮件用完了/)
 assert.equal(sends, 1)
})

test('101 正式域名的 POST 经 pages.dev 回源可通过来源校验', async () => {
 const config = await readFile('wrangler.toml', 'utf8')
 const allowed = config.match(/^ALLOWED_ORIGINS = "([^"]+)"/m)[1]
 const response = await handleRequest(new Request('https://stronghold-plaza.pages.dev/api/auth/code', {
  method: 'POST', headers: { Origin: 'https://sa.lubeiluchen.com', 'Content-Type': 'application/json' },
  body: JSON.stringify({ email: 'esa-origin@example.com', turnstileToken: 'test-token' }),
 }), { ...env, ALLOWED_ORIGINS: allowed })
 assert.equal(response.status, 200)
 assert.equal(response.headers.get('Access-Control-Allow-Origin'), 'https://sa.lubeiluchen.com')
})

for (const scenario of [
 { name: '可信回源', secret: 'test-edge-secret', edge: 'test-edge-secret', expected: '192.0.2.10' },
 { name: '回源头错误', secret: 'test-edge-secret', edge: 'wrong', expected: '192.0.2.20' },
 { name: '回源头缺失', secret: 'test-edge-secret', expected: '192.0.2.20' },
 { name: '未配置密钥', edge: 'test-edge-secret', expected: '192.0.2.20' },
 { name: '未配置密钥且无 CF IP', edge: 'test-edge-secret', noCf: true, expected: 'local' },
]) test('101 ' + scenario.name + '使用预期 IP 限流、验证和生成来源', async t => {
 const edgeEnv = { ...env, EDGE_SECRET: scenario.secret }
 const headers = {
  'Content-Type': 'application/json', 'ali-real-client-ip': ' 192.0.2.10 ',
  ...(!scenario.noCf && { 'CF-Connecting-IP': '192.0.2.20' }),
  ...(scenario.edge && { 'X-Plaza-Edge': scenario.edge }),
 }
 const email = 'esa@example.com'
 const makeRequest = (path, body) => new Request('https://stronghold-plaza.pages.dev' + path, {
  method: 'POST', headers, body: JSON.stringify(body),
 })
 const fetch = globalThis.fetch
 t.mock.method(globalThis, 'fetch', async (url, options) => {
  if (String(url) === 'https://challenges.cloudflare.com/turnstile/v0/siteverify')
   assert.equal(options.body.get('remoteip'), scenario.expected)
  return fetch(url, options)
 })
 const codeRequest = makeRequest('/api/auth/code', { email, turnstileToken: 'test-token' })
 assert.equal((await handleRequest(codeRequest, edgeEnv)).status, 200)
 assert.equal((await db.prepare('SELECT ip FROM login_codes WHERE email=?').bind(email).first()).ip, scenario.expected)
 const passwordRequest = () => makeRequest('/api/auth/password', { email, password: 'wrong-password', turnstileToken: 'test-token' })
 assert.equal((await handleRequest(passwordRequest(), edgeEnv)).status, 401)
 assert.equal((await db.prepare('SELECT ip FROM password_attempts WHERE email=?').bind(email).first()).ip, scenario.expected)
 // 用不同邮箱的失败记录占满同一 IP，确认限额查询也采用该 IP。
 await db.batch(Array.from({ length: 29 }, (_, i) => db.prepare(
  'INSERT INTO password_attempts(email,ip,created_at,success) VALUES(?,?,?,0)',
 ).bind(`esa-${i}@example.com`, scenario.expected, new Date().toISOString())))
 assert.equal((await handleRequest(passwordRequest(), edgeEnv)).status, 429)
 const now = Date.parse('2026-11-14T12:00:00Z')
 for (const purpose of ['income', 'import']) {
  const expected = createHmac('sha256', env.IP_SECRET).update(`${purpose}:2026-11-14:${scenario.expected}`).digest('hex')
  assert.equal(await ipSource(edgeEnv, codeRequest, purpose, now), expected)
 }
})

test('登录、错误码、昵称、Cookie、登出、60 秒限频与第六次失效', async () => {
 const email = 'account@example.com', code = await codeFor(email.toUpperCase())
 let result = await request('/api/auth/code', { method: 'POST', body: { email, turnstileToken: 'test-token' } })
 assert.equal(result.status, 429); assert.equal(result.data.error, 'rate_limited')
 result = await request('/api/auth/verify', { method: 'POST', body: { email, code: 'wrong' } })
 assert.equal(result.status, 400); assert.equal(result.data.message, '验证码不对，请再检查一下。')
 result = await request('/api/auth/verify', { method: 'POST', body: { email, code } })
 assert.equal(result.status, 200)
 const { token, me } = result.data
 assert.equal(me.nickname, null)
 assert.match(result.headers.get('Set-Cookie'), /HttpOnly; Secure; SameSite=Lax; Path=\/; Max-Age=2592000/)
 assert.equal((await ok('/api/me', { token })).id, me.id)
 assert.equal((await ok('/api/me', { headers: { Cookie: 'sa_session=' + token } })).emailMasked, 'ac***@example.com')
 assert.equal((await ok('/api/me', { method: 'PATCH', token, body: { nickname: '测试昵称' } })).nickname, '测试昵称')
 assert.equal((await request('/api/auth/logout', { method: 'POST', token })).status, 204)
 assert.equal((await request('/api/me', { token })).status, 401)
 const other = 'attempts@example.com', otherCode = await codeFor(other)
 for (let i = 0; i < 5; i++) assert.equal((await request('/api/auth/verify', { method: 'POST', body: { email: other, code: 'wrong' } })).data.message, '验证码不对，请再检查一下。')
 result = await request('/api/auth/verify', { method: 'POST', body: { email: other, code: otherCode } })
 assert.equal(result.status, 400); assert.equal(result.data.message, '验证码已失效，请重新获取。')
})

test('发布内容一致、指纹和敏感词拒绝、重复发布更新简介', async () => {
 const user = await login(), original = lineup(), code = encodeLineup(original, catalog)
 const post = await publish(user)
 const { id, source, updatedAt, ...expected } = original
 assert.deepEqual(post.lineup, expected)
 assert.equal(post.current, true)
 assert.deepEqual((await ok('/api/posts')).posts[0], post)
 const bytes = Buffer.from(code.slice(4), 'base64url'); bytes[1] ^= 1
 let result = await request('/api/posts', { method: 'POST', token: user.token, body: { code: 'SA1-' + bytes.toString('base64url'), description: '' } })
 assert.equal(result.status, 400); assert.equal(result.data.error, 'season_mismatch')
 result = await request('/api/posts', { method: 'POST', token: user.token, body: { code, description: '加 Q Q' } })
 assert.equal(result.status, 400); assert.equal(result.data.error, 'blocked_words')
 const same = await ok('/api/posts', { method: 'POST', token: user.token, body: { code, description: '更新后的简介' } })
 assert.equal(same.id, post.id); assert.equal(same.description, '更新后的简介')
 assert.equal((await ok('/api/posts')).posts.length, 1)
 // SA2：干员带候选装备和优先标记
 const withEq = lineup('带装备的阵容')
 withEq.members[0].equipment = [{ name: catalog.equipment[3].name, priority: true }, { name: catalog.equipment[5].name, priority: false }]
 const code2 = encodeLineup(withEq, catalog)
 assert.match(code2, /^SA2-/)
 const post2 = await ok('/api/posts', { method: 'POST', token: user.token, body: { code: code2, description: '' } })
 assert.equal(post2.code, code2)
 assert.deepEqual(post2.lineup.members[0].equipment, withEq.members[0].equipment)
})

test('065 作者修改分享码更新阵容并保留点赞', async () => {
 const user = await login(), post = await publish(user)
 await ok('/api/posts/' + post.id + '/like', { method: 'POST', token: user.token })
 const replacement = lineup('修改后的阵容')
 replacement.members = replacement.members.slice(0, 3)
 const code = encodeLineup(replacement, catalog)
 const updated = await ok('/api/posts/' + post.id, { method: 'PATCH', token: user.token,
  body: { code, description: '修改后的简介' } })
 const { id, source, updatedAt, ...expected } = replacement
 assert.equal(updated.id, post.id)
 assert.equal(updated.code, code)
 assert.deepEqual(updated.lineup, expected)
 assert.equal(updated.description, '修改后的简介')
 assert.equal(updated.likes, 1)
 assert.equal(updated.liked, true)
 assert.deepEqual(await ok('/api/posts/' + post.id, { token: user.token }), updated)
})

test('065 非作者不能修改阵容', async () => {
 const author = await login(), other = await login(), post = await publish(author)
 const result = await request('/api/posts/' + post.id, { method: 'PATCH', token: other.token,
  body: { code: encodeLineup(lineup('其他阵容'), catalog), description: '' } })
 assert.equal(result.status, 403)
})

test('065 修改成作者另一帖的分享码返回 duplicate', async () => {
 const author = await login(), a = await publish(author, '阵容甲'), b = await publish(author, '阵容乙')
 const result = await request('/api/posts/' + a.id, { method: 'PATCH', token: author.token,
  body: { code: b.code, description: '重复阵容' } })
 assert.equal(result.status, 409)
 assert.deepEqual(result.data, { error: 'duplicate', message: '你已经发过一模一样的阵容。' })
 assert.deepEqual(await ok('/api/posts/' + a.id), a)
})

test('065 修改为旧赛季分享码返回 season_mismatch', async () => {
 const author = await login(), post = await publish(author)
 const bytes = Buffer.from(post.code.slice(4), 'base64url'); bytes[1] ^= 1
 const result = await request('/api/posts/' + post.id, { method: 'PATCH', token: author.token,
  body: { code: post.code.slice(0, 4) + bytes.toString('base64url'), description: '' } })
 assert.equal(result.status, 400)
 assert.deepEqual(result.data, { error: 'season_mismatch', message: '这个分享码来自其他赛季或其他版本的数据，请先把助手更新到最新版。' })
})

test('点赞取消、导入去重、热度/最新/精选、盟约策略筛选和游标翻页', async () => {
 const user = await login(), headers = await access('admin@example.com')
 const a = await publish(user, '阵容甲')
 const b = await publish(user, '阵容乙')
 const c = await publish(user, '阵容丙', [{ id: catalog.covenants[2].id, level: 1 }])
 for (const [i, post] of [a,b,c].entries()) await db.prepare('UPDATE posts SET created_at=? WHERE id=?').bind('2026-01-0' + (i+1) + 'T00:00:00.000Z', post.id).run()
 assert.deepEqual(await ok('/api/posts/' + a.id + '/like', { method: 'POST', token: user.token }), { likes: 1, liked: true })
 assert.deepEqual(await ok('/api/posts/' + a.id + '/like', { method: 'DELETE', token: user.token }), { likes: 0, liked: false })
 await ok('/api/posts/' + a.id + '/like', { method: 'POST', token: user.token })
 for (let i=0; i<2; i++) assert.deepEqual(await ok('/api/posts/' + a.id + '/import', { method: 'POST', body: { device: 'same-device' } }), { imports: 1 })
 await ok('/api/admin/posts/' + b.id, { method: 'PATCH', headers, body: { featured: true } })
 const ids = result => result.posts.map(p => p.id)
 assert.deepEqual(ids(await ok('/api/posts?sort=hot')), [a.id,c.id,b.id])
 assert.deepEqual(ids(await ok('/api/posts?sort=new')), [c.id,b.id,a.id])
 assert.deepEqual(ids(await ok('/api/posts?sort=featured')), [b.id])
 assert.equal((await ok('/api/posts?sort=featured')).cursor, null)
 const filters = new URLSearchParams({ sort: 'new', strategy: catalog.strategies[0].id })
 for (const goal of a.lineup.goals) filters.append('covenant', goal.id)
 assert.deepEqual(ids(await ok('/api/posts?' + filters)), [b.id,a.id])
 for (const sort of ['hot','new']) {
  const page1 = await ok('/api/posts?sort=' + sort + '&limit=2')
  assert.ok(page1.cursor)
  const page2 = await ok('/api/posts?sort=' + sort + '&limit=2&cursor=' + encodeURIComponent(page1.cursor))
  assert.equal(page2.cursor, null)
  assert.deepEqual([...ids(page1), ...ids(page2)], ids(await ok('/api/posts?sort=' + sort)))
 }
})

test('094 搜索阵容、简介、作者、各阶段干员装备和目标盟约，多个词全部命中', async () => {
 const user = await login('SearchAuthor'), other = await login('其他玩家')
 const original = lineup('SearchTitle', [{ id: 'siracusa', level: 1 }])
 original.members = [{ name: catalog.operators[0].name, equipment: [{ name: catalog.equipment[0].name, priority: true }] }]
 original.equipment = [catalog.equipment[1].name]
 original.stages = [{ name: '过渡', fromRound: 1, toRound: 4, goals: [],
  members: [{ name: catalog.operators[1].name, equipment: [{ name: catalog.equipment[2].name, priority: false }] }],
  equipment: [catalog.equipment[3].name] }]
 const post = await ok('/api/posts', { method: 'POST', token: user.token,
  body: { code: encodeLineup(original, catalog), description: 'SearchDescription' } })
 const unrelated = lineup('无关阵容', [])
 unrelated.members = []; unrelated.equipment = []
 await ok('/api/posts', { method: 'POST', token: other.token, body: { code: encodeLineup(unrelated, catalog), description: '' } })
 for (const word of ['searchtitle', 'searchdescription', 'searchauthor', ...catalog.operators.slice(0, 2).map(o => o.name),
  ...catalog.equipment.slice(0, 4).map(e => e.name), '叙拉古', '叙拉', 'searchtitle\tsearchdescription']) {
  assert.deepEqual((await ok('/api/posts?' + new URLSearchParams({ q: word }))).posts.map(p => p.id), [post.id], word)
 }
 assert.deepEqual((await ok('/api/posts?q=' + encodeURIComponent('SearchTitle 不存在'))).posts, [])
})

test('094 LIKE 特殊字符按字面匹配，空搜索等同未传', async () => {
 const user = await login()
 for (const name of ['字面%值', '字面_值', '字面\\值', '字面普通值']) await publish(user, name)
 for (const word of ['%', '_', '\\']) {
  assert.deepEqual((await ok('/api/posts?q=' + encodeURIComponent(word))).posts.map(p => p.lineup.name), ['字面' + word + '值'])
 }
 const all = await ok('/api/posts')
 assert.deepEqual(await ok('/api/posts?q='), all)
 assert.deepEqual(await ok('/api/posts?q=' + encodeURIComponent(' \t\n ')), all)
})

test('094 搜索叠加盟约收藏筛选，分页游标绑定中文关键词', async () => {
 const user = await login(), ids = []
 for (const [name, goals, favorite] of [
  ['搜索甲', [{ id: 'siracusa', level: 1 }], true],
  ['搜索乙', [{ id: 'siracusa', level: 1 }], true],
  ['搜索未收藏', [{ id: 'siracusa', level: 1 }], false],
  ['搜索其他盟约', [], true],
  ['无关名称', [{ id: 'siracusa', level: 1 }], true],
 ]) {
  const post = await publish(user, name, goals)
  if (favorite) await ok('/api/posts/' + post.id + '/favorite', { method: 'POST', token: user.token })
  if (name === '搜索甲' || name === '搜索乙') ids.push(post.id)
 }
 const filters = new URLSearchParams({ q: '搜索', covenant: 'siracusa', favorite: '1', limit: '1' })
 const first = await ok('/api/posts?' + filters, { token: user.token })
 assert.ok(first.cursor)
 assert.match(JSON.parse(Buffer.from(first.cursor, 'base64').toString('utf8')).key, /搜索/)
 filters.set('cursor', first.cursor)
 const second = await ok('/api/posts?' + filters, { token: user.token })
 assert.equal(second.cursor, null)
 assert.deepEqual([...first.posts, ...second.posts].map(p => p.id).sort(), ids.sort())
 filters.set('q', '搜索甲')
 const changed = await request('/api/posts?' + filters, { token: user.token })
 assert.equal(changed.status, 400); assert.equal(changed.data.error, 'bad_cursor')
})

test('后台 Access 签名与邮箱名单、下架、封禁及凭据失效', async () => {
 const user = await login(), post = await publish(user)
 assert.equal((await request('/api/admin/posts')).status, 403)
 assert.equal((await request('/api/admin/posts', { headers: await access('outsider@example.com') })).status, 403)
 const headers = await access('admin@example.com')
 assert.equal((await ok('/api/admin/posts', { headers })).posts.length, 1)
 const hidden = await ok('/api/admin/posts/' + post.id, { method: 'PATCH', headers, body: { status: 'hidden' } })
 assert.equal(hidden.status, 'hidden')
 assert.equal((await request('/api/posts/' + post.id)).status, 404)
 assert.equal((await ok('/api/posts/' + post.id, { token: user.token })).hidden, true)
 await ok('/api/admin/posts/' + post.id, { method: 'PATCH', headers, body: { status: 'visible' } })
 const banned = await ok('/api/admin/users/' + user.me.id, { method: 'PATCH', headers, body: { banned: true } })
 assert.equal(banned.banned, true)
 assert.equal((await request('/api/posts/' + post.id)).status, 404)
 assert.equal((await request('/api/me', { token: user.token })).status, 403)
 assert.equal((await ok('/api/admin/posts?status=hidden', { headers })).posts.length, 1)
})

test('注销删除帖子和点赞、清除邮箱昵称、使凭据失效', async () => {
 const user = await login(), other = await login()
 const own = await publish(user), another = await publish(other)
 await ok('/api/posts/' + another.id + '/like', { method: 'POST', token: user.token })
 assert.equal((await request('/api/me', { method: 'DELETE', token: user.token })).status, 204)
 assert.equal((await request('/api/me', { token: user.token })).status, 401)
 assert.equal((await request('/api/posts/' + own.id)).status, 404)
 assert.equal((await ok('/api/posts/' + another.id)).likes, 0)
 const row = await db.prepare('SELECT email,nickname,deleted FROM users WHERE id=?').bind(user.me.id).first()
 assert.deepEqual(row, { email: null, nickname: null, deleted: 1 })
 assert.equal((await db.prepare('SELECT COUNT(*) AS n FROM posts WHERE user_id=?').bind(user.me.id).first()).n, 0)
 assert.equal((await db.prepare('SELECT COUNT(*) AS n FROM likes WHERE user_id=?').bind(user.me.id).first()).n, 0)
})

const commentPath = post => '/api/posts/' + post.id + '/comments'
const comment = (user, post, body = '测试评论', replyTo) => ok(commentPath(post), {
 method: 'POST', token: user.token, body: { body, ...(replyTo !== undefined && { replyTo }) },
})
const reportTarget = (user, kind, id, reason = 'spam', detail) => request('/api/reports', {
 method: 'POST', token: user.token, body: { target: { kind, id }, reason, ...(detail !== undefined && { detail }) },
})
const moderateComment = (headers, id, status) => request('/api/admin/comments/' + id, { method: 'PATCH', headers, body: { status } })
const resolve = (headers, kind, id, action) => request('/api/admin/reports/' + kind + '/' + id, { method: 'PATCH', headers, body: { action } })

test('评论发布、同时间分页、回复昵称快照、删除权限及可见计数', async () => {
 const owner = await login(), writer = await login(), other = await login(), headers = await access('admin@example.com')
 const post = await publish(owner), another = await publish(owner, '另一帖子')
 assert.equal(post.comments, 0)
 const a = await comment(writer, post, ' 第一条🙂 ')
 assert.equal(a.body, '第一条🙂'); assert.equal(a.replyTo, null); assert.equal(a.canDelete, true)
 const b = await comment(other, post, '回复', a.id)
 const c = await comment(owner, post, '回复的回复', b.id)
 assert.deepEqual(c.replyTo, { id: b.id, nickname: other.me.nickname })
 const badReply = await request(commentPath(another), { method: 'POST', token: writer.token, body: { body: '跨帖回复', replyTo: a.id } })
 assert.equal(badReply.status, 400); assert.equal(badReply.data.error, 'bad_reply')
 await db.prepare('UPDATE comments SET created_at=? WHERE post_id=?').bind('2026-01-01T00:00:00.000Z', post.id).run()
 const first = await ok(commentPath(post) + '?limit=2'), second = await ok(commentPath(post) + '?limit=2&cursor=' + encodeURIComponent(first.cursor))
 assert.equal(second.cursor, null)
 assert.deepEqual([...first.comments, ...second.comments].map(c => c.id), [a.id,b.id,c.id].sort().reverse())
 assert.ok(first.comments.every(c => !c.canDelete))
 assert.ok((await ok(commentPath(post), { token: owner.token })).comments.every(c => c.canDelete))
 assert.equal((await ok('/api/posts/' + post.id)).comments, 3)
 assert.equal((await request('/api/comments/' + a.id, { method: 'DELETE', token: other.token })).status, 403)
 assert.equal((await moderateComment(headers, c.id, 'hidden')).status, 204)
 assert.equal((await ok('/api/posts/' + post.id)).comments, 2)
 assert.equal((await moderateComment(headers, c.id, 'visible')).status, 204)
 assert.equal((await ok('/api/posts/' + post.id)).comments, 3)
 const admin = await ok('/api/admin/comments?post=' + post.id + '&status=visible&limit=2', { headers })
 assert.equal(admin.comments.length, 2); assert.ok(admin.cursor)
 assert.equal(admin.comments[0].postTitle, post.lineup.name); assert.ok(admin.comments[0].authorEmailMasked.includes('***'))
 assert.equal('canDelete' in admin.comments[0], false)
 assert.equal((await ok('/api/admin/comments?post=' + post.id + '&status=visible&limit=2&cursor=' + encodeURIComponent(admin.cursor), { headers })).comments.length, 1)
 assert.equal((await request('/api/comments/' + a.id, { method: 'DELETE', token: writer.token })).status, 204)
 assert.equal((await request('/api/comments/' + b.id, { method: 'DELETE', token: owner.token })).status, 204)
 assert.equal((await ok('/api/posts/' + post.id)).comments, 1)
 assert.deepEqual((await ok(commentPath(post))).comments[0].replyTo, { id: b.id, nickname: other.me.nickname })
 await ok('/api/admin/posts/' + post.id, { method: 'PATCH', headers, body: { status: 'hidden' } })
 assert.equal((await request(commentPath(post), { token: owner.token })).status, 404)
 assert.equal((await request(commentPath(post), { method: 'POST', token: owner.token, body: { body: '不可发' } })).status, 404)
 assert.equal((await request('/api/comments/' + c.id, { method: 'DELETE', token: owner.token })).status, 404)
 assert.equal((await ok('/api/posts/' + post.id, { token: owner.token })).comments, 1)
 await ok('/api/admin/posts/' + post.id, { method: 'PATCH', headers, body: { status: 'visible' } })
 assert.equal((await ok('/api/posts/' + post.id)).comments, 1)
})

test('评论正文、敏感词、十秒间隔与北京时间每日三十条', async t => {
 let now = Date.parse('2026-11-12T15:00:00.000Z')
 t.mock.method(Date, 'now', () => now)
 const user = await login(), post = await publish(user)
 for (const text of ['  ', '🙂'.repeat(301)]) {
  const result = await request(commentPath(post), { method: 'POST', token: user.token, body: { body: text } })
  assert.equal(result.status, 400); assert.equal(result.data.error, 'bad_body')
 }
 const blocked = await request(commentPath(post), { method: 'POST', token: user.token, body: { body: '加 Q Q' } })
 assert.equal(blocked.status, 400); assert.equal(blocked.data.error, 'blocked_words')
 await comment(user, post, '🙂'.repeat(300))
 const send = () => request(commentPath(post), { method: 'POST', token: user.token, body: { body: '继续评论' } })
 const quick = await send()
 assert.equal(quick.status, 429); assert.equal(quick.data.error, 'rate_limited'); assert.match(quick.data.message, /10 秒/)
 for (let i = 1; i < 30; i++) { now += 10000; await comment(user, post) }
 now += 10000
 const daily = await send()
 assert.equal(daily.status, 429); assert.equal(daily.data.message, '今天评论次数已用完，请明天再试。')
 now = Date.parse('2026-11-12T16:00:00.000Z')
 await comment(user, post)
 assert.equal((await ok('/api/posts/' + post.id)).comments, 31)
})

test('举报去重、理由限制、后台归并分页和处理后重新待处理', async t => {
 let now = Date.parse('2026-11-12T10:00:00.000Z')
 t.mock.method(Date, 'now', () => now)
 const owner = await login(), writer = await login(), headers = await access('admin@example.com')
 const post = await publish(owner), c = await comment(writer, post)
 const invalid = await reportTarget(owner, 'comment', c.id, 'broken')
 assert.equal(invalid.status, 400); assert.equal(invalid.data.error, 'bad_reason')
 assert.equal((await reportTarget(owner, 'post', post.id, 'spam', '🙂'.repeat(201))).data.error, 'bad_detail')
 for (let i = 0; i < 6; i++) {
  now += 1000
  const reporter = await login(null)
  assert.equal((await reportTarget(reporter, 'comment', c.id, i % 2 ? 'other' : 'spam', '说明' + i)).status, 204)
 }
 now += 1000
 assert.equal((await reportTarget(owner, 'post', post.id, 'broken', '阵容错误')).status, 204)
 assert.equal((await request('/api/admin/comments')).status, 403)
 assert.equal((await request('/api/admin/reports', { headers: await access('outsider@example.com') })).status, 403)
 const page = await ok('/api/admin/reports?limit=1', { headers })
 assert.equal(page.openCount, 2); assert.equal(page.reports[0].target.id, post.id)
 const next = await ok('/api/admin/reports?limit=1&cursor=' + encodeURIComponent(page.cursor), { headers })
 assert.equal(next.cursor, null); assert.equal(next.openCount, 2)
 const grouped = next.reports[0]
 assert.equal(grouped.post.id, post.id); assert.equal(grouped.comment.id, c.id); assert.equal(grouped.count, 6)
 assert.deepEqual(grouped.reasons, { spam: 3, other: 3 })
 assert.deepEqual(grouped.details, ['说明5', '说明4', '说明3', '说明2', '说明1'])
 assert.equal((await resolve(headers, 'post', post.id, 'dismiss')).status, 204)
 assert.equal((await ok('/api/posts/' + post.id)).id, post.id)
 assert.equal((await ok('/api/admin/reports?status=resolved', { headers })).openCount, 1)
 now += 1000
 assert.equal((await reportTarget(owner, 'post', post.id, 'other', '修改说明')).status, 204)
 const reopened = (await ok('/api/admin/reports', { headers })).reports.find(r => r.target.kind === 'post')
 assert.equal(reopened.count, 1); assert.equal(reopened.status, 'open'); assert.deepEqual(reopened.reasons, { other: 1 })
 assert.deepEqual(reopened.details, ['修改说明'])
 assert.equal((await resolve(headers, 'comment', c.id, 'hide')).status, 204)
 assert.equal((await ok('/api/posts/' + post.id)).comments, 0)
 assert.equal((await reportTarget(owner, 'comment', c.id)).status, 404)
 assert.equal((await moderateComment(headers, c.id, 'visible')).status, 204)
 assert.equal((await ok('/api/posts/' + post.id)).comments, 1)
 assert.equal((await resolve(headers, 'post', post.id, 'delete')).status, 204)
 assert.equal((await request('/api/posts/' + post.id)).status, 404)
 assert.equal((await ok('/api/admin/reports', { headers })).openCount, 0)
})

test('重复举报计入每日二十次且北京时间零点重置', async t => {
 let now = Date.parse('2026-11-12T15:59:00.000Z')
 t.mock.method(Date, 'now', () => now)
 const user = await login(null), author = await login(), post = await publish(author)
 for (let i = 0; i < 20; i++) assert.equal((await reportTarget(user, 'post', post.id)).status, 204)
 const limited = await reportTarget(user, 'post', post.id)
 assert.equal(limited.status, 429)
 assert.deepEqual(limited.data, { error: 'rate_limited', message: '今天举报次数已用完，请明天再试。' })
 now = Date.parse('2026-11-12T16:00:00.000Z')
 assert.equal((await reportTarget(user, 'post', post.id)).status, 204)
 assert.equal((await db.prepare('SELECT COUNT(*) AS n FROM reports').first()).n, 1)
})

test('封禁隐藏评论并返回403、解封不恢复，注销删除评论举报并维护计数', async () => {
 const owner = await login(), writer = await login(), headers = await access('admin@example.com')
 const post = await publish(owner), own = await publish(writer), c = await comment(writer, post)
 await reportTarget(writer, 'post', post.id)
 await reportTarget(owner, 'post', own.id)
 await reportTarget(owner, 'comment', c.id)
 await ok('/api/admin/users/' + writer.me.id, { method: 'PATCH', headers, body: { banned: true } })
 assert.equal((await ok('/api/posts/' + post.id)).comments, 0)
 assert.equal((await ok('/api/admin/comments?status=hidden', { headers })).comments[0].id, c.id)
 assert.equal((await request(commentPath(post), { method: 'POST', token: writer.token, body: { body: '禁止评论' } })).status, 403)
 assert.equal((await reportTarget(writer, 'post', post.id)).status, 403)
 await ok('/api/admin/users/' + writer.me.id, { method: 'PATCH', headers, body: { banned: false } })
 assert.equal((await request('/api/me', { token: writer.token })).status, 401)
 assert.equal((await ok('/api/posts/' + post.id)).comments, 0)
 // 新验证码登录后恢复一条评论，再注销核对物理删除与计数。
 await db.prepare('UPDATE login_codes SET created_at=? WHERE email=?').bind('2020-01-01T00:00:00.000Z', writer.email).run()
 const code = await codeFor(writer.email)
 const fresh = await ok('/api/auth/verify', { method: 'POST', body: { email: writer.email, code } })
 await moderateComment(headers, c.id, 'visible')
 assert.equal((await ok('/api/posts/' + post.id)).comments, 1)
 assert.equal((await request('/api/me', { method: 'DELETE', token: fresh.token })).status, 204)
 assert.equal((await ok('/api/posts/' + post.id)).comments, 0)
 assert.equal((await db.prepare('SELECT COUNT(*) AS n FROM comments WHERE user_id=?').bind(writer.me.id).first()).n, 0)
 assert.equal((await db.prepare('SELECT COUNT(*) AS n FROM reports').first()).n, 0)
 assert.equal((await ok('/api/admin/reports', { headers })).openCount, 0)
})

const password = ' 中文密码🙂 test '
test('046 SA3 阶段、精锐技能模组原样返回，目录指纹不变', async () => {
 assert.equal(fingerprint.toString(16), '981d')
 const user = await login(), original = lineup()
 const op = catalog.operators.find(o => o.skills?.length && o.modules?.length)
 const member = { name: op.name, elite: true, skill: op.skills[0].id, module: op.modules[0].id }
 original.members = [member]
 original.stages = [{ name: '过渡', fromRound: 1, toRound: 4, goals: original.goals, members: [member], equipment: [] }]
 const code = encodeLineup(original, catalog)
 assert.match(code, /^SA3-/)
 const post = await ok('/api/posts', { method: 'POST', token: user.token, body: { code, description: '' } })
 const detail = await ok('/api/posts/' + post.id)
 assert.deepEqual(detail.lineup.stages, original.stages)
 assert.deepEqual(detail.lineup.members, original.members)
})

test('046 收藏增删、跨赛季分页、可见性、500 上限和注销清理', async () => {
 const author = await login(), user = await login(null), a = await publish(author), b = await publish(author, '旧阵容')
 const favorite = (id, method = 'POST') => ok('/api/posts/' + id + '/favorite', { method, token: user.token })
 assert.equal((await request('/api/posts?favorite=1')).status, 401)
 assert.equal((await favorite(a.id)).favorited, true)
 await favorite(b.id)
 await db.prepare('UPDATE posts SET fingerprint=0 WHERE id=?').bind(b.id).run()
 await db.prepare('UPDATE favorites SET created_at=? WHERE post_id=?').bind('2020-01-01T00:00:00.000Z', a.id).run()
 assert.equal((await ok('/api/posts/' + a.id, { token: user.token })).favorited, true)
 assert.equal((await ok('/api/posts')).posts[0].favorited, false)
 const page = await ok('/api/posts?favorite=1&sort=ignored&season=ignored&limit=1', { token: user.token })
 assert.equal(page.posts[0].id, b.id)
 assert.equal((await ok('/api/posts?favorite=1&limit=1&cursor=' + encodeURIComponent(page.cursor), { token: user.token })).posts[0].id, a.id)
 await favorite(a.id, 'DELETE')
 assert.equal((await ok('/api/posts/' + a.id, { token: user.token })).favorited, false)
 await db.prepare("UPDATE posts SET status='hidden' WHERE id=?").bind(b.id).run()
 assert.deepEqual((await ok('/api/posts?favorite=1', { token: user.token })).posts, [])
 for (const method of ['POST', 'DELETE']) assert.equal((await request('/api/posts/' + b.id + '/favorite', { method, token: user.token })).status, 404)
 await db.prepare(`WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x<499)
  INSERT INTO posts(id,user_id,code,fingerprint,lineup,description,created_at,updated_at)
  SELECT 'bulk-'||x,?,'bulk-'||x,0,'{}','','2020','2020' FROM n`).bind(author.me.id).run()
 await db.prepare("INSERT INTO favorites SELECT ?,id,'2020' FROM posts WHERE id LIKE 'bulk-%'").bind(user.me.id).run()
 assert.equal((await request('/api/posts/' + a.id + '/favorite', { method: 'POST', token: user.token })).status, 429)
 assert.equal((await request('/api/me', { method: 'DELETE', token: user.token })).status, 204)
 assert.equal((await db.prepare('SELECT COUNT(*) AS n FROM favorites').first()).n, 0)
})

const cloud = (id, updatedAt = '2026-01-01T00:00:00.000Z') => ({ id, data: { ...lineup(), id, updatedAt }, updatedAt, deleted: false })
const syncWith = (user, lineups = [], since = null, prefs) => ok('/api/sync', { method: 'POST', token: user.token, body: { lineups, since, ...(prefs && { prefs }) } })
test('046 同步上传下载、服务端增量、冲突、墓碑、偏好及注销', async t => {
 let now = Date.parse('2026-11-12T10:00:00.000Z')
 t.mock.method(Date, 'now', () => now)
 const user = await login(null), other = await login(null), a = cloud('a')
 const prefs = { data: { focusOperators: [], focusCovenants: [], gameTheme: 'terminal', shopMarkers: true, operatorDefaults: {} }, updatedAt: a.updatedAt }
 const uploaded = await syncWith(user, [a], null, prefs)
 assert.deepEqual(uploaded.lineups, []); assert.deepEqual(uploaded.conflicts, [])
 const downloaded = await syncWith(user)
 assert.deepEqual(downloaded.lineups, [a]); assert.deepEqual(downloaded.prefs, prefs)
 assert.deepEqual((await syncWith(other)).lineups, [])
 assert.deepEqual((await syncWith(user, [], downloaded.now)).lineups, [])
 now += 1000
 const b = cloud('b'), newer = cloud('a', '2026-02-01T00:00:00.000Z')
 await syncWith(user, [newer, b], downloaded.now, { ...prefs, updatedAt: newer.updatedAt })
 const delta = await syncWith(user, [], downloaded.now)
 assert.deepEqual(delta.lineups.map(x => x.id).sort(), ['a', 'b'])
 const conflict = await syncWith(user, [a], delta.now, prefs)
 assert.deepEqual(conflict.conflicts, [newer]); assert.equal(conflict.prefs.updatedAt, newer.updatedAt)
 now += 1000
 const tombstone = { id: 'a', data: null, deleted: true, updatedAt: '2026-03-01T00:00:00.000Z' }
 await syncWith(user, [tombstone])
 assert.deepEqual((await syncWith(user, [], conflict.now)).lineups, [tombstone])
 assert.deepEqual((await syncWith(user)).lineups, [b])
 await db.prepare('UPDATE cloud_lineups SET changed_at=? WHERE id=?').bind(new Date(now - 91 * 86400000).toISOString(), 'a').run()
 await syncWith(user)
 assert.equal((await db.prepare("SELECT COUNT(*) AS n FROM cloud_lineups WHERE id='a'").first()).n, 0)
 assert.equal((await request('/api/me', { method: 'DELETE', token: user.token })).status, 204)
 for (const table of ['cloud_lineups', 'cloud_prefs', 'sync_usage'])
  assert.equal((await db.prepare('SELECT COUNT(*) AS n FROM ' + table + ' WHERE user_id=?').bind(user.me.id).first()).n, 0)
})

test('046 同步容量、64 KB、id 校验和每分钟限频', async () => {
 const user = await login(null)
 const send = lineups => request('/api/sync', { method: 'POST', token: user.token, body: { since: null, lineups } })
 const large = cloud('large'); large.data.description = '中'.repeat(22000)
 assert.equal((await send([large])).data.error, 'too_large')
 const invalid = cloud('a'); invalid.data.id = 'b'
 assert.equal((await send([invalid])).status, 400)
 await syncWith(user, Array.from({ length: 300 }, (_, i) => cloud('x' + i)))
 assert.equal((await send([cloud('overflow')])).data.error, 'too_many')
 await syncWith(user, [cloud('x0')])
 await syncWith(user, [{ id: 'x0', data: null, deleted: true, updatedAt: '2026-02-01T00:00:00.000Z' }, cloud('replacement')])
 assert.equal((await syncWith(user)).lineups.length, 300)
 await db.prepare('DELETE FROM sync_usage WHERE user_id=?').bind(user.me.id).run()
 for (let i = 0; i < 30; i++) await syncWith(user, [], '2099-01-01T00:00:00.000Z')
 assert.equal((await send([])).status, 429)
})

const income = (device, samples = [{ round: 1, startMoney: 10 }], extra = {}, ip = 'local') => request('/api/income', {
 method: 'POST', headers: { 'CF-Connecting-IP': ip }, body: { device, mode: 'mode_single_16', solo: true, strategyId: null, fingerprint, samples, ...extra },
})
test('046 资金参数校验、经济策略拒收、三设备最小值及每日缓存', async t => {
 let now = Date.parse('2026-11-12T10:00:00.000Z')
 t.mock.method(Date, 'now', () => now)
 for (const extra of [{ mode: 'mode_single_unknown' }, { samples: [{ round: 31, startMoney: 1 }] }, { samples: [{ round: 1, startMoney: 201 }] }, { samples: Array(31).fill({ round: 1, startMoney: 1 }) }])
  assert.equal((await income('bad', undefined, extra)).data.error, 'bad_income')
 for (const strategyId of ['band_cannot', 'band_lmlee', 'band_mlynar', 'band_fund', 'band_protect', 'band_treasure', 'band_ducklord', 'band_siracusa'])
  assert.equal((await income('excluded', undefined, { strategyId })).status, 204)
 assert.equal((await db.prepare('SELECT COUNT(*) AS n FROM income_samples').first()).n, 0)
 for (const device of ['a', 'a', 'b']) assert.equal((await income(device)).status, 204)
 assert.deepEqual((await ok('/api/income-table')).rows, [])
 await income('c')
 assert.deepEqual((await ok('/api/income-table')).rows, [])
 for (const device of ['a', 'b', 'c']) await income(device, [{ round: 1, startMoney: 8 }, { round: 2, startMoney: 12 }], {}, '192.0.2.' + (['a', 'b', 'c'].indexOf(device) + 1))
 await income('a', [{ round: 1, startMoney: 1 }])
 for (const device of ['a', 'b', 'c']) await income(device, [{ round: 1, startMoney: 0 }], { fingerprint: 0 })
 now += 86400000
 const result = await request('/api/income-table')
 assert.equal(result.headers.get('Cache-Control'), 'public, max-age=3600')
 assert.deepEqual(result.data.rows, [{ mode: 'mode_single_16', solo: true, round: 1, base: 8 }, { mode: 'mode_single_16', solo: true, round: 2, base: 12 }])
 assert.equal(result.data.fingerprint, fingerprint)
})

test('046 资金上报按设备二十次、IP 六十次限频', async () => {
 for (let i = 0; i < 20; i++) assert.equal((await income('same')).status, 204)
 assert.equal((await income('same')).status, 429)
 for (let i = 0; i < 40; i++) assert.equal((await income('other' + i)).status, 204)
 assert.equal((await income('last')).status, 429)
})

const setPassword = (user, value = password, currentPassword) => ok('/api/me/password', {
 method: 'PUT', token: user.token, body: { password: value, ...(currentPassword !== undefined && { currentPassword }) },
})
const passwordRequest = (email, value = password, headers = {}) => request('/api/auth/password', {
 method: 'POST', body: { email, password: value, turnstileToken: 'test-token' }, headers,
})

test('验证码登录设密码、退出后密码登录，返回 hasPassword 和 Cookie', async () => {
 const user = await login()
 assert.equal(user.me.hasPassword, false)
 assert.equal((await setPassword(user)).hasPassword, true)
 assert.equal((await ok('/api/me', { token: user.token })).hasPassword, true)
 assert.equal((await request('/api/auth/logout', { method: 'POST', token: user.token })).status, 204)
 const result = await passwordRequest(user.email)
 assert.equal(result.status, 200)
 assert.equal(result.data.me.hasPassword, true)
 assert.match(result.headers.get('Set-Cookie'), /HttpOnly; Secure/)
 assert.equal((await ok('/api/me', { token: result.data.token })).id, user.me.id)
})

test('密码错误五次后正确密码也限频，未知邮箱和错误密码提示一致', async () => {
 const user = await login()
 await setPassword(user)
 const missing = await passwordRequest('missing@example.com')
 assert.equal(missing.status, 401)
 assert.deepEqual(missing.data, { error: 'bad_credentials', message: '邮箱或密码不对。' })
 for (let i = 0; i < 5; i++) {
  const wrong = await passwordRequest(user.email, 'wrong-password')
  assert.equal(wrong.status, 401)
  assert.deepEqual(wrong.data, missing.data)
 }
 const limited = await passwordRequest(user.email)
 assert.equal(limited.status, 429)
 assert.deepEqual(limited.data, { error: 'rate_limited', message: '输错太多次，请 15 分钟后再试，或者用验证码登录。' })
})

test('验证码会话可直接重设，密码会话必须提供当前密码，修改撤销其他会话', async () => {
 const user = await login()
 await setPassword(user)
 const other = (await passwordRequest(user.email)).data
 const denied = await request('/api/me/password', { method: 'PUT', token: other.token, body: { password: 'next-password' } })
 assert.equal(denied.status, 403)
 assert.deepEqual(denied.data, { error: 'bad_credentials', message: '当前密码不对。' })
 assert.equal((await setPassword(user, 'next-password')).hasPassword, true)
 assert.equal((await request('/api/me', { token: other.token })).status, 401)
 assert.equal((await ok('/api/me', { token: user.token })).id, user.me.id)
 const current = (await passwordRequest(user.email, 'next-password')).data
 await setPassword(current, 'third-password', 'next-password')
 assert.equal((await request('/api/me', { token: user.token })).status, 401)
 assert.equal((await ok('/api/me', { token: current.token })).hasPassword, true)
})

test('七位密码和与邮箱相同的密码拒绝', async () => {
 const user = await login()
 for (const value of ['1234567', user.email.toUpperCase()]) {
  const result = await request('/api/me/password', { method: 'PUT', token: user.token, body: { password: value } })
  assert.equal(result.status, 400)
  assert.deepEqual(result.data, { error: 'weak_password', message: '密码要 8～64 位，而且不能和邮箱一样。' })
 }
})

test('北京时间零点重置验证码发送、发布与密码失败 IP 配额', async t => {
 let now = Date.parse('2026-11-12T15:30:00.000Z')
 t.mock.method(Date, 'now', () => now)
 const user = await login()
 await setPassword(user)
 for (let i = 0; i < 10; i++) await publish(user, '昨日阵容' + i)
 assert.equal((await request('/api/posts', { method: 'POST', token: user.token,
  body: { code: encodeLineup(lineup('超额阵容'), catalog), description: '' } })).status, 429)
 // 填满同一邮箱和 IP 的昨日发送额度。
 await db.batch(Array.from({ length: 29 }, () => db.prepare(
  'INSERT INTO login_codes(email,ip,code_hash,created_at,expires_at) VALUES(?,?,?,?,?)'
 ).bind(user.email, 'local', 'unused', new Date(now).toISOString(), new Date(now).toISOString())))
 now += 61000
 assert.equal((await request('/api/auth/code', { method: 'POST', body: { email: user.email, turnstileToken: 'test-token' } })).status, 429)
 await db.batch(Array.from({ length: 30 }, (_, i) => db.prepare(
  'INSERT INTO password_attempts(email,ip,created_at,success) VALUES(?,?,?,0)'
 ).bind('failed' + i + '@example.com', 'local', new Date(now).toISOString())))
 assert.equal((await passwordRequest(user.email)).status, 429)
 now = Date.parse('2026-11-12T16:30:00.000Z')
 await codeFor(user.email)
 await publish(user, '今日阵容')
 assert.equal((await passwordRequest(user.email)).status, 200)
})


test('075 同步请求大小、条数、id 限制及未知删除不留墓碑', async () => {
 const user = await login(null)
 const send = body => request('/api/sync', { method: 'POST', token: user.token, body: { since: null, lineups: [], ...body } })
 for (const body of [
  { lineups: Array.from({ length: 401 }, (_, i) => cloud('x' + i)) },
  { lineups: [cloud('x'.repeat(65))] },
  { padding: '中'.repeat(700000) },
 ]) {
  const result = await send(body)
  assert.equal(result.status, 400); assert.equal(result.data.error, 'too_large')
 }
 assert.equal((await send({ lineups: [cloud('bad/id')] })).status, 400)
 const tombstone = { id: 'missing', data: null, deleted: true, updatedAt: '2026-01-01T00:00:00.000Z' }
 assert.deepEqual((await syncWith(user, [tombstone])).conflicts, [])
 assert.deepEqual((await syncWith(user)).lineups, [])
 assert.equal((await db.prepare('SELECT COUNT(*) AS n FROM cloud_lineups WHERE user_id=?').bind(user.me.id).first()).n, 0)
 await syncWith(user, Array.from({ length: 300 }, (_, i) => cloud('x' + i)))
 assert.equal((await syncWith(user)).lineups.length, 300)
})

test('075 旧游标上传较新时间也冲突，首次合并同样保留云端', async t => {
 let now = Date.parse('2026-11-13T10:00:00.000Z')
 t.mock.method(Date, 'now', () => now)
 const user = await login(null), initial = await syncWith(user, [cloud('a')])
 now += 1000
 const remote = cloud('a', '2026-02-01T00:00:00.000Z')
 remote.data.name = '设备二修改'
 await syncWith(user, [remote], initial.now)
 now += 1000
 const local = cloud('a', '2026-03-01T00:00:00.000Z')
 for (const since of [initial.now, null]) {
  const result = await syncWith(user, [local], since)
  assert.deepEqual(result.conflicts, [remote])
  assert.deepEqual(result.lineups, [remote])
 }
 assert.deepEqual((await syncWith(user)).lineups, [remote])
})

test('075 偏好只存白名单，主题及字段类型校验', async () => {
 const user = await login(null), updatedAt = '2026-01-01T00:00:00.000Z'
 const data = { focusOperators: [], focusCovenants: [], gameTheme: 'terminal', shopMarkers: true, operatorDefaults: {} }
 await syncWith(user, [], null, { data: { ...data, shareIncome: true }, updatedAt })
 const stored = await db.prepare('SELECT data FROM cloud_prefs WHERE user_id=?').bind(user.me.id).first()
 assert.deepEqual(JSON.parse(stored.data), data)
 for (const invalid of [{ gameTheme: 'dark' }, { focusOperators: [1] }, { shopMarkers: 'true' }, { operatorDefaults: [] }]) {
  const result = await request('/api/sync', { method: 'POST', token: user.token,
   body: { lineups: [], since: null, prefs: { data: { ...data, ...invalid }, updatedAt } } })
  assert.equal(result.status, 400)
 }
})

test('075 三个编号且来源为空的旧资金样本仍参与汇总', async () => {
 await db.batch(['a', 'b', 'c'].map(device => db.prepare(
  'INSERT INTO income_samples (device,mode,solo,round,start_money,fingerprint,strategy_id,created_at,source) VALUES(?,?,?,?,?,?,?,?,NULL)'
 ).bind(device, 'mode_single_16', 1, 1, 10, fingerprint, null, '2026-11-12T00:00:00.000Z')))
 assert.deepEqual((await ok('/api/income-table')).rows, [{ mode: 'mode_single_16', solo: true, round: 1, base: 10 }])
})

test('075 资金汇总要求三个来源，来源按 UTC 日期和用途隔离', async t => {
 let now = Date.parse('2026-11-13T23:59:00.000Z')
 t.mock.method(Date, 'now', () => now)
 const ip = '192.0.2.1', sample = [{ round: 1, startMoney: 10 }]
 for (const device of ['a', 'b', 'c']) assert.equal((await income(device, sample, {}, ip)).status, 204)
 assert.deepEqual((await ok('/api/income-table')).rows, [])
 await income('b', sample, {}, '192.0.2.2')
 await income('c', sample, {}, '192.0.2.3')
 await db.prepare('DELETE FROM income_summary').run()
 assert.deepEqual((await ok('/api/income-table')).rows, [{ mode: 'mode_single_16', solo: true, round: 1, base: 10 }])
 const source = (await db.prepare('SELECT source FROM income_samples ORDER BY rowid LIMIT 1').first()).source
 const digest = Buffer.from(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(ip))).toString('hex')
 assert.match(source, /^[a-f0-9]{64}$/); assert.notEqual(source, digest)
 assert.equal((await db.prepare('SELECT ip FROM income_reports ORDER BY rowid LIMIT 1').first()).ip, source)
 const user = await login(), post = await publish(user)
 await ok('/api/posts/' + post.id + '/import', { method: 'POST', body: { device: 'a' }, headers: { 'CF-Connecting-IP': ip } })
 assert.notEqual((await db.prepare('SELECT source FROM imports').first()).source, source)
 now += 60000
 await income('a', sample, {}, ip)
 assert.notEqual((await db.prepare('SELECT source FROM income_samples ORDER BY rowid DESC LIMIT 1').first()).source, source)
})

test('075 同一来源更换三个编号导入只计一次', async () => {
 const user = await login(), post = await publish(user)
 for (const device of ['a', 'b', 'c']) {
  const result = await ok('/api/posts/' + post.id + '/import', {
   method: 'POST', body: { device }, headers: { 'CF-Connecting-IP': '192.0.2.1' },
  })
  assert.equal(result.imports, 1)
 }
 assert.equal((await db.prepare('SELECT COUNT(*) AS n FROM imports').first()).n, 1)
})

test('075 注销清除别人回复中的昵称', async () => {
 const a = await login(), b = await login(), post = await publish(b)
 const original = await comment(a, post), reply = await comment(b, post, '回复', original.id)
 assert.equal((await request('/api/me', { method: 'DELETE', token: a.token })).status, 204)
 assert.equal((await db.prepare('SELECT reply_nickname FROM comments WHERE id=?').bind(reply.id).first()).reply_nickname, '已注销用户')
})
