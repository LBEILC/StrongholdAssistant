import type { AdminPost, Post, PostLineup } from '../shared/api'
import { catalog, decodeLineup, fingerprint } from './catalog'
import { authenticate, type Context } from './auth'
import { body, dayStart, description, empty, fail, hasBlocked, hash, iso, json, mask, needNickname, stmt, string, type Env } from './common'

export interface PostRow {
 id: string; user_id: string; code: string; fingerprint: number; lineup: string; description: string
 status: 'visible' | 'hidden' | 'deleted'; featured: number; likes: number; imports: number
 created_at: string; updated_at: string; nickname: string; email: string | null; liked: number; rank: number
}
const select = `SELECT p.*,u.nickname,u.email,
 EXISTS(SELECT 1 FROM likes l WHERE l.post_id=p.id AND l.user_id=?) AS liked`
export function toPost(p: PostRow): Post {
 return { id: p.id, code: p.code, lineup: JSON.parse(p.lineup) as PostLineup, description: p.description,
  author: { id: p.user_id, nickname: p.nickname }, createdAt: p.created_at, updatedAt: p.updated_at,
  featured: !!p.featured, likes: p.likes, imports: p.imports, liked: !!p.liked, current: p.fingerprint === fingerprint }
}
export const toAdminPost = (p: PostRow): AdminPost => ({
 ...toPost(p), status: p.status, ...(p.status === 'hidden' && { hidden: true }), authorEmailMasked: mask(p.email),
})
export async function getPost(env: Env, id: string, viewer: string | null) {
 return stmt(env, select + ' FROM posts p JOIN users u ON u.id=p.user_id WHERE p.id=?', viewer, id).first<PostRow>()
}
export async function listPosts(ctx: Context, viewer: string | null, admin = false) {
 const q = new URL(ctx.request.url).searchParams, env = ctx.env
 const sort = admin ? 'new' : q.get('sort') ?? 'hot'
 if (!['new', 'hot', 'featured'].includes(sort)) fail(400, 'bad_query', '排序参数不正确。')
 const limit = Math.min(50, Number(q.get('limit') ?? 20))
 if (!Number.isInteger(limit) || limit < 1) fail(400, 'bad_query', '分页参数不正确。')
 const rank = sort === 'hot' ? '(p.likes*2+p.imports)' : sort === 'featured' ? 'p.featured' : '0'
 const where: string[] = [], values: (string | number)[] = []
 if (admin) {
  const status = q.get('status')
  if (status) {
   if (!['visible', 'hidden', 'deleted'].includes(status)) fail(400, 'bad_query', '状态参数不正确。')
   where.push('p.status=?'); values.push(status)
  }
 } else {
  where.push("p.status='visible'")
  if (sort === 'featured') where.push('p.featured=1')
  const season = q.get('season') ?? 'current'
  if (!['current', 'all'].includes(season)) fail(400, 'bad_query', '赛季参数不正确。')
  if (q.has('author')) { where.push('p.user_id=?'); values.push(q.get('author')!) }
  else if (season === 'current') { where.push('p.fingerprint=?'); values.push(fingerprint) }
  for (const covenant of q.getAll('covenant')) {
   where.push("EXISTS(SELECT 1 FROM json_each(p.lineup,'$.goals') g WHERE json_extract(g.value,'$.id')=?)")
   values.push(covenant)
  }
  if (q.has('strategy')) { where.push("json_extract(p.lineup,'$.strategyId')=?"); values.push(q.get('strategy')!) }
 }
 // 游标绑定当前筛选，防止换排序后误用旧游标。
 const queryKey = JSON.stringify([admin, sort, where, values])
 if (q.has('cursor')) {
  let cursor: { key: string; rank: number; time: string; id: string }
  try {
   cursor = JSON.parse(atob(q.get('cursor')!))
   if (cursor.key !== queryKey || typeof cursor.rank !== 'number' || typeof cursor.time !== 'string' || typeof cursor.id !== 'string') throw new Error()
  } catch { return fail(400, 'bad_cursor', '分页参数不正确。') }
  where.push(`(${rank},p.created_at,p.id)<(?,?,?)`)
  values.push(cursor.rank, cursor.time, cursor.id)
 }
 const rows = (await stmt(env, select + `,${rank} AS rank FROM posts p JOIN users u ON u.id=p.user_id` +
  (where.length ? ' WHERE ' + where.join(' AND ') : '') + ` ORDER BY ${sort === 'new' ? '' : rank + ' DESC,'}p.created_at DESC,p.id DESC LIMIT ?`,
  viewer, ...values, limit + 1).all<PostRow>()).results
 const more = rows.length > limit
 const page = rows.slice(0, limit), last = page.at(-1)
 const cursor = more ? btoa(JSON.stringify({ key: queryKey, rank: last!.rank, time: last!.created_at, id: last!.id })) : null
 return json({ posts: page.map(admin ? toAdminPost : toPost), cursor })
}
export async function posts(ctx: Context, path: string) {
 const { env, request } = ctx, method = request.method
 const u = await authenticate(ctx, false)
 if (path === '/api/posts' && method === 'GET') return listPosts(ctx, u?.id ?? null)
 if (path === '/api/posts' && method === 'POST') {
  if (!u) fail(401, 'unauthorized', '请先登录。')
  needNickname(u)
  const input = await body(request), text = string(input.code), desc = description(input.description)
  const decoded = decodeLineup(text, catalog)
  if (!decoded.ok) {
   if (decoded.error.includes('其他赛季')) fail(400, 'season_mismatch', '这个分享码来自其他赛季或其他版本的数据，请先把助手更新到最新版。')
   fail(400, 'bad_share_code', decoded.error)
  }
  const { name, goals, members, equipment, strategyId } = decoded.lineup
  if (hasBlocked(name)) fail(400, 'blocked_words', '阵容名或简介里有不允许的词。')
  // SA1 和 SA2（带干员候选装备）都认，decodeLineup 已经校验过格式
  const code = text.match(/SA\d+-[A-Za-z0-9_-]+/)![0]
  const existing = await stmt(env, 'SELECT id,status FROM posts WHERE user_id=? AND code=?', u.id, code).first<{ id: string; status: string }>()
  if (existing) {
   // TODO(design): 重发已下架或删除的分享码暂不恢复可见状态，需确认后续产品提示。
   await stmt(env, 'UPDATE posts SET description=?,updated_at=? WHERE id=?', desc, iso(), existing.id).run()
   return json(toPost((await getPost(env, existing.id, u.id))!))
  }
  const id = crypto.randomUUID(), now = iso()
  const inserted = await stmt(env, `INSERT INTO posts(id,user_id,code,fingerprint,lineup,description,created_at,updated_at)
   SELECT ?,?,?,?,?,?,?,? WHERE (SELECT COUNT(*) FROM posts WHERE user_id=? AND created_at>=?)<10
   ON CONFLICT(user_id,code) DO UPDATE SET description=excluded.description,updated_at=excluded.updated_at RETURNING id`,
   id, u.id, code, fingerprint, JSON.stringify({ name, goals, members, equipment, strategyId }), desc, now, now, u.id, dayStart()).first<{ id: string }>()
  if (!inserted) fail(429, 'rate_limited', '今天发布次数已用完，请明天再试。')
  return json(toPost((await getPost(env, inserted.id, u.id))!))
 }
 const match = path.match(/^\/api\/posts\/([^/]+)(?:\/(like|import))?$/)
 if (!match) fail(404, 'not_found', '接口不存在。')
 const [, id, action] = match
 const p = await getPost(env, id, u?.id ?? null)
 if (!p || p.status === 'deleted' || (p.status === 'hidden' && (u?.id !== p.user_id || action)))
  fail(404, 'not_found', '帖子不存在。')
 if (!action && method === 'GET') return json({ ...toPost(p), ...(p.status === 'hidden' && { hidden: true }) })
 if (action === 'import' && method === 'POST') {
  if (p.fingerprint !== fingerprint) fail(400, 'season_mismatch', '这个帖子来自其他赛季，不能导入。')
  const device = string((await body(request)).device)
  if (!device.trim() || device.length > 256) fail(400, 'bad_device', '设备标识不正确。')
  await stmt(env, 'INSERT OR IGNORE INTO imports VALUES(?,?)', id, await hash(device)).run()
  return json({ imports: (await getPost(env, id, u?.id ?? null))!.imports })
 }
 if (!u) fail(401, 'unauthorized', '请先登录。')
 if (action === 'like' && ['POST', 'DELETE'].includes(method)) {
  needNickname(u)
  await stmt(env, method === 'POST' ? 'INSERT OR IGNORE INTO likes VALUES(?,?)' : 'DELETE FROM likes WHERE post_id=? AND user_id=?', id, u.id).run()
  const updated = (await getPost(env, id, u.id))!
  return json({ likes: updated.likes, liked: !!updated.liked })
 }
 if (!action && ['PATCH', 'DELETE'].includes(method)) {
  if (p.user_id !== u.id) fail(403, 'forbidden', '只能修改自己的帖子。')
  if (method === 'PATCH') {
   const desc = description((await body(request)).description)
   await stmt(env, 'UPDATE posts SET description=?,updated_at=? WHERE id=?', desc, iso(), id).run()
   return json(toPost((await getPost(env, id, u.id))!))
  }
  await stmt(env, "UPDATE posts SET status='deleted',updated_at=? WHERE id=?", iso(), id).run()
  return empty()
 }
 return fail(404, 'not_found', '接口不存在。')
}
