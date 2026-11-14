import type { RatingKind, RatingSummary, RatingComment } from '../shared/api'
import { authenticate, type Context } from './auth'
import { body, clientIp, empty, fail, hash, iso, json, needNickname, stmt, type Env } from './common'
import catalog from '../data/catalog.json'
import { pagination } from './pagination'

interface RatingRow {
 id: string; kind: RatingKind; name: string; user_id: string; nickname: string
 score: RatingComment['score']; comment: string; comment_hidden: number; created_at: string; updated_at: string
}
const select = 'SELECT r.*,u.nickname FROM ratings r JOIN users u ON u.id=r.user_id'
export const getRating = (env: Env, id: string) => stmt(env, select + ' WHERE r.id=?', id).first<RatingRow>()
function kindOf(value: unknown): RatingKind {
 // TODO(design): 评分参数与限流错误提示先沿用简短中文，待统一。
 if (value !== 'operator' && value !== 'equipment') fail(400, 'bad_kind', '评分类型不正确。')
 return value
}
function target(kind: RatingKind, name: unknown): string {
 if (typeof name !== 'string' || !(kind === 'operator' ? catalog.operators : catalog.equipment).some(x => x.name === name))
  fail(404, 'not_found', '评分目标不存在。')
 return name
}
async function summaries(env: Env, kind: RatingKind, name?: string): Promise<RatingSummary[]> {
 const rows = (await stmt(env, `SELECT name,score,COUNT(*) AS n FROM ratings WHERE season=? AND kind=?` +
  (name === undefined ? '' : ' AND name=?') + ' GROUP BY name,score ORDER BY name,score',
  env.RATING_SEASON, kind, ...(name === undefined ? [] : [name])).all<{ name: string; score: number; n: number }>()).results
 const items = new Map<string, RatingSummary>()
 if (name !== undefined) items.set(name, { kind, name, count: 0, avg: null, dist: [0,0,0,0,0] })
 for (const row of rows) {
  if (!items.has(row.name)) items.set(row.name, { kind, name: row.name, count: 0, avg: null, dist: [0,0,0,0,0] })
  const item = items.get(row.name)!
  item.count += row.n
  item.dist[row.score - 1] = row.n
 }
 for (const item of items.values()) if (item.count >= 5)
  item.avg = Math.round(item.dist.reduce((sum, n, i) => sum + n * (i + 1), 0) / item.count * 10) / 10
 return [...items.values()]
}
export async function ratings(ctx: Context, path: string) {
 const { env, request } = ctx, method = request.method, q = new URL(request.url).searchParams, season = env.RATING_SEASON
 if (path === '/api/public/ratings' && method === 'GET') {
  const kind = kindOf(q.get('kind'))
  const response = json({ season, kind, items: await summaries(env, kind), at: iso() })
  response.headers.set('Cache-Control', 'public, max-age=0, s-maxage=120')
  return response
 }
 if (path === '/api/ratings/mine' && method === 'GET') {
  const u = (await authenticate(ctx))!
  const items = (await stmt(env, 'SELECT kind,name,score FROM ratings WHERE season=? AND user_id=? ORDER BY kind,name', season, u.id).all()).results
  return json({ season, items })
 }
 if (path === '/api/ratings/item' && method === 'GET') {
  const kind = kindOf(q.get('kind')), name = target(kind, q.get('name')), u = await authenticate(ctx, false)
  const page = pagination(q, JSON.stringify(['ratings', season, kind, encodeURIComponent(name)]))
  const rows = (await stmt(env, select + ` WHERE r.season=? AND r.kind=? AND r.name=? AND r.comment<>'' AND r.comment_hidden=0` +
   (page.after ? ' AND (r.updated_at,r.id)<(?,?)' : '') + ' ORDER BY r.updated_at DESC,r.id DESC LIMIT ?',
   season, kind, name, ...(page.after ? [page.after.time, page.after.id] : []), page.limit + 1).all<RatingRow>()).results
  const visible = rows.slice(0, page.limit), last = visible.at(-1)!
  const mine = u ? await stmt(env, 'SELECT score,comment,updated_at AS updatedAt FROM ratings WHERE season=? AND kind=? AND name=? AND user_id=?', season, kind, name, u.id).first() : null
  return json({ season, summary: (await summaries(env, kind, name))[0], mine,
   comments: visible.map(r => ({ id: r.id, author: { id: r.user_id, nickname: r.nickname }, score: r.score,
    comment: r.comment, createdAt: r.created_at, updatedAt: r.updated_at })),
   nextCursor: rows.length > page.limit ? page.cursor(last.updated_at, last.id) : null })
 }
 if (path === '/api/ratings' && (method === 'PUT' || method === 'DELETE')) {
  const u = (await authenticate(ctx))!
  if (method === 'DELETE') {
   const kind = kindOf(q.get('kind')), name = target(kind, q.get('name'))
   await stmt(env, 'DELETE FROM ratings WHERE season=? AND kind=? AND name=? AND user_id=?', season, kind, name, u.id).run()
   return empty()
  }
  needNickname(u)
  const input = await body(request), kind = kindOf(input.kind), name = target(kind, input.name)
  if (typeof input.score !== 'number' || !Number.isInteger(input.score) || input.score < 1 || input.score > 5)
   fail(400, 'bad_score', '评分要为 1～5 的整数。')
  if (input.comment !== undefined && typeof input.comment !== 'string') fail(400, 'bad_comment', '短评内容不正确。')
  const raw = input.comment as string | undefined ?? '', comment = raw.trim()
  if ([...comment].length > 100 || /[\r\n\u2028\u2029]/u.test(raw)) fail(400, 'bad_comment', '短评最多 100 字，不能换行。')
  const ip = await hash(env.IP_SECRET + ':rating:' + clientIp(env, request)), now = iso()
  await stmt(env, 'DELETE FROM rating_usage WHERE ip_hash=? AND created_at<=?', ip, iso(Date.now() - 3600000)).run()
  const usage = await stmt(env, 'INSERT INTO rating_usage SELECT ?,? WHERE (SELECT COUNT(*) FROM rating_usage WHERE ip_hash=?)<60', ip, now, ip).run()
  if (!usage.meta.changes) fail(429, 'rate_limited', '评分太频繁，请稍后再试。')
  await stmt(env, `INSERT INTO ratings(season,kind,name,user_id,id,score,comment,created_at,updated_at) VALUES(?,?,?,?,?,?,?,?,?)
   ON CONFLICT(season,kind,name,user_id) DO UPDATE SET score=excluded.score,comment=excluded.comment,
   comment_hidden=CASE WHEN ratings.comment<>excluded.comment THEN 0 ELSE ratings.comment_hidden END,updated_at=excluded.updated_at`,
   season, kind, name, u.id, crypto.randomUUID(), input.score, comment, now, now).run()
  return json((await summaries(env, kind, name))[0])
 }
 return fail(404, 'not_found', '接口不存在。')
}
