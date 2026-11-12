import { after, before, beforeEach, test } from 'node:test'
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { build } from 'esbuild'
import { Miniflare } from 'miniflare'
import { exportJWK, generateKeyPair, SignJWT } from 'jose'

const built = await build({
 stdin: { contents: "export * from './server/api'; export * from './server/catalog'", resolveDir: process.cwd(), loader: 'ts' },
 bundle: true, platform: 'neutral', format: 'esm', write: false, loader: { '.txt': 'text' },
})
const { handleRequest, catalog, encodeLineup } = await import('data:text/javascript;base64,' + Buffer.from(built.outputFiles[0].text).toString('base64'))
const originalFetch = globalThis.fetch
let mf, db, env, privateKey, jwk
const mails = new Map()
let serial = 0

before(async () => {
 mf = new Miniflare({ modules: true, script: 'export default { fetch() { return new Response() } }', d1Databases: ['DB'], compatibilityDate: '2025-10-11' })
 db = await mf.getD1Database('DB')
 for (const file of ['0001_init.sql', '0002_password.sql']) {
  const sql = await readFile('migrations/' + file, 'utf8')
  // D1 exec 按行分语句；保留触发器整体，迁移一次执行。
  await db.exec(sql.replace(/\r?\n/g, ' '))
 }
 const pair = await generateKeyPair('RS256')
 privateKey = pair.privateKey
 jwk = { ...await exportJWK(pair.publicKey), kid: 'test-key', alg: 'RS256', use: 'sig' }
 env = {
  DB: db, CODE_PEPPER: 'test-only-pepper', RESEND_API_KEY: 'test-only-key', MAIL_FROM: 'test@example.com',
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
 await db.batch(['likes', 'imports', 'posts', 'sessions', 'login_codes', 'password_attempts', 'users'].map(t => db.prepare('DELETE FROM ' + t)))
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
 assert.equal((await request('/api/me', { token: user.token })).status, 401)
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

const password = ' 中文密码🙂 test '
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
