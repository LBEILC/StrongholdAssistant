import type { Notice, NoticeKind, NoticeList } from '../shared/api'
import { authenticate, type Context } from './auth'
import { body, DAY, empty, fail, iso, json, stmt, type Env } from './common'
import { pagination } from './pagination'

// 与管理操作放在同一批事务内；只在状态实际改变时发消息。
export function adminNotice(env: Env, kind: NoticeKind, id: string) {
 const comment = kind === 'comment_hidden'
 return stmt(env, `INSERT INTO notices(id,user_id,kind,post_id,post_name,comment_id,comment_body,created_at)
  SELECT ?,${comment ? 'c' : 'p'}.user_id,?,p.id,json_extract(p.lineup,'$.name'),
  ${comment ? 'c.id,c.body' : 'NULL,NULL'},? FROM posts p
  ${comment ? 'JOIN comments c ON c.post_id=p.id' : ''}
  WHERE ${comment ? 'c' : 'p'}.id=? AND ${kind === 'featured' ? 'p.featured=0' : `${comment ? 'c' : 'p'}.status<>'hidden'`}
  ${kind === 'post_hidden' ? '' : "AND p.status<>'deleted'"}`,
 crypto.randomUUID(), kind, iso(), id)
}

interface NoticeRow {
 id: string; kind: NoticeKind; created_at: string; read: number; post_id: string; post_name: string
 comment_id: string | null; comment_body: string | null
}
export async function notices(ctx: Context, path: string) {
 const { env, request } = ctx, u = (await authenticate(ctx))!
 const cutoff = iso(Date.now() - 90 * DAY)
 await env.DB.batch([
  stmt(env, 'DELETE FROM notice_actors WHERE created_at<?', cutoff),
  stmt(env, 'DELETE FROM notices WHERE created_at<?', cutoff),
 ])
 if (path === '/api/notices/read' && request.method === 'POST') {
  const input = await body(request)
  if (typeof input.before !== 'string' || !Number.isFinite(Date.parse(input.before)))
   return fail(400, 'bad_request', '请求内容不正确。')
  await stmt(env, 'UPDATE notices SET read=1 WHERE user_id=? AND read=0 AND created_at<=?', u.id, iso(Date.parse(input.before))).run()
  return empty()
 }
 if (request.method !== 'GET' || !['/api/notices', '/api/notices/unread'].includes(path))
  return fail(404, 'not_found', '接口不存在。')
 const unread = (await stmt(env, 'SELECT COUNT(*) AS n FROM notices WHERE user_id=? AND read=0', u.id).first<{ n: number }>())!.n
 if (path === '/api/notices/unread') return json({ unread })
 const page = pagination(new URL(request.url).searchParams, 'notices:' + u.id)
 const rows = (await stmt(env, `SELECT n.*,COALESCE(json_extract(p.lineup,'$.name'),n.post_name) AS post_name
  FROM notices n LEFT JOIN posts p ON p.id=n.post_id WHERE n.user_id=?
  ${page.after ? 'AND (n.created_at,n.id)<(?,?)' : ''}
  ORDER BY n.created_at DESC,n.id DESC LIMIT ?`, u.id,
  ...(page.after ? [page.after.time, page.after.id] : []), page.limit + 1).all<NoticeRow>()).results
 const result: Notice[] = []
 for (const n of rows.slice(0, page.limit)) {
  const actors = (await stmt(env, `SELECT u.id,COALESCE(u.nickname,'') AS nickname,COUNT(*) OVER() AS count
   FROM notice_actors a JOIN users u ON u.id=a.user_id WHERE a.notice_id=?
   ORDER BY a.created_at DESC,a.sequence DESC LIMIT 3`, n.id).all<{ id: string; nickname: string; count: number }>()).results
  result.push({ id: n.id, kind: n.kind, createdAt: n.created_at, read: !!n.read,
   post: { id: n.post_id, name: n.post_name }, actors: actors.map(({ id, nickname }) => ({ id, nickname })),
   actorCount: actors[0]?.count ?? 0,
   ...(n.comment_id && { comment: { id: n.comment_id, body: [...n.comment_body!].slice(0, 100).join('') } }),
  })
 }
 const last = result.at(-1)
 return json({ notices: result, unread, cursor: rows.length > page.limit ? page.cursor(last!.createdAt, last!.id) : null } satisfies NoticeList)
}
