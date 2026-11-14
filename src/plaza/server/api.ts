import { account, type Context } from './auth'
import { admin } from './admin'
import { reportTraffic } from './traffic'
import { posts, publicPosts } from './posts'
import { comments } from './comments'
import { report } from './reports'
import { ratings } from './ratings'
import { notices } from './notices'
import { sync } from './sync'
import { incomeTable, reportIncome } from './income'
import { ApiFailure, empty, fail, json, type Env } from './common'
export type { Env } from './common'

export async function handleRequest(request: Request, env: Env): Promise<Response> {
 const ctx: Context = { request, env }, url = new URL(request.url)
 const origin = request.headers.get('Origin')
 const allowed = origin && (origin === url.origin || env.ALLOWED_ORIGINS.split(',').map(s => s.trim()).includes(origin))
 let response: Response
 try {
  // Cookie 写请求也限制来源，避免其他站点借用户登录状态修改数据。
  if (origin && !allowed) fail(403, 'forbidden', '此来源无权访问。')
  if (request.method === 'OPTIONS') response = empty()
  else if (url.pathname === '/api/edge/traffic' && request.method === 'POST') response = await reportTraffic(ctx)
  else if (url.pathname.startsWith('/api/admin/')) response = await admin(ctx, url.pathname)
  else if (url.pathname === '/api/me' || url.pathname === '/api/me/password' || url.pathname.startsWith('/api/auth/')) response = await account(ctx, url.pathname)
  else if (/^\/api\/posts\/[^/]+\/comments$/.test(url.pathname) || /^\/api\/comments\/[^/]+$/.test(url.pathname)) response = await comments(ctx, url.pathname)
  else if (url.pathname === '/api/notices' || url.pathname.startsWith('/api/notices/')) response = await notices(ctx, url.pathname)
  else if (url.pathname === '/api/public/ratings' || url.pathname === '/api/ratings' || url.pathname.startsWith('/api/ratings/')) response = await ratings(ctx, url.pathname)
  else if (url.pathname === '/api/reports' && request.method === 'POST') response = await report(ctx)
  else if (url.pathname === '/api/sync' && request.method === 'POST') response = await sync(ctx)
  else if (url.pathname === '/api/income' && request.method === 'POST') response = await reportIncome(ctx)
  else if (url.pathname === '/api/income-table' && request.method === 'GET') response = await incomeTable(ctx)
  else if (url.pathname === '/api/public/posts' && request.method === 'GET') response = await publicPosts(ctx)
  else if (url.pathname === '/api/posts' || url.pathname.startsWith('/api/posts/')) response = await posts(ctx, url.pathname)
  else fail(404, 'not_found', '接口不存在。')
 } catch (error) {
  if (error instanceof ApiFailure) response = json({ error: error.code, message: error.message }, error.status)
  else response = json({ error: 'server_error', message: '请求处理失败，请稍后再试。' }, 500)
 }
 if (!response.headers.has('Cache-Control')) response.headers.set('Cache-Control', 'no-store')
 response.headers.set('Vary', 'Origin')
 if (allowed) {
  response.headers.set('Access-Control-Allow-Origin', origin!)
  response.headers.set('Access-Control-Allow-Credentials', 'true')
  response.headers.set('Access-Control-Allow-Headers', 'Authorization, Content-Type')
  response.headers.set('Access-Control-Allow-Methods', 'GET, POST, PUT, PATCH, DELETE, OPTIONS')
 }
 if (ctx.cookie) response.headers.set('Set-Cookie', ctx.cookie)
 return response
}
