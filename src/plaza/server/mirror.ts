import type { AdminMirrorProject, AdminMirrorStats } from '../shared/api'
import type { Context } from './auth'
import { DAY, fail, iso, json } from './common'

interface Sale {
 activated_at: string
 plan: string
 buy_count: number
 amount: string
 platform: string
 blocked: boolean
}
type Usage = Record<string, Record<string, { count: number; request: number }>>
const fees: Record<string, number> = { yimapay: 0.02, afdian: 0.06, weixin: 0.01, alipay: 0.006 }

async function query<T>(path: string, key: string): Promise<T> {
 // Mirror酱 在阿里云 ESA 后面，不带 UA 的机房请求会被当成机器人拦下（返回拦截页，不是 JSON）
 const response = await fetch('https://mirrorchyan.com/api/billing/' + path, {
  headers: { Authorization: key.trim(), Accept: 'application/json', 'User-Agent': 'Mozilla/5.0 (compatible; StrongholdPlaza/1.0; +https://sa.lubeiluchen.com)' },
 })
 const body = await response.text()
 let result: { ec: number; data: T; msg?: string } | null = null
 try { result = JSON.parse(body) } catch { /* 下面统一记日志 */ }
 if (!response.ok || result?.ec !== 200) {
  console.log(`[mirror] ${path.split('?')[0]} status=${response.status} ec=${result?.ec} msg=${result?.msg ?? body.slice(0, 80).replace(/\s+/g, ' ')}`)
  throw new Error('Mirror request failed')
 }
 return result.data
}

export async function mirrorStats({ request, env }: Context) {
 const now = Date.now(), today = iso(now + 8 * 3600000).slice(0, 10)
 const month = new URL(request.url).searchParams.get('month') ?? today.slice(0, 7)
 // TODO(design): 月份参数和项目查询失败提示沿用简短中文，待统一文案。
 if (!/^\d{4}-(0[1-9]|1[0-2])$/.test(month)) fail(400, 'bad_request', '月份参数不正确。')
 const cacheKey = new URL('/api/admin/mirror', request.url)
 cacheKey.searchParams.set('month', month)
 const cache = await caches.open('admin-mirror-v2')
 const cached = await cache.match(cacheKey.toString())
 // 缓存仅供鉴权后的 Worker 使用，给浏览器的响应仍由 API 层设置 no-store。
 if (cached) return json(await cached.json())
 const monthStart = Date.parse(month + '-01T00:00:00Z')
 const nextMonth = new Date(monthStart)
 nextMonth.setUTCMonth(nextMonth.getUTCMonth() + 1)
 const days = month === today.slice(0, 7) ? Number(today.slice(8)) : (nextMonth.getTime() - monthStart) / DAY
 const dates = Array.from({ length: days }, (_, i) => iso(monthStart + i * DAY).slice(0, 10))
 const usageDates = Array.from({ length: 30 }, (_, i) => iso(Date.parse(today) - (29 - i) * DAY).slice(0, 10))
 const projects = await Promise.all(env.MIRROR_RIDS.split(',').map(s => s.trim()).filter(Boolean).map(async rid => {
  const empty: AdminMirrorProject = { rid, orders: 0, amount: 0, income: 0, refundedOrders: 0, refundedAmount: 0,
   daily: [], byPlan: [], byPlatform: [], usage: [] }
  try {
   const [sales, usage] = await Promise.all([
    query<Sale[]>(`revenue?rid=${encodeURIComponent(rid)}&date=${month.replace('-', '')}&is_ua=0`, env.MIRROR_KEY),
    query<Usage>(`stat/resources?rid=${encodeURIComponent(rid)}`, env.MIRROR_KEY),
   ])
   const project = { ...empty, daily: dates.map(date => ({ date, orders: 0, amount: 0 })) }
   const plans = new Map<string, { name: string; orders: number; amount: number }>()
   const platforms = new Map<string, { name: string; orders: number; amount: number }>()
   for (const sale of sales) {
    const amount = Number(sale.amount)
    if (sale.blocked) {
     project.refundedOrders += sale.buy_count
     project.refundedAmount += amount
     continue
    }
    project.orders += sale.buy_count
    project.amount += amount
    project.income += amount * (1 - (fees[sale.platform] ?? 0)) * 0.5
    // 上游日期本身就是北京时间，不再按 UTC 转换。
    const day = project.daily.find(d => d.date === sale.activated_at.slice(0, 10))
    if (day) { day.orders += sale.buy_count; day.amount += amount }
    for (const [groups, name] of [[plans, sale.plan], [platforms, sale.platform]] as const) {
     const group = groups.get(name) ?? { name, orders: 0, amount: 0 }
     group.orders += sale.buy_count
     group.amount += amount
     groups.set(name, group)
    }
   }
   project.byPlan = [...plans.values()].sort((a, b) => b.amount - a.amount)
   project.byPlatform = [...platforms.values()].sort((a, b) => b.amount - a.amount)
   project.usage = usageDates.map(date => ({ date, users: usage[rid]?.[date]?.request ?? 0, downloads: usage[rid]?.[date]?.count ?? 0 }))
   return project
  } catch {
   return { ...empty, error: 'Mirror酱 数据查询失败。' }
  }
 }))
 const result: AdminMirrorStats = { month, projects, at: iso(now) }
 const stored = json(result)
 stored.headers.set('Cache-Control', 'max-age=300')
 await cache.put(cacheKey.toString(), stored)
 return json(result)
}
