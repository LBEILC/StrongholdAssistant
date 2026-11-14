import type { AdminTrafficHost, AdminTrafficStats, TrafficDay, TrafficPath } from '../shared/api'
import type { Context } from './auth'
import { body, DAY, empty, fail, iso, json, stmt } from './common'

const counters = ['requests', 'bytes', 'visitors', 'cacheHits', 'cacheMisses', 'status4xx', 'status5xx'] as const
// TODO(design): 流量参数错误沿用现有简短提示，待统一文案。
const invalid = () => fail(400, 'bad_request', '请求内容不正确。')
function validRow(value: unknown, path: boolean) {
 if (!value || typeof value !== 'object' || Array.isArray(value)) return false
 const row = value as Record<string, unknown>
 if (typeof row.date !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(row.date)) return false
 const time = Date.parse(row.date + 'T00:00:00Z')
 if (!Number.isFinite(time) || iso(time).slice(0, 10) !== row.date) return false
 if (typeof row.host !== 'string' || !row.host.trim()) return false
 if (path && (typeof row.path !== 'string' || !row.path.startsWith('/'))) return false
 return (path ? ['requests', 'bytes'] : counters).every(key => typeof row[key] === 'number' && Number.isSafeInteger(row[key]) && (row[key] as number) >= 0)
}
export async function reportTraffic({ request, env }: Context) {
 if (!env.EDGE_SECRET || request.headers.get('X-Plaza-Edge') !== env.EDGE_SECRET) fail(403, 'forbidden', '无权访问。')
 const input = await body(request)
 if (!Array.isArray(input.days) || input.days.length > 200 || !input.days.every(row => validRow(row, false)) ||
     !Array.isArray(input.paths) || input.paths.length > 4000 || !input.paths.every(row => validRow(row, true))) invalid()
 const days = input.days as TrafficDay[], paths = input.paths as TrafficPath[]
 const pairs = new Set(days.map(row => JSON.stringify([row.date, row.host])))
 if (pairs.size !== days.length || paths.some(row => !pairs.has(JSON.stringify([row.date, row.host]))) ||
     new Set(paths.map(row => JSON.stringify([row.date, row.host, row.path]))).size !== paths.length) invalid()
 const changes = days.flatMap(row => [
  stmt(env, `INSERT OR REPLACE INTO traffic_days VALUES (?,?,?,?,?,?,?,?,?)`, row.date, row.host, ...counters.map(key => row[key])),
  stmt(env, 'DELETE FROM traffic_paths WHERE date=? AND host=?', row.date, row.host),
 ])
 changes.push(...paths.map(row => stmt(env, 'INSERT INTO traffic_paths VALUES (?,?,?,?,?)', row.date, row.host, row.path, row.requests, row.bytes)))
 changes.push(stmt(env, 'INSERT OR REPLACE INTO traffic_state VALUES (1,?)', iso()))
 await env.DB.batch(changes)
 return empty()
}

export async function trafficStats({ request, env }: Context) {
 const now = Date.now(), local = now + 8 * 3600000, today = iso(local).slice(0, 10)
 const month = new URL(request.url).searchParams.get('month') ?? today.slice(0, 7)
 if (!/^\d{4}-(0[1-9]|1[0-2])$/.test(month)) invalid()
 const start = Date.parse(month + '-01T00:00:00Z'), next = new Date(start)
 next.setUTCMonth(next.getUTCMonth() + 1)
 const monthDays = (next.getTime() - start) / DAY
 const current = month === today.slice(0, 7)
 const dates = Array.from({ length: current ? Number(today.slice(8)) : monthDays }, (_, i) => iso(start + i * DAY).slice(0, 10))
 const rows = (await stmt(env, 'SELECT * FROM traffic_days WHERE date>=? AND date<=? ORDER BY date', dates[0], dates.at(-1)!).all<TrafficDay>()).results
 const paths = (await stmt(env, `SELECT host,path,SUM(requests) AS requests,SUM(bytes) AS bytes FROM traffic_paths
  WHERE date>=? AND date<=? GROUP BY host,path ORDER BY bytes DESC,path`, dates[0], dates.at(-1)!).all<TrafficPath>()).results
 const hosts: AdminTrafficHost[] = [...new Set(rows.map(row => row.host))].map(host => {
  const own = rows.filter(row => row.host === host)
  const sum = (key: typeof counters[number]) => own.reduce((total, row) => total + row[key], 0)
  const hits = sum('cacheHits'), cache = hits + sum('cacheMisses')
  const byDate = new Map(own.map(row => [row.date, row]))
  return { host, requests: sum('requests'), bytes: sum('bytes'), status4xx: sum('status4xx'), status5xx: sum('status5xx'),
   cacheHitRate: cache ? hits / cache : null,
   daily: dates.map(date => { const row = byDate.get(date); return { date, requests: row?.requests ?? 0, bytes: row?.bytes ?? 0, visitors: row?.visitors ?? 0 } }),
   topPaths: paths.filter(row => row.host === host).slice(0, 10).map(({ path, requests, bytes }) => ({ path, requests, bytes })),
  }
 }).sort((a, b) => b.bytes - a.bytes)
 const bytes = hosts.reduce((total, host) => total + host.bytes, 0)
 const elapsed = (local - start) / DAY
 const state = await stmt(env, 'SELECT lastReportAt FROM traffic_state WHERE id=1').first<{ lastReportAt: string }>()
 const result: AdminTrafficStats = { month, hosts, bytes, pricePerGB: 3,
  forecastBytes: current && elapsed > 0 ? bytes / elapsed * monthDays : bytes,
  lastReportAt: state?.lastReportAt ?? null, at: iso(now) }
 return json(result)
}
