import type { CloudLineup, CloudPrefs, SyncResponse } from '../shared/api'
import { authenticate, type Context } from './auth'
import { body, DAY, fail, iso, json, stmt, string } from './common'

interface Row { id: string; data: string | null; updated_at: string; deleted: number; changed_at: string }
const lineup = (r: Row): CloudLineup => ({ id: r.id, data: r.data === null ? null : JSON.parse(r.data), updatedAt: r.updated_at, deleted: !!r.deleted })
function timestamp(value: unknown) {
 const text = string(value)
 if (!Number.isFinite(Date.parse(text))) fail(400, 'bad_request', '请求内容不正确。')
 return iso(Date.parse(text))
}
function object(value: unknown): Record<string, unknown> {
 if (!value || typeof value !== 'object' || Array.isArray(value)) fail(400, 'bad_request', '请求内容不正确。')
 return value as Record<string, unknown>
}
function dataJson(value: unknown) {
 const data = JSON.stringify(object(value))
 if (new TextEncoder().encode(data).length > 64 * 1024) fail(400, 'too_large', '单套数据最多 64 KB。')
 return data
}
function prefsData(value: unknown) {
 const input = object(value), data: Record<string, unknown> = {}
 for (const key of ['focusOperators', 'focusCovenants', 'gameTheme', 'shopMarkers', 'operatorDefaults']) {
  if (!(key in input)) continue
  const v = input[key]
  const valid = key === 'focusOperators' || key === 'focusCovenants'
   ? Array.isArray(v) && v.every(item => typeof item === 'string')
   : key === 'gameTheme' ? ['terminal', 'ursus', 'closure'].includes(v as string)
   : key === 'shopMarkers' ? typeof v === 'boolean'
   : v !== null && typeof v === 'object' && !Array.isArray(v)
  if (!valid) fail(400, 'bad_request', '请求内容不正确。')
  data[key] = v
 }
 return data
}
export async function sync(ctx: Context) {
 const u = (await authenticate(ctx))!, { env } = ctx, input = await body(ctx.request, 2 * 1024 * 1024)
 const since = input.since === null ? null : timestamp(input.since)
 if (!Array.isArray(input.lineups)) fail(400, 'bad_request', '请求内容不正确。')
 if (input.lineups.length > 400) fail(400, 'too_large', '请求内容过大。')
 const ids = new Set<string>()
 const uploads = input.lineups.map(value => {
  const item = object(value), id = string(item.id), updatedAt = timestamp(item.updatedAt)
  if (id.length > 64) fail(400, 'too_large', '请求内容过大。')
  if (!/^[A-Za-z0-9_-]+$/.test(id) || ids.has(id) || typeof item.deleted !== 'boolean') fail(400, 'bad_request', '请求内容不正确。')
  ids.add(id)
  const data = item.deleted ? null : dataJson(item.data)
  if (item.deleted ? item.data !== null : object(item.data).id !== id) fail(400, 'bad_request', '请求内容不正确。')
  return { id, data, updatedAt, deleted: item.deleted }
 })
 let prefs: { data: string; updatedAt: string } | undefined
 if (input.prefs !== undefined) {
  const p = object(input.prefs)
  prefs = { data: dataJson(prefsData(p.data)), updatedAt: timestamp(p.updatedAt) }
 }
 const clock = iso()
 await stmt(env, 'DELETE FROM sync_usage WHERE user_id=? AND created_at<=?', u.id, iso(Date.now() - 60000)).run()
 const usage = await stmt(env, `INSERT INTO sync_usage SELECT ?,? WHERE
  (SELECT COUNT(*) FROM sync_usage WHERE user_id=?)<30`, u.id, clock, u.id).run()
 // TODO(design): 新增限制提示先沿用简短中文，待统一文案。
 if (!usage.meta.changes) fail(429, 'rate_limited', '同步太频繁，请稍后再试。')
 await stmt(env, 'DELETE FROM cloud_lineups WHERE user_id=? AND deleted=1 AND changed_at<?', u.id, iso(Date.now() - 90 * DAY)).run()
 // 同一毫秒的连续请求也产生不同游标，客户端时间不用于增量筛选。
 const latest = await stmt(env, `SELECT MAX(changed_at) AS time FROM (
  SELECT changed_at FROM cloud_lineups WHERE user_id=? UNION ALL SELECT changed_at FROM cloud_prefs WHERE user_id=?)`, u.id, u.id).first<{ time: string | null }>()
 const now = iso(Math.max(Date.now(), latest!.time ? Date.parse(latest!.time) + 1 : 0))
 // 先处理删除，允许满额时在同一请求内替换阵容；批次失败整体回滚。
 uploads.sort((a, b) => Number(b.deleted) - Number(a.deleted))
 const writes = uploads.map(item => stmt(env, `INSERT INTO cloud_lineups(user_id,id,data,updated_at,deleted,changed_at)
  SELECT ?,?,?,?,?,? WHERE
  (?=0 OR EXISTS(SELECT 1 FROM cloud_lineups WHERE user_id=? AND id=?))
  AND NOT EXISTS(SELECT 1 FROM cloud_lineups WHERE user_id=? AND id=? AND
   (updated_at>? OR (?=0 AND (? IS NULL OR changed_at>?) AND updated_at<>?)))
  ON CONFLICT(user_id,id) DO UPDATE SET data=excluded.data,updated_at=excluded.updated_at,deleted=excluded.deleted,changed_at=excluded.changed_at
  RETURNING id`, u.id, item.id, item.data, item.updatedAt, Number(item.deleted), now,
  Number(item.deleted), u.id, item.id, u.id, item.id, item.updatedAt, Number(item.deleted), since, since, item.updatedAt))
 if (prefs) writes.push(stmt(env, `INSERT INTO cloud_prefs VALUES(?,?,?,?)
  ON CONFLICT(user_id) DO UPDATE SET data=excluded.data,updated_at=excluded.updated_at,changed_at=excluded.changed_at
  WHERE excluded.updated_at>=cloud_prefs.updated_at RETURNING user_id AS id`, u.id, prefs.data, prefs.updatedAt, now))
 let results: D1Result<{ id: string }>[] = []
 try { if (writes.length) results = await env.DB.batch<{ id: string }>(writes) }
 catch (error) {
  if (String(error).includes('cloud_too_many')) fail(400, 'too_many', '最多保存 300 套云端阵容。')
  throw error
 }
 const accepted = new Set(uploads.filter((_, i) => results[i].results.length).map(item => item.id))
 const rows = (await stmt(env, 'SELECT * FROM cloud_lineups WHERE user_id=?', u.id).all<Row>()).results
 const cloudPrefs = await stmt(env, 'SELECT * FROM cloud_prefs WHERE user_id=?', u.id).first<{ data: string; updated_at: string; changed_at: string }>()
 const response: SyncResponse = {
  lineups: rows.filter(r => !accepted.has(r.id) && (since === null ? !r.deleted : r.changed_at > since)).map(lineup),
  conflicts: rows.filter(r => ids.has(r.id) && !accepted.has(r.id)).map(lineup),
  prefs: cloudPrefs && ((prefs && cloudPrefs.updated_at > prefs.updatedAt) || since === null || cloudPrefs.changed_at > since)
   ? { data: JSON.parse(cloudPrefs.data), updatedAt: cloudPrefs.updated_at } as CloudPrefs : null,
  now,
 }
 return json(response)
}
