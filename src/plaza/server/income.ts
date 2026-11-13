import modes from '../data/income-modes.json'
import type { IncomeTable } from '../shared/api'
import type { Context } from './auth'
import { fingerprint } from './catalog'
import { body, DAY, dayStart, empty, fail, hash, ipSource, iso, json, stmt, string } from './common'

const excluded = new Set(['band_cannot', 'band_lmlee', 'band_mlynar', 'band_fund', 'band_protect', 'band_treasure', 'band_ducklord', 'band_siracusa'])
export async function reportIncome({ env, request }: Context) {
 const input = await body(request), device = string(input.device), mode = string(input.mode)
 // TODO(design): 设备标识沿用导入接口的 256 字符上限；资金参数错误先用统一提示。
 if (!device.trim() || device.length > 256) fail(400, 'bad_device', '设备标识不正确。')
 if (!modes.includes(mode) || typeof input.solo !== 'boolean' ||
  !(input.strategyId === null || typeof input.strategyId === 'string') ||
  !Number.isInteger(input.fingerprint) || Number(input.fingerprint) < 0 || Number(input.fingerprint) > 65535 ||
  !Array.isArray(input.samples) || input.samples.length > 30)
  fail(400, 'bad_income', '资金上报参数不正确。')
 const samples = input.samples.map(value => {
  const s = value as { round: number; startMoney: number } | null
  if (!s || !Number.isInteger(s.round) || s.round < 1 || s.round > 30 || !Number.isInteger(s.startMoney) || s.startMoney < 0 || s.startMoney > 200)
   fail(400, 'bad_income', '资金上报参数不正确。')
  return s
 })
 const deviceHash = await hash(device), time = Date.now(), ip = await ipSource(env, request, 'income', time), now = iso(time), start = dayStart()
 // 用条件插入原子地占用每日额度；经济策略拒收也计入请求次数。
 const quota = await stmt(env, `INSERT INTO income_reports SELECT ?,?,?,? WHERE
  (SELECT COUNT(*) FROM income_reports WHERE device=? AND created_at>=?)<20 AND
  (SELECT COUNT(*) FROM income_reports WHERE ip=? AND created_at>=?)<60`,
  crypto.randomUUID(), deviceHash, ip, now, deviceHash, start, ip, start).run()
 if (!quota.meta.changes) fail(429, 'rate_limited', '今天资金上报次数已用完，请明天再试。')
 if (excluded.has(input.strategyId as string)) return empty()
 if (samples.length) await env.DB.batch(samples.map(s => stmt(env, 'INSERT INTO income_samples VALUES(?,?,?,?,?,?,?,?,?)',
  deviceHash, mode, Number(input.solo), s.round, s.startMoney, Number(input.fingerprint), input.strategyId as string | null, now, ip)))
 return empty()
}
export async function incomeTable({ env }: Context) {
 let summary = await stmt(env, 'SELECT data,updated_at FROM income_summary WHERE fingerprint=?', fingerprint).first<{ data: string; updated_at: string }>()
 if (!summary || Date.now() - Date.parse(summary.updated_at) >= DAY) {
  await stmt(env, 'DELETE FROM income_reports WHERE created_at<?', iso(Date.now() - 2 * DAY)).run()
  const rows = (await stmt(env, `SELECT mode,solo,round,MIN(start_money) AS base FROM (
   SELECT mode,solo,round,start_money FROM income_samples WHERE fingerprint=?
   GROUP BY mode,solo,round,start_money HAVING COUNT(DISTINCT device)>=3 AND COUNT(DISTINCT COALESCE(source, 'old:' || device))>=3
  ) GROUP BY mode,solo,round ORDER BY mode,solo,round`, fingerprint).all<{ mode: string; solo: number; round: number; base: number }>()).results
  summary = { data: JSON.stringify(rows.map(r => ({ ...r, solo: !!r.solo }))), updated_at: iso() }
  await stmt(env, `INSERT INTO income_summary VALUES(?,?,?) ON CONFLICT(fingerprint)
   DO UPDATE SET data=excluded.data,updated_at=excluded.updated_at`, fingerprint, summary.data, summary.updated_at).run()
 }
 const table: IncomeTable = { fingerprint, updatedAt: summary.updated_at, rows: JSON.parse(summary.data) }
 const response = json(table)
 response.headers.set('Cache-Control', 'public, max-age=3600')
 return response
}
