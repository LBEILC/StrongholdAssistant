import type { AdminComment, Comment } from '../shared/api'
import { authenticate, type Context } from './auth'
import { body, dayStart, empty, fail, hasBlocked, iso, json, mask, needNickname, stmt, type Env } from './common'
import { getPost } from './posts'
import { pagination } from './pagination'
import { adminNotice } from './notices'

interface CommentRow {
 id: string; post_id: string; user_id: string; body: string; reply_to: string | null; reply_nickname: string | null
 status: 'visible' | 'hidden' | 'deleted'; created_at: string; nickname: string; email: string | null; post_title: string
}
const select = `SELECT c.*,u.nickname,u.email,json_extract(p.lineup,'$.name') AS post_title
 FROM comments c JOIN users u ON u.id=c.user_id JOIN posts p ON p.id=c.post_id`
export const getComment = (env: Env, id: string) => stmt(env, select + ' WHERE c.id=?', id).first<CommentRow>()
function fields(c: CommentRow): Omit<Comment, 'canDelete'> {
 return { id: c.id, postId: c.post_id, author: { id: c.user_id, nickname: c.nickname }, body: c.body,
  replyTo: c.reply_to ? { id: c.reply_to, nickname: c.reply_nickname! } : null, createdAt: c.created_at }
}
export const toAdminComment = (c: CommentRow): AdminComment => ({
 ...fields(c), status: c.status, postTitle: c.post_title, authorEmailMasked: mask(c.email),
})
export async function listComments(ctx: Context, postId: string | null, viewer: string | null, owner: string | null) {
 const q = new URL(ctx.request.url).searchParams, admin = postId === null
 const where: string[] = [], values: (string | number)[] = []
 if (!admin) { where.push("c.post_id=? AND c.status='visible'"); values.push(postId!) }
 else {
  if (q.has('post')) { where.push('c.post_id=?'); values.push(q.get('post')!) }
  if (q.has('status')) {
   const status = q.get('status')!
   if (!['visible', 'hidden', 'deleted'].includes(status)) fail(400, 'bad_query', '状态参数不正确。')
   where.push('c.status=?'); values.push(status)
  }
 }
 const page = pagination(q, JSON.stringify(['comments', admin, where, values]))
 if (page.after) { where.push('(c.created_at,c.id)<(?,?)'); values.push(page.after.time, page.after.id) }
 const rows = (await stmt(ctx.env, select + (where.length ? ' WHERE ' + where.join(' AND ') : '') +
  ' ORDER BY c.created_at DESC,c.id DESC LIMIT ?', ...values, page.limit + 1).all<CommentRow>()).results
 const visible = rows.slice(0, page.limit), last = visible.at(-1)
 return json({ comments: visible.map(c => admin ? toAdminComment(c) : {
  ...fields(c), canDelete: viewer !== null && (viewer === c.user_id || viewer === owner),
 }), cursor: rows.length > page.limit ? page.cursor(last!.created_at, last!.id) : null })
}
export async function comments(ctx: Context, path: string) {
 const { env, request } = ctx, method = request.method
 const list = path.match(/^\/api\/posts\/([^/]+)\/comments$/)
 const item = path.match(/^\/api\/comments\/([^/]+)$/)
 const c = item ? await getComment(env, item[1]) : null
 const p = await getPost(env, list ? list[1] : c?.post_id ?? '', null)
 if (!p || p.status !== 'visible') fail(404, 'not_found', '帖子不存在。')
 const u = await authenticate(ctx, false)
 if (list && method === 'GET') return listComments(ctx, p.id, u?.id ?? null, p.user_id)
 if (!u) fail(401, 'unauthorized', '请先登录。')
 if (list && method === 'POST') {
  needNickname(u)
  const input = await body(request)
  // TODO(design): 新增评论与举报的错误提示先用简短中文，待设计统一。
  if (typeof input.body !== 'string' || !input.body.trim() || [...input.body.trim()].length > 300)
   fail(400, 'bad_body', '评论要 1～300 个字。')
  const text = input.body.trim()
  if (hasBlocked(text)) fail(400, 'blocked_words', '评论里有不允许的词。')
  let reply: CommentRow | null = null
  if (input.replyTo !== undefined) {
   if (typeof input.replyTo !== 'string') fail(400, 'bad_reply', '回复的评论不存在或不可见。')
   reply = await getComment(env, input.replyTo)
   if (!reply || reply.post_id !== p.id || reply.status !== 'visible') fail(400, 'bad_reply', '回复的评论不存在或不可见。')
  }
  const id = crypto.randomUUID(), now = iso(), start = dayStart(), cutoff = iso(Date.now() - 10000)
  const inserted = await stmt(env, `INSERT INTO comments(id,post_id,user_id,body,reply_to,reply_nickname,created_at)
   SELECT ?,?,?,?,?,?,? WHERE (SELECT COUNT(*) FROM comments WHERE user_id=? AND created_at>=?)<30
   AND NOT EXISTS(SELECT 1 FROM comments WHERE user_id=? AND created_at>?) RETURNING id`,
   id, p.id, u.id, text, reply?.id ?? null, reply?.nickname ?? null, now, u.id, start, u.id, cutoff).first()
  if (!inserted) {
   const count = (await stmt(env, 'SELECT COUNT(*) AS n FROM comments WHERE user_id=? AND created_at>=?', u.id, start).first<{ n: number }>())!.n
   if (count >= 30) fail(429, 'rate_limited', '今天评论次数已用完，请明天再试。')
   fail(429, 'rate_limited', '评论太频繁，两条评论之间至少间隔 10 秒。')
  }
  return json({ ...fields((await getComment(env, id))!), canDelete: true })
 }
 if (item && method === 'DELETE') {
  if (!c || c.status !== 'visible') fail(404, 'not_found', '评论不存在。')
  if (u.id !== c.user_id && u.id !== p.user_id) fail(403, 'forbidden', '只能删除自己的评论或自己帖子下的评论。')
  await stmt(env, "UPDATE comments SET status='deleted' WHERE id=?", c.id).run()
  return empty()
 }
 return fail(404, 'not_found', '接口不存在。')
}
export async function updateComment(ctx: Context, id: string) {
 const input = await body(ctx.request)
 if (!['visible', 'hidden', 'deleted'].includes(String(input.status))) fail(400, 'bad_request', '状态参数不正确。')
 if (!await getComment(ctx.env, id)) fail(404, 'not_found', '评论不存在。')
 await ctx.env.DB.batch([
  ...(input.status === 'hidden' ? [adminNotice(ctx.env, 'comment_hidden', id)] : []),
  stmt(ctx.env, 'UPDATE comments SET status=? WHERE id=?', String(input.status), id),
 ])
 return empty()
}
