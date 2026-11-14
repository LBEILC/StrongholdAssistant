import type { AdminReport, ReportReason, ReportTarget } from '../shared/api'
import { authenticate, type Context } from './auth'
import { body, dayStart, empty, fail, iso, json, stmt } from './common'
import { getComment, toAdminComment } from './comments'
import { getPost, toAdminPost } from './posts'
import { pagination } from './pagination'
import { adminNotice } from './notices'

export async function report(ctx: Context) {
 const { env, request } = ctx
 const u = (await authenticate(ctx))!, input = await body(request)
 const target = input.target as ReportTarget | undefined
 if (!target || !['post', 'comment'].includes(target.kind) || typeof target.id !== 'string')
  fail(400, 'bad_target', '举报目标不正确。')
 if (!['spam', 'offensive', 'unrelated', 'broken', 'other'].includes(String(input.reason)) ||
  (target.kind === 'comment' && input.reason === 'broken')) fail(400, 'bad_reason', '举报理由不适用于这个目标。')
 if (input.detail !== undefined && (typeof input.detail !== 'string' || [...input.detail].length > 200))
  fail(400, 'bad_detail', '举报说明最多 200 字。')
 const c = target.kind === 'comment' ? await getComment(env, target.id) : null
 const p = await getPost(env, target.kind === 'post' ? target.id : c?.post_id ?? '', null)
 if (!p || p.status !== 'visible' || (target.kind === 'comment' && c?.status !== 'visible'))
  fail(404, 'not_found', '举报目标不存在或不可见。')
 // 单独计次，重复更新同一举报也消耗当天额度；触发器和写入在同一事务内。
 const inserted = await stmt(env, `INSERT INTO reports(target_kind,target_id,user_id,reason,detail,created_at)
  SELECT ?,?,?,?,?,? WHERE COALESCE((SELECT count FROM report_usage WHERE user_id=? AND day=date(?,'+8 hours')),0)<20
  ON CONFLICT(target_kind,target_id,user_id) DO UPDATE SET reason=excluded.reason,detail=excluded.detail,
  created_at=excluded.created_at,resolved_at=NULL RETURNING target_id`,
  target.kind, target.id, u.id, String(input.reason), (input.detail as string | undefined ?? '').trim(), iso(), u.id, dayStart()).first()
 if (!inserted) fail(429, 'rate_limited', '今天举报次数已用完，请明天再试。')
 return empty()
}
interface ReportGroup {
 target_kind: 'post' | 'comment'; target_id: string; count: number; first_at: string; last_at: string; open: number; key: string
}
export async function listReports(ctx: Context) {
 const { env, request } = ctx, q = new URL(request.url).searchParams, status = q.get('status') ?? 'open'
 if (!['open', 'resolved'].includes(status)) fail(400, 'bad_query', '状态参数不正确。')
 const page = pagination(q, JSON.stringify(['reports', status]))
 const grouped = `SELECT target_kind,target_id,COUNT(*) AS count,MIN(created_at) AS first_at,MAX(created_at) AS last_at,
  MAX(resolved_at IS NULL) AS open,target_kind||':'||target_id AS key FROM reports GROUP BY target_kind,target_id`
 const args: (string | number)[] = [status === 'open' ? 1 : 0]
 if (page.after) args.push(page.after.time, page.after.id)
 const groups = (await stmt(env, `SELECT * FROM (${grouped}) WHERE open=?` +
  (page.after ? ' AND (last_at,key)<(?,?)' : '') + ' ORDER BY last_at DESC,key DESC LIMIT ?',
  ...args, page.limit + 1).all<ReportGroup>()).results
 const reports: AdminReport[] = []
 for (const g of groups.slice(0, page.limit)) {
  const c = g.target_kind === 'comment' ? (await getComment(env, g.target_id))! : null
  const p = (await getPost(env, c ? c.post_id : g.target_id, null))!
  const reasons = (await stmt(env, `SELECT reason,COUNT(*) AS n FROM reports WHERE target_kind=? AND target_id=? GROUP BY reason`,
   g.target_kind, g.target_id).all<{ reason: ReportReason; n: number }>()).results
  const details = (await stmt(env, `SELECT detail FROM reports WHERE target_kind=? AND target_id=? AND detail<>''
   ORDER BY created_at DESC,user_id DESC LIMIT 5`, g.target_kind, g.target_id).all<{ detail: string }>()).results
  reports.push({ target: { kind: g.target_kind, id: g.target_id }, post: toAdminPost(p), comment: c ? toAdminComment(c) : null,
   count: g.count, reasons: Object.fromEntries(reasons.map(r => [r.reason, r.n])), details: details.map(r => r.detail),
   firstAt: g.first_at, lastAt: g.last_at, status: g.open ? 'open' : 'resolved' })
 }
 const openCount = (await stmt(env, `SELECT COUNT(*) AS n FROM (${grouped}) WHERE open=1`).first<{ n: number }>())!.n
 const last = groups[page.limit - 1]
 return json({ reports, openCount, cursor: groups.length > page.limit ? page.cursor(last.last_at, last.key) : null })
}
export async function resolveReport(ctx: Context, kind: string, id: string) {
 const { env, request } = ctx, input = await body(request)
 if (!['post', 'comment'].includes(kind)) fail(400, 'bad_target', '举报目标不正确。')
 if (!['dismiss', 'hide', 'delete'].includes(String(input.action))) fail(400, 'bad_action', '举报处理方式不正确。')
 if (!await stmt(env, 'SELECT 1 FROM reports WHERE target_kind=? AND target_id=?', kind, id).first())
  fail(404, 'not_found', '举报不存在。')
 const changes = [stmt(env, 'UPDATE reports SET resolved_at=? WHERE target_kind=? AND target_id=?', iso(), kind, id)]
 if (input.action !== 'dismiss') {
  const status = input.action === 'hide' ? 'hidden' : 'deleted'
  if (input.action === 'hide') changes.push(adminNotice(env, kind === 'post' ? 'post_hidden' : 'comment_hidden', id))
  changes.push(kind === 'post' ? stmt(env, 'UPDATE posts SET status=?,updated_at=? WHERE id=?', status, iso(), id) :
   stmt(env, 'UPDATE comments SET status=? WHERE id=?', status, id))
 }
 await env.DB.batch(changes)
 return empty()
}
