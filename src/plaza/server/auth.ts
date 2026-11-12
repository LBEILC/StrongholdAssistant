import { body, DAY, dayStart, empty, fail, hash, hasBlocked, iso, json, me, stmt, string, type Env, type UserRow } from './common'
import { checkPassword, hashPassword } from './password'

export interface Context { request: Request; env: Env; cookie?: string }
export const cookie = (token: string, seconds = 30 * DAY / 1000) =>
 `sa_session=${token}; HttpOnly; Secure; SameSite=Lax; Path=/; Max-Age=${seconds}`
export const tokenFrom = (request: Request) => {
 const bearer = request.headers.get('Authorization')
 return bearer?.startsWith('Bearer ') ? bearer.slice(7) : request.headers.get('Cookie')?.match(/(?:^|;\s*)sa_session=([^;]+)/)?.[1]
}
export async function authenticate(ctx: Context, required = true) {
 const token = tokenFrom(ctx.request)
 if (!token) {
  if (required) fail(401, 'unauthorized', '请先登录。')
  return null
 }
 const tokenHash = await hash(ctx.env.CODE_PEPPER + token)
 const u = await stmt(ctx.env, `SELECT u.*, s.renewed_at, s.expires_at, s.created_at AS session_created_at, s.login_method FROM sessions s JOIN users u ON u.id=s.user_id
  WHERE s.token_hash=?`, tokenHash).first<UserRow & { renewed_at: string; expires_at: string; session_created_at: string; login_method: string }>()
 if (!u || u.deleted) fail(401, 'unauthorized', '登录已失效，请重新登录。')
 if (u.banned) fail(403, 'banned', '这个账号已被封禁。')
 if (u.expires_at <= iso()) fail(401, 'unauthorized', '登录已失效，请重新登录。')
 if (Date.now() - Date.parse(u.renewed_at) > DAY) {
  await stmt(ctx.env, 'UPDATE sessions SET renewed_at=?, expires_at=? WHERE token_hash=?', iso(), iso(Date.now() + 30 * DAY), tokenHash).run()
  ctx.cookie = cookie(token)
 }
 return u
}
function emailValue(value: unknown) {
 const email = string(value).trim().toLowerCase()
 if (email.length > 254 || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) fail(400, 'bad_email', '请填写有效的邮箱。')
 return email
}
async function turnstile(ctx: Context, token: string) {
 const { request, env } = ctx
 const ip = request.headers.get('CF-Connecting-IP') ?? 'local'
 const challenge = await fetch('https://challenges.cloudflare.com/turnstile/v0/siteverify', {
  method: 'POST', body: new URLSearchParams({ secret: env.TURNSTILE_SECRET, response: token, remoteip: ip }),
 })
 if (!challenge.ok || !(await challenge.json() as { success: boolean }).success)
  fail(400, 'bad_turnstile', '人机验证未通过，请重试。')
}
async function sendCode(ctx: Context) {
 const { request, env } = ctx, input = await body(request)
 const email = emailValue(input.email), ip = request.headers.get('CF-Connecting-IP') ?? 'local'
 await turnstile(ctx, string(input.turnstileToken))
 const now = iso(), start = dayStart()
 const counts = await stmt(env, `SELECT
  (SELECT COUNT(*) FROM login_codes WHERE email=? AND created_at>=?) AS emails,
  (SELECT COUNT(*) FROM login_codes WHERE ip=? AND created_at>=?) AS ips,
  (SELECT MAX(created_at) FROM login_codes WHERE email=?) AS latest`, email, start, ip, start, email)
  .first<{ emails: number; ips: number; latest: string | null }>()
 const limited = () => {
  if (counts!.emails >= 10 || counts!.ips >= 30) fail(429, 'rate_limited', '今天发送次数已用完，请明天再试。')
  const wait = counts!.latest ? Math.max(1, Math.ceil((Date.parse(counts!.latest) + 60000 - Date.now()) / 1000)) : 60
  fail(429, 'rate_limited', `发送太频繁，请 ${wait} 秒后再试。`)
 }
 if (counts!.emails >= 10 || counts!.ips >= 30 || (counts!.latest && Date.now() - Date.parse(counts!.latest) < 60000)) limited()
 if (await stmt(env, 'SELECT id FROM users WHERE email=? AND banned=1', email).first())
  fail(403, 'banned', '这个账号已被封禁。')
 // 拒绝取模偏差，均匀生成六位码。
 let random: number
 do { random = crypto.getRandomValues(new Uint32Array(1))[0] } while (random >= 4294000000)
 const code = String(random % 1000000).padStart(6, '0')
 const codeHash = await hash(env.CODE_PEPPER + email + code)
 const inserted = await stmt(env, `INSERT INTO login_codes(email,ip,code_hash,created_at,expires_at)
  SELECT ?,?,?,?,? WHERE
  NOT EXISTS(SELECT 1 FROM login_codes WHERE email=? AND created_at>?)
  AND (SELECT COUNT(*) FROM login_codes WHERE email=? AND created_at>=?)<10
  AND (SELECT COUNT(*) FROM login_codes WHERE ip=? AND created_at>=?)<30`,
  email, ip, codeHash, now, iso(Date.now() + 600000), email, iso(Date.now() - 60000), email, start, ip, start).run()
 if (!inserted.meta.changes) limited()
 const sent = await fetch('https://api.resend.com/emails', {
  method: 'POST', headers: { Authorization: `Bearer ${env.RESEND_API_KEY}`, 'Content-Type': 'application/json' },
  body: JSON.stringify({ from: env.MAIL_FROM, to: [email], subject: `卫戍协议助手登录验证码：${code}`,
   text: `你的验证码是 ${code}，10 分钟内有效。\n如果不是你本人在登录卫戍协议助手的阵容广场，请忽略这封邮件。` }),
 })
 if (!sent.ok) fail(502, 'mail_failed', '验证码发送失败，请稍后再试。')
 return json({ retryAfter: 60 })
}
async function verify(ctx: Context) {
 const { request, env } = ctx
 const input = await body(request), email = emailValue(input.email), code = string(input.code)
 const row = await stmt(env, 'SELECT * FROM login_codes WHERE email=? ORDER BY created_at DESC,id DESC LIMIT 1', email)
  .first<{ id: number; code_hash: string; expires_at: string; attempts: number }>()
 const expired = () => fail(400, 'bad_code', '验证码已失效，请重新获取。')
 if (!row || row.attempts >= 5 || row.expires_at <= iso()) return expired()
 const correct = row.code_hash === await hash(env.CODE_PEPPER + email + code)
 const changed = await stmt(env, `UPDATE login_codes SET attempts=attempts+1, expires_at=CASE WHEN ?=1 THEN ? ELSE expires_at END
  WHERE id=? AND attempts<5 AND expires_at>?`, Number(correct), iso(), row.id, iso()).run()
 if (!changed.meta.changes) return expired()
 if (!correct) fail(400, 'bad_code', '验证码不对，请再检查一下。')
 const now = iso()
 const u = await stmt(env, `INSERT INTO users(id,email,created_at) VALUES(?,?,?)
  ON CONFLICT(email) DO UPDATE SET email=excluded.email RETURNING *`, crypto.randomUUID(), email, now).first<UserRow>()
 return createSession(ctx, u!, 'code')
}
async function createSession(ctx: Context, u: UserRow, method: 'code' | 'password') {
 const { env } = ctx, now = iso()
 if (u.banned) fail(403, 'banned', '这个账号已被封禁。')
 const token = Array.from(crypto.getRandomValues(new Uint8Array(32)), b => b.toString(16).padStart(2, '0')).join('')
 await stmt(env, 'INSERT INTO sessions(token_hash,user_id,created_at,renewed_at,expires_at,login_method) VALUES(?,?,?,?,?,?)', await hash(env.CODE_PEPPER + token), u.id, now, now, iso(Date.now() + 30 * DAY), method).run()
 ctx.cookie = cookie(token)
 return json({ token, me: me(u) })
}
async function passwordLogin(ctx: Context) {
 const { env, request } = ctx, input = await body(request)
 const email = emailValue(input.email), password = string(input.password)
 await turnstile(ctx, string(input.turnstileToken))
 const ip = request.headers.get('CF-Connecting-IP') ?? 'local'
 const counts = await stmt(env, `SELECT
  (SELECT COUNT(*) FROM password_attempts WHERE email=? AND success=0 AND created_at>=?
   AND id>COALESCE((SELECT MAX(id) FROM password_attempts WHERE email=? AND success=1),0)) AS emails,
  (SELECT COUNT(*) FROM password_attempts WHERE ip=? AND success=0 AND created_at>=?) AS ips`,
  email, iso(Date.now() - 15 * 60000), email, ip, dayStart()).first<{ emails: number; ips: number }>()
 if (counts!.emails >= 5 || counts!.ips >= 30)
  fail(429, 'rate_limited', '输错太多次，请 15 分钟后再试，或者用验证码登录。')
 const u = await stmt(env, 'SELECT * FROM users WHERE email=? AND deleted=0', email).first<UserRow>()
 const correct = !!u?.password_hash && await checkPassword(password, u.password_hash)
 await stmt(env, 'INSERT INTO password_attempts(email,ip,created_at,success) VALUES(?,?,?,?)', email, ip, iso(), Number(correct)).run()
 if (!correct) fail(401, 'bad_credentials', '邮箱或密码不对。')
 return createSession(ctx, u!, 'password')
}
export async function account(ctx: Context, path: string) {
 const { request, env } = ctx, method = request.method
 if (path === '/api/auth/code' && method === 'POST') return sendCode(ctx)
 if (path === '/api/auth/verify' && method === 'POST') return verify(ctx)
 if (path === '/api/auth/password' && method === 'POST') return passwordLogin(ctx)
 const u = (await authenticate(ctx))!
 if (path === '/api/auth/logout' && method === 'POST') {
  await stmt(env, 'DELETE FROM sessions WHERE token_hash=?', await hash(env.CODE_PEPPER + tokenFrom(request))).run()
  ctx.cookie = cookie('', 0)
  return empty()
 }
 if (path === '/api/me/password' && (method === 'PUT' || method === 'DELETE')) {
  const input = method === 'DELETE' && request.body === null ? {} : await body(request)
  const recentCode = u.login_method === 'code' && Date.now() - Date.parse(u.session_created_at) <= 30 * 60000
  if (u.password_hash && (input.currentPassword === undefined ? !recentCode :
   typeof input.currentPassword !== 'string' || !await checkPassword(input.currentPassword, u.password_hash)))
   fail(403, 'bad_credentials', '当前密码不对。')
  let passwordHash: string | null = null
  if (method === 'PUT') {
   // TODO(design): 密码位数沿用昵称的 Unicode 码点计数，待明确口径。
   const password = string(input.password), length = [...password].length
   if (length < 8 || length > 64 || password.toLowerCase() === u.email)
    fail(400, 'weak_password', '密码要 8～64 位，而且不能和邮箱一样。')
   passwordHash = await hashPassword(password)
  }
  await env.DB.batch([
   stmt(env, 'UPDATE users SET password_hash=? WHERE id=?', passwordHash, u.id),
   stmt(env, 'DELETE FROM sessions WHERE user_id=? AND token_hash<>?', u.id, await hash(env.CODE_PEPPER + tokenFrom(request))),
  ])
  // TODO(design): 删除密码的响应未定义，先沿用现有删除接口返回 204。
  return method === 'PUT' ? json(me({ ...u, password_hash: passwordHash })) : empty()
 }
 if (path === '/api/me') {
  if (method === 'GET') return json(me(u))
  if (method === 'PATCH') {
   const nickname = string((await body(request)).nickname).trim()
   if ([...nickname].length < 2 || [...nickname].length > 16) fail(400, 'bad_nickname', '昵称要 2～16 个字。')
   if (hasBlocked(nickname)) fail(400, 'blocked_words', '昵称里有不允许的词。')
   const result = await stmt(env, 'UPDATE OR IGNORE users SET nickname=?, nickname_key=? WHERE id=?', nickname, nickname.toLowerCase(), u.id).run()
   if (!result.meta.changes) fail(409, 'nickname_taken', '这个昵称已经有人用了。')
   return json(me({ ...u, nickname }))
  }
  if (method === 'DELETE') {
   await env.DB.batch([
    ...['favorites', 'cloud_lineups', 'cloud_prefs', 'sync_usage'].map(table => stmt(env, 'DELETE FROM ' + table + ' WHERE user_id=?', u.id)),
    stmt(env, 'DELETE FROM likes WHERE user_id=?', u.id),
    stmt(env, 'DELETE FROM comments WHERE user_id=?', u.id),
    stmt(env, 'DELETE FROM reports WHERE user_id=?', u.id),
    stmt(env, 'DELETE FROM report_usage WHERE user_id=?', u.id),
    stmt(env, 'DELETE FROM posts WHERE user_id=?', u.id),
    stmt(env, 'DELETE FROM sessions WHERE user_id=?', u.id),
    stmt(env, 'DELETE FROM login_codes WHERE email=?', u.email),
    stmt(env, 'DELETE FROM password_attempts WHERE email=?', u.email),
    stmt(env, 'UPDATE users SET email=NULL,nickname=NULL,nickname_key=NULL,password_hash=NULL,deleted=1 WHERE id=?', u.id),
   ])
   ctx.cookie = cookie('', 0)
   return empty()
  }
 }
 return fail(404, 'not_found', '接口不存在。')
}
