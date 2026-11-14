import { createRemoteJWKSet, jwtVerify } from 'jose'
import { type Context } from './auth'
import { body, fail, iso, json, me, stmt, type UserRow } from './common'
import { getPost, listPosts, toAdminPost } from './posts'
import { listComments, updateComment } from './comments'
import { listReports, resolveReport } from './reports'
import { adminNotice } from './notices'
import { stats } from './stats'
import { mirrorStats } from './mirror'
import { trafficStats } from './traffic'

// 公钥由库按缓存期限刷新；团队域名来自部署配置，不能由请求指定。
const keys = new Map<string, ReturnType<typeof createRemoteJWKSet>>()
async function authorize(ctx: Context) {
 const { env, request } = ctx
 const token = request.headers.get('Cf-Access-Jwt-Assertion')
 if (!token || !env.ACCESS_TEAM_DOMAIN || !env.ACCESS_AUD) fail(403, 'forbidden', '无权访问后台。')
 const issuer = 'https://' + env.ACCESS_TEAM_DOMAIN
 try {
  if (!keys.has(issuer)) keys.set(issuer, createRemoteJWKSet(new URL(issuer + '/cdn-cgi/access/certs')))
  const { payload } = await jwtVerify(token, keys.get(issuer)!, {
   issuer, audience: env.ACCESS_AUD, algorithms: ['RS256'], requiredClaims: ['exp', 'email'],
  })
  const allowed = env.ADMIN_EMAILS.split(',').map(s => s.trim().toLowerCase())
  if (typeof payload.email !== 'string' || !allowed.includes(payload.email.toLowerCase())) throw new Error()
 } catch { fail(403, 'forbidden', '无权访问后台。') }
}
export async function admin(ctx: Context, path: string) {
 await authorize(ctx)
 const { env, request } = ctx, method = request.method
 if (path === '/api/admin/stats' && method === 'GET') return stats(ctx)
 if (path === '/api/admin/traffic' && method === 'GET') return trafficStats(ctx)
 if (path === '/api/admin/mirror' && method === 'GET') return mirrorStats(ctx)
 if (path === '/api/admin/comments' && method === 'GET') return listComments(ctx, null, null, null)
 if (path === '/api/admin/reports' && method === 'GET') return listReports(ctx)
 const comment = path.match(/^\/api\/admin\/comments\/([^/]+)$/)
 if (comment && method === 'PATCH') return updateComment(ctx, comment[1])
 const report = path.match(/^\/api\/admin\/reports\/([^/]+)\/([^/]+)$/)
 if (report && method === 'PATCH') return resolveReport(ctx, report[1], report[2])
 if (path === '/api/admin/posts' && method === 'GET') return listPosts(ctx, null, true)
 if (path === '/api/admin/users' && method === 'GET') {
  const query = new URL(request.url).searchParams.get('q') ?? ''
  const rows = (await stmt(env, `SELECT u.*, (SELECT COUNT(*) FROM posts p WHERE p.user_id=u.id) AS posts
   FROM users u WHERE instr(lower(COALESCE(u.nickname,'')),lower(?))>0 OR instr(COALESCE(u.email,''),lower(?))>0
   ORDER BY u.created_at DESC,u.id DESC`, query, query).all<UserRow & { posts: number }>()).results
  return json(rows.map(u => ({ ...me(u), banned: !!u.banned, posts: u.posts })))
 }
 const post = path.match(/^\/api\/admin\/posts\/([^/]+)$/)
 if (post && method === 'PATCH') {
  const input = await body(request)
  if (input.status !== undefined && !['visible', 'hidden', 'deleted'].includes(String(input.status))) fail(400, 'bad_request', '状态参数不正确。')
  if (input.featured !== undefined && typeof input.featured !== 'boolean') fail(400, 'bad_request', '精选参数不正确。')
  const existing = await getPost(env, post[1], null)
  if (!existing) fail(404, 'not_found', '帖子不存在。')
  const changes = []
  if (input.featured === true) changes.push(adminNotice(env, 'featured', post[1]))
  if (input.status === 'hidden') changes.push(adminNotice(env, 'post_hidden', post[1]))
  changes.push(stmt(env, 'UPDATE posts SET status=?,featured=?,updated_at=? WHERE id=?',
   input.status === undefined ? existing.status : String(input.status),
   input.featured === undefined ? existing.featured : Number(input.featured), iso(), post[1]))
  await env.DB.batch(changes)
  return json(toAdminPost((await getPost(env, post[1], null))!))
 }
 const user = path.match(/^\/api\/admin\/users\/([^/]+)$/)
 if (user && method === 'PATCH') {
  const input = await body(request)
  if (typeof input.banned !== 'boolean') fail(400, 'bad_request', '封禁参数不正确。')
  if (!await stmt(env, 'SELECT id FROM users WHERE id=?', user[1]).first()) fail(404, 'not_found', '账号不存在。')
  const changes = [stmt(env, 'UPDATE users SET banned=? WHERE id=?', Number(input.banned), user[1])]
  if (input.banned) changes.push(
   stmt(env, "UPDATE posts SET status='hidden',updated_at=? WHERE user_id=?", iso(), user[1]),
   stmt(env, "UPDATE comments SET status='hidden' WHERE user_id=?", user[1]),
   stmt(env, 'UPDATE ratings SET comment_hidden=1 WHERE user_id=?', user[1]),
   stmt(env, 'UPDATE sessions SET expires_at=? WHERE user_id=?', iso(), user[1]),
  )
  await env.DB.batch(changes)
  const u = (await stmt(env, 'SELECT u.*,(SELECT COUNT(*) FROM posts p WHERE p.user_id=u.id) AS posts FROM users u WHERE id=?', user[1]).first<UserRow & { posts: number }>())!
  return json({ ...me(u), banned: !!u.banned, posts: u.posts })
 }
 return fail(404, 'not_found', '接口不存在。')
}
