// 原型用的假后端：数据取自参考项目的当期数据表，本局信息仿照 TestData/info-ingame-1920x1080.png。
// 禁用推算和规划都是粗略的假实现，只为让界面能走通；真实逻辑在参考项目的 BanScreenAnalyzer / LineupPlanner。
import type { Api, Catalog, Lineup, LineupMember, MatchInfo, PlannedLineup, PlanRequest, PlanResult, Session, Settings, UpdateStatus } from './types'
import raw from '../mock/catalog.json'

const catalog = raw as Catalog
const cov = new Map(catalog.covenants.map((c) => [c.id, c]))
const hasCov = (o: Catalog['operators'][number], cid: string) => o.core.includes(cid) || o.extra.includes(cid)

// 样图里灰掉的盟约
const incomplete = new Set(['victoria', 'kjerag', 'egir', 'kazimierz', 'ursus', 'preci', 'skillful', 'stead', 'visi', 'mira'])
const manualBan = new Set<string>()
const manualUnban = new Set<string>()

// 假的禁用推算：核心盟约阵容不完整的干员都禁掉，只有附加盟约的看附加盟约；名单长度和游戏里差不多
const autoBanned = () =>
  catalog.operators
    .filter((o) => (o.core.length ? o.core.some((c) => incomplete.has(c)) : o.extra.some((c) => incomplete.has(c))))
    .map((o) => o.name)

function computeMatch(source: MatchInfo['source'], recognizedAt: string): MatchInfo {
  const banned = new Set([...autoBanned(), ...manualBan])
  for (const n of manualUnban) banned.delete(n)
  return {
    source,
    recognizedAt,
    strategyId: 'band_amiya',
    covenants: catalog.covenants.map((c) => ({ id: c.id, incomplete: incomplete.has(c.id) })),
    bannedOperators: [...banned],
    bossId: 'enemy_1521_dslily',
    enemyTypes: ['REBORN', 'INVISIBLE', 'DOT'],
  }
}
let match: MatchInfo = computeMatch('recognized', new Date(Date.now() - 4 * 60_000).toISOString())

// 假规划：贪心挑能补目标的干员，凑不齐再用转职补
function planOne(req: PlanRequest, people: number, transforms: number, cheap: boolean): PlannedLineup | null {
  const banned = new Set(match.bannedOperators)
  const need = new Map(req.goals.map((g) => [g.id, cov.get(g.id)!.thresholds[g.level - 1]]))
  const members: LineupMember[] = []
  const take = (name: string) => {
    members.push({ name })
    const o = catalog.operators.find((x) => x.name === name)!
    for (const [cid, n] of need) if (hasCov(o, cid)) need.set(cid, n - 1)
  }
  req.locked.forEach(take)
  const pool = catalog.operators
    .filter((o) => !banned.has(o.name) && !req.locked.includes(o.name))
    .sort((a, b) => (cheap ? a.tier - b.tier : b.tier - a.tier))
  while (members.length < people) {
    let best: (typeof pool)[number] | undefined
    let bestGain = 0
    for (const o of pool) {
      if (members.some((m) => m.name === o.name)) continue
      const gain = [...need].filter(([cid, n]) => n > 0 && hasCov(o, cid)).length
      if (gain > bestGain) [best, bestGain] = [o, gain]
    }
    if (!best) break
    take(best.name)
  }
  let left = transforms
  for (const [cid, n] of need) {
    for (let i = 0; i < n && left > 0; i++) {
      const m = members.find((x) => !x.transform && !hasCov(catalog.operators.find((o) => o.name === x.name)!, cid))
      if (!m) break
      m.transform = cid
      need.set(cid, need.get(cid)! - 1)
      left--
    }
  }
  if ([...need.values()].some((n) => n > 0)) return null
  const counts = Object.fromEntries(
    req.goals.map((g) => [
      g.id,
      members.filter((m) => m.transform === g.id || hasCov(catalog.operators.find((o) => o.name === m.name)!, g.id)).length,
    ]),
  )
  return { people, transforms: transforms - left, members, freeSlots: people - members.length, counts }
}

function plan(req: PlanRequest): PlanResult {
  if (!req.goals.length) return { candidates: [], error: '先选至少一个目标盟约' }
  const out: PlannedLineup[] = []
  const seen = new Set<string>()
  for (const p of req.people)
    for (const t of req.transforms)
      for (const cheap of [false, true]) {
        const r = planOne(req, p, t, cheap)
        const key = r?.members.map((m) => m.name + (m.transform ?? '')).sort().join()
        if (r && !seen.has(key!)) seen.add(key!), out.push(r)
      }
  return out.length ? { candidates: out } : { candidates: [], error: '按当前禁用情况凑不齐这些目标，试试降低档位或允许转职' }
}

// 以某个核心盟约为主，取高阶干员凑 9 人
function build(id: string, name: string, cid: string, note?: string): Lineup {
  // 样例是「之前存的阵容」，不受本局禁用影响：取该盟约等阶最高的成员，不够再用同附加盟约的补
  const byTier = [...catalog.operators].sort((a, b) => b.tier - a.tier)
  const main = byTier.filter((o) => o.core.includes(cid))
  const extra = byTier.filter((o) => !o.core.includes(cid) && o.extra.some((e) => main.some((m) => m.extra.includes(e))))
  const members = [...main, ...extra].slice(0, 9).map((o) => ({ name: o.name }))
  return { id, name, members, source: 'user', note, updatedAt: new Date(Date.now() - 86400_000).toISOString() }
}

const lineups: Lineup[] = [
  build('l-siracusa', '叙拉古 · 家族', 'siracusa'),
  build('l-yan', '炎 · 元素', 'yan'),
  build('l-lethania', '莱塔尼亚 · 叠层', 'lethania'),
  build('l-sargon', '萨尔贡 · 流沙', 'sargon', '后期补伊比利亚'),
  build('l-victoria', '维多利亚 · 稳守', 'victoria'),
]

let session: Session = {
  targetLineupId: 'l-siracusa',
  owned: lineups[0].members.slice(1, 4).map((m) => m.name),
  tracked: [],
}

let settings: Settings = { gameTheme: 'terminal', shopMarkers: true, autoCheckUpdate: true, uiScale: 'auto' }
const updateListeners = new Set<(u: UpdateStatus) => void>()
const pushUpdate = (u: UpdateStatus) => updateListeners.forEach((cb) => cb(u))

const wait = <T>(v: T, ms = 120) => new Promise<T>((r) => setTimeout(() => r(structuredClone(v)), ms))

export const mockApi: Api = {
  catalog: () => wait(catalog, 0),
  // 地址带 ?overlay 时模拟覆盖模式，方便看收起竖条
  connection: () => wait({ state: 'connected', resolution: '1920×1080', ...(location.search.includes('overlay') && { fullscreen: true, overlay: true }) }),
  match: () => wait(match),
  rescan: () => {
    incomplete.clear()
    ;['victoria', 'kjerag', 'egir', 'kazimierz', 'ursus', 'preci', 'skillful', 'stead', 'visi', 'mira'].forEach((c) => incomplete.add(c))
    manualBan.clear()
    manualUnban.clear()
    match = computeMatch('recognized', new Date().toISOString())
    return wait(match, 900)
  },
  setCovenantIncomplete: (id, v) => {
    v ? incomplete.add(id) : incomplete.delete(id)
    match = computeMatch('manual', match.recognizedAt!)
    return wait(match, 60)
  },
  setOperatorBanned: (name, v) => {
    ;(v ? manualUnban : manualBan).delete(name)
    ;(v ? manualBan : manualUnban).add(name)
    match = computeMatch('manual', match.recognizedAt!)
    return wait(match, 60)
  },
  lineups: () => wait(lineups),
  saveLineup: (l) => {
    const saved = { ...l, id: l.id || `l-${Date.now()}`, updatedAt: new Date().toISOString() }
    const i = lineups.findIndex((x) => x.id === saved.id)
    i >= 0 ? (lineups[i] = saved) : lineups.unshift(saved)
    return wait(saved, 60)
  },
  deleteLineup: async (id) => {
    lineups.splice(lineups.findIndex((x) => x.id === id), 1)
  },
  plan: (req) => wait(plan(req), 400),
  session: () => wait(session),
  saveSession: async (s) => {
    session = structuredClone(s)
  },
  windowCommand: async () => {},
  settings: () => wait(settings, 0),
  saveSettings: async (s) => {
    settings = structuredClone(s)
  },
  appInfo: () => wait({ version: '0.1.0' }, 0),
  // 假数据：每次检查都发现 0.2.0，方便看界面
  checkUpdate: async () => {
    pushUpdate({ state: 'checking' })
    const u: UpdateStatus = { state: 'available', version: '0.2.0', notesUrl: 'https://github.com/LBEILC/StrongholdAssistant/releases' }
    await new Promise((r) => setTimeout(r, 700))
    pushUpdate(u)
    return u
  },
  applyUpdate: async () => {
    for (let i = 1; i <= 10; i++) {
      await new Promise((r) => setTimeout(r, 150))
      pushUpdate({ state: 'downloading', version: '0.2.0', progress: i / 10 })
    }
    pushUpdate({ state: 'ready', version: '0.2.0' })
  },
  openUrl: async (url) => void window.open(url, '_blank'),
  onUpdate: (cb) => {
    updateListeners.add(cb)
    return () => updateListeners.delete(cb)
  },
  onMatch: () => () => {},
  onConnection: () => () => {},
  onScan: () => () => {},
}
