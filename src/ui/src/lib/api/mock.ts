// 原型用的假后端：数据取自参考项目的当期数据表，本局信息仿照 TestData/info-ingame-1920x1080.png。
// 禁用推算是粗略的假实现，只为让界面能走通；真实逻辑在参考项目的 BanScreenAnalyzer。
import type { Api, BanGroup, Catalog, CovenantGoal, Economy, Income, Lineup, MatchInfo, MatchRecord, ScanStatus, Session, Settings, UpdateStatus } from './types'
import raw from '../mock/catalog.json'

// 目录数据重新生成前先用这份收入来源（生成后以数据为准）
const catalog = {
  incomeSources: [
    { name: '精打细算玩偶', trigger: 'equipment', kind: 'each', amount: 1, eliteAmount: 2 },
    { name: '见钱眼开玩偶', trigger: 'equipment', kind: 'once', amount: 2, eliteAmount: 4 },
    { name: '波登可', trigger: 'sell', kind: 'once', amount: 1, eliteAmount: 2 },
  ],
  ...raw,
} as unknown as Catalog
for (const o of catalog.operators as Partial<Catalog['operators'][number]>[])
  if (!o.skills) {
    o.skills = [{ id: `${o.charId}_s1`, name: '攻击力强化·β型' }, { id: `${o.charId}_s2`, name: `${o.name}的技能` }]
    o.modules = o.diy ? [] : [{ id: `${o.charId}_m1`, name: `${o.name}的模组` }]
    o.defaultSkill = o.skills[1].id
    o.defaultModule = o.modules[0]?.id ?? null
  }

// v0.5.0 图鉴增强的假数据：合成从效果文字里解析；变形同构体、来源只填几件示例；首领血量按游戏 boss_1 的比例套
for (const e of catalog.equipment) {
  const m = e.normal.match(/若与“(.+?)”同时持有则组合为“(.+?)”/)
  if (m) e.fuse = { with: m[1], result: m[2] }
  if (e.name.includes('维式重锤') && !e.name.startsWith('终极')) e.transformBond = 'victoria'
  if (e.name.includes('汲取者')) e.transformBond = 'egir'
}
catalog.equipment.find((e) => e.name === '维式重锤')!.sources = ['凯瑟琳']
catalog.equipment.find((e) => e.name === '虹彩活血饮料')!.sources = ['斥罪', '史尔特尔']
for (const o of catalog.operators) {
  o.equipSlots = o.name === '暴躁铁皮' ? { normal: 4, elite: 6 } : { normal: 2, elite: 2 }
  if (o.name === '凯瑟琳') o.givesEquipment = ['维式重锤']
  if (o.name === '斥罪' || o.name === '史尔特尔') o.givesEquipment = ['虹彩活血饮料']
}
const MODE_HP: [string, string, 'single' | 'multi', number][] = [
  ['mode_single_funny', '极速模拟', 'single', 226944],
  ...Array.from({ length: 20 }, (_, i): [string, string, 'single', number] => [`mode_single_${i + 1}`, `规程等级${i + 1}`, 'single', [283680, 378240, 472800, 567360, 661920, 756480, 851040, 945600, 1182000, 1418400, 1654800, 1891200, 2127600, 2364000, 2626667, 2889333, 3152000, 3414667, 3677333, 3940000][i]]),
  ['mode_multi_funny', '标准模拟', 'multi', 283680],
  ['mode_multi_normal', '险境模拟', 'multi', 945600],
  ['mode_multi_hard', '绝境模拟', 'multi', 2364000],
  ['mode_multi_abyss', '终极模拟', 'multi', 3940000],
]
catalog.bosses.forEach((b, i) => {
  b.hidingCore = b.abilities.includes('隐秘核心')
  const k = 0.55 + ((i * 37) % 10) / 10
  b.hp = MODE_HP.map(([mode, name, group, hp]) => ({ mode, name, group, hp: Math.round(hp * k), ...(b.hidingCore && /single_(1[0-9]|20|[5-9])$|multi_(normal|hard|abyss)/.test(mode) && { coreHp: Math.round(hp * k * 1.7) }) }))
})
const cov = new Map(catalog.covenants.map((c) => [c.id, c]))
const hasCov = (o: Catalog['operators'][number], cid: string) => o.core.includes(cid) || o.extra.includes(cid)

// 样图里灰掉的盟约
const incomplete = new Set(['victoria', 'kjerag', 'egir', 'kazimierz', 'ursus', 'preci', 'skillful', 'stead', 'visi', 'mira'])
const manualBan = new Set<string>()
const manualUnban = new Set<string>()

// 假的禁用推算：阵容不完整的核心盟约按游戏顺序（星级降序、数据表顺序）每三名禁两名，只有附加盟约的看附加盟约
const rank = new Map(catalog.operators.map((o, i) => [o.name, i]))
const byGame = (a: Catalog['operators'][number], b: Catalog['operators'][number]) => b.tier - a.tier || rank.get(a.name)! - rank.get(b.name)!
const banList = (cid: string) => catalog.operators.filter((o) => !o.obtain && o.core.includes(cid)).sort(byGame).filter((_, i) => i % 3 !== 1)
const autoBanned = () => [
  ...new Set([...catalog.operators.filter((o) => !o.core.length && o.extra.some((c) => incomplete.has(c))), ...[...incomplete].flatMap(banList)].map((o) => o.name)),
]

// 假的格位：维多利亚第 3 格没认出（候选不按顺序缩小，有好几个）、谢拉格第 3 格只剩一个候选（推测）、
// 伊比利亚只看到前 4 格（往下还有）。用户补上、说「不是」都记在这里
const hidden = new Map([['victoria', 2], ['kjerag', 2]])
const filled = new Map<string, string>() // 盟约 → 用户补上的
const notThis = new Map<string, Set<string>>() // 盟约 → 这格说过不是的
function banGroups(): { groups: BanGroup[]; out: string[]; inferred: string[] } {
  const groups: BanGroup[] = []
  const out: string[] = [] // 被藏起来、没认出的真名
  const inferred: string[] = []
  for (const cid of ['victoria', 'kjerag', 'egir']) {
    if (!incomplete.has(cid)) continue
    const list = banList(cid)
    const h = hidden.get(cid)
    const slots = list.map((o, i) => {
      if (i !== h) return { name: o.name, tier: o.tier, candidates: [] as string[] }
      out.push(o.name)
      if (filled.has(cid)) return { name: filled.get(cid)!, tier: o.tier, candidates: [] }
      const seen = new Set(list.filter((_, j) => j !== i).map((x) => x.name))
      let cands = catalog.operators.filter((x) => !x.obtain && x.tier === o.tier && (x.core.includes(cid) || x.extra.includes(cid)) && !seen.has(x.name) && !notThis.get(cid)?.has(x.name))
      // 谢拉格：按游戏顺序夹在前后两格之间
      if (cid === 'kjerag') cands = cands.filter((x) => (i === 0 || byGame(list[i - 1], x) < 0) && (i === list.length - 1 || byGame(x, list[i + 1]) < 0))
      if (cid === 'victoria') cands = [...cands, ...catalog.operators.filter((x) => x.tier === o.tier && !x.obtain && x.core.includes('kazimierz'))].slice(0, 4)
      if (cands.length === 1) {
        inferred.push(cands[0].name)
        return { name: cands[0].name, tier: o.tier, candidates: [cands[0].name], inferred: true }
      }
      return { name: null, tier: o.tier, candidates: cands.slice(0, 6).map((x) => x.name) }
    })
    groups.push(cid === 'egir' ? { covenant: cid, slots: slots.slice(0, 4), complete: false } : { covenant: cid, slots, complete: true })
  }
  return { groups, out, inferred }
}

function computeMatch(source: MatchInfo['source'], recognizedAt: string): MatchInfo {
  const g = banGroups()
  const banned = new Set(autoBanned())
  for (const n of g.out) banned.delete(n)
  for (const n of [...g.inferred, ...filled.values(), ...manualBan]) banned.add(n)
  for (const n of manualUnban) banned.delete(n)
  return {
    source,
    recognizedAt,
    strategyId: 'band_amiya',
    covenants: catalog.covenants.map((c) => ({ id: c.id, incomplete: incomplete.has(c.id) })),
    bannedOperators: [...banned],
    bossId: 'enemy_1521_dslily',
    enemyTypes: ['REBORN', 'INVISIBLE', 'DOT'],
    banGroups: g.groups,
    solo: false,
    awaitingDetail: false,
    bansScanned: !location.search.includes('rule'), // ?rule：只做过入场自动识别
    mode: { id: 'mode_single_16', name: '规程等级16' },
    economy: economy(),
  }
}
// 回合与资金：?econ=carry|deferred|kazimier|random 换策略规则，?econ=none 没有基础资金数据
let incomes: Income[] = [{ id: 'i-1', source: '精打细算玩偶', elite: false, amount: 1, kind: 'each', round: 4 }]
function economy(): Economy {
  const q = new URLSearchParams(location.search).get('econ')
  const rule = (['carry', 'deferred', 'kazimier', 'random'] as const).find((r) => r === q) ?? null
  const known = incomes.reduce((n, i) => n + (i.kind === 'each' || i.round === 6 ? i.amount : 0), 0)
  return {
    round: 6,
    money: 4,
    startMoney: 14,
    level: 2,
    upgradeCost: 5,
    readAt: new Date().toISOString(),
    next: q === 'none' ? null : (rule === 'carry' ? 15 : 11) + known,
    extra: 1,
    incomes,
    rule,
    basis: q === 'none' ? 'none' : 'own',
  }
}
const emptyMatch = (): MatchInfo => ({ source: 'none', strategyId: null, covenants: [], bannedOperators: [], bossId: null, enemyTypes: [], banGroups: [], solo: false, awaitingDetail: false, bansScanned: false, mode: null, economy: null })
// ?solo：单人开局后、还没读到对局详情的状态
let match: MatchInfo = location.search.includes('solo')
  ? { ...emptyMatch(), solo: true, awaitingDetail: true }
  : computeMatch('recognized', new Date(Date.now() - 4 * 60_000).toISOString())

// 样例阵容：目标盟约 + 这些盟约里等阶高的干员（不限人数），前两名标为关键
function build(id: string, name: string, goals: CovenantGoal[], note?: string): Lineup {
  const members = [...catalog.operators]
    .filter((o) => goals.some((g) => hasCov(o, g.id)))
    .sort((a, b) => goals.filter((g) => hasCov(b, g.id)).length - goals.filter((g) => hasCov(a, g.id)).length || b.tier - a.tier)
    .slice(0, 12)
    .map((o, i) => ({ name: o.name, ...(i < 2 && { key: true }) }))
  return { id, name, goals, members, equipment: [], strategyId: null, source: 'user', note, updatedAt: new Date(Date.now() - 86400_000).toISOString() }
}

let lineups: Lineup[] = [
  build('l-siracusa', '叙拉古 · 家族', [{ id: 'siracusa', level: 2 }, { id: 'stead', level: 1 }]),
  build('l-yan', '炎 · 元素', [{ id: 'yan', level: 2 }]),
  build('l-lethania', '莱塔尼亚 · 叠层', [{ id: 'lethania', level: 1 }, { id: 'visi', level: 1 }]),
  build('l-sargon', '萨尔贡 · 流沙', [{ id: 'sargon', level: 1 }], '后期补伊比利亚'),
]
// 样例：目标阵容带两件装备，另外单独追踪一件
lineups[0].equipment = catalog.equipment.filter((e) => e.tier >= 4).slice(0, 2).map((e) => e.name)
lineups[0].strategyId = catalog.strategies[0].id
// 样例：第一名关键干员配了三件候选、两件优先；第二名一件候选
lineups[0].members[0].equipment = [
  { name: '维式重锤', priority: true },
  { name: '虹彩活血饮料', priority: true },
  { name: '汲取者之喙', priority: false },
]
lineups[0].members[1].equipment = [{ name: '汲取者之心', priority: false }]
// 样例：目标阵容有两个回合阶段（假数据回合 6 → 自动在 4～8 回合）
const low = catalog.operators.filter((o) => o.tier <= 2 && (hasCov(o, 'siracusa') || hasCov(o, 'stead')))
lineups[0].stages = [
  { name: '', fromRound: 1, toRound: 3, goals: [{ id: 'siracusa', level: 1 }], members: low.slice(0, 5).map((o) => ({ name: o.name })), equipment: [] },
  { name: '过渡', fromRound: 4, toRound: 8, goals: [{ id: 'siracusa', level: 1 }, { id: 'stead', level: 1 }], members: [...low.slice(3, 7), ...lineups[0].members.slice(2, 5)].map((m) => ({ name: m.name })), equipment: [] },
]

let session: Session = {
  targetLineupId: 'l-siracusa',
  owned: [],
  // 目标名单全部追踪，另外单独追踪一名干员、一件装备；取消了名单里的一名，方便看「全部追踪」
  tracked: [...lineups[0].members.slice(1).map((m) => m.name), lineups[1].members[0].name],
  trackedEquipment: [...lineups[0].equipment, catalog.equipment.find((e) => e.tier === 3)!.name],
}

// 关注的样例：一个可用、一个阵容不完整的盟约，再加两名会被禁的干员，方便看提示
let settings: Settings = {
  gameTheme: 'terminal',
  uiSkin: 'follow',
  shopMarkers: true,
  autoCheckUpdate: true,
  updateSource: 'github',
  mirrorChyanCdk: '',
  updateProxy: '',
  uiScale: 'auto',
  alwaysOnTop: false,
  followGame: true,
  dockSide: 'auto',
  overlaySide: 'left',
  collapseMode: 'strip',
  gameSource: 'auto',
  mumuSerial: '',
  focusCovenants: ['sargon', 'visi'],
  focusOperators: autoBanned().slice(0, 2),
  toggleHotkey: 'Alt+S',
  refreshKey: 'D',
  startupMode: 'remember',
  shareIncome: false,
  cloudSync: { enabled: false, since: null, pushedAt: null, prefsAt: null, deleted: [] },
  operatorDefaults: {},
}
// 历史记录假数据：一局本地和森空岛对上了，一局只有本地（还在打）
const hoursAgo = (h: number) => new Date(Date.now() - h * 3600_000).toISOString()
let history: MatchRecord[] = [0.2, 2.5].map((h, i) => {
  const m = computeMatch('recognized', hoursAgo(h))
  const rounds = Array.from({ length: i ? 8 : 6 }, (_, r) => ({ round: r + 1, startMoney: [22, 9, 10, 10, 12, 14, 14, 16][r] }))
  return {
    id: `h-${i}`,
    time: hoursAgo(h),
    local: {
      mode: m.mode,
      solo: false,
      strategyId: m.strategyId,
      covenants: m.covenants,
      bannedOperators: m.bannedOperators,
      bossId: m.bossId,
      lineup: lineups[i] ?? null,
      rounds,
      lastRound: rounds.length,
      lastSeenAt: hoursAgo(h - 0.5),
    },
    skland: i
      ? {
          id: 'rec-local-1',
          strategyId: m.strategyId,
          operators: catalog.operators.slice(0, 8).map((o) => ({ id: o.charId, elite: false })),
          equipment: [],
          success: false,
          time: hoursAgo(h - 0.6),
          startTime: hoursAgo(h),
          coinSpent: 137,
          kills: 170,
          covenants: [],
          bosses: [],
          mode: 'MULTI',
          difficulty: 'HARD',
          passRound: 8,
          versionId: 'V3_1',
        }
      : null,
  }
})
const updateListeners = new Set<(u: UpdateStatus) => void>()
const pushUpdate = (u: UpdateStatus) => updateListeners.forEach((cb) => cb(u))

const scanListeners = new Set<(s: ScanStatus) => void>()
const windowListeners = new Set<(e: 'closing') => void>()
const matchListeners = new Set<(m: MatchInfo) => void>()
let continuousTimer = 0
let continuousBefore = 0
// 假数据：持续识别每秒多识别出一名禁用干员，6 次后像宿主一样自动结束
function stopContinuous(message: string) {
  clearInterval(continuousTimer)
  continuousTimer = 0
  const added = match.bannedOperators.length - continuousBefore
  scanListeners.forEach((cb) => cb({ busy: false, continuous: false, ok: true, message: message.replace('{added}', String(added)).replace('{total}', String(match.bannedOperators.length)) }))
}

const wait = <T>(v: T, ms = 120) => new Promise<T>((r) => setTimeout(() => r(structuredClone(v)), ms))

export const mockApi: Api = {
  catalog: () => wait(catalog, 0),
  // 地址带 ?overlay 时模拟覆盖模式，方便看收起竖条
  // ?overlay：覆盖模式；?sideright / ?sideleft：侧栏在游戏哪一侧（不写时贴靠在右、覆盖在左）
  connection: () => {
    const q = location.search
    const overlay = q.includes('overlay')
    const side = q.includes('sideright') ? 'right' : q.includes('sideleft') ? 'left' : overlay ? 'left' : 'right'
    return wait({ state: 'connected', resolution: '1920×1080', side, ...(overlay && { fullscreen: true, overlay: true }) })
  },
  match: () => wait(match),
  rescan: () => {
    incomplete.clear()
    ;['victoria', 'kjerag', 'egir', 'kazimierz', 'ursus', 'preci', 'skillful', 'stead', 'visi', 'mira'].forEach((c) => incomplete.add(c))
    manualBan.clear()
    manualUnban.clear()
    filled.clear()
    notThis.clear()
    match = computeMatch('recognized', new Date().toISOString())
    return wait(match, 900)
  },
  scanContinuous: async (on) => {
    if (!on) return stopContinuous('持续识别已停止，本次新识别到 {added} 名禁用干员，共 {total} 名。')
    continuousBefore = match.bannedOperators.length
    let n = 0
    const tick = () => {
      const next = catalog.operators.find((o) => !match.bannedOperators.includes(o.name))
      if (next) manualBan.add(next.name)
      match = computeMatch('recognized', new Date().toISOString())
      matchListeners.forEach((cb) => cb(structuredClone(match)))
      scanListeners.forEach((cb) => cb({ busy: false, continuous: true }))
      if (++n >= 6) stopContinuous('连续 3 次没找到开局信息页，持续识别已结束。本次新识别到 {added} 名禁用干员，共 {total} 名。')
    }
    scanListeners.forEach((cb) => cb({ busy: true, continuous: true }))
    continuousTimer = window.setInterval(tick, 1000)
  },
  setMatchStrategy: async (id) => {
    match = { ...match, strategyId: id }
    return wait(match, 0)
  },
  setCovenantIncomplete: (id, v) => {
    v ? incomplete.add(id) : incomplete.delete(id)
    match = computeMatch('manual', match.recognizedAt!)
    return wait(match, 60)
  },
  resetMatch: () => {
    incomplete.clear()
    manualBan.clear()
    manualUnban.clear()
    filled.clear()
    notThis.clear()
    match = emptyMatch()
    return wait(match, 60)
  },
  setOperatorBanned: (name, v) => {
    // 和宿主一样维护格位：补上就填进候选里有他的空格；取消就让他所在的格子变回没认出，并记下「不是他」
    for (const g of match.banGroups) {
      const i = g.slots.findIndex((s) => (v ? s.name === null || s.inferred : s.name === name) && (!v || s.candidates.includes(name)))
      if (i < 0) continue
      if (v) filled.set(g.covenant, name)
      else {
        hidden.set(g.covenant, i)
        filled.delete(g.covenant)
        notThis.set(g.covenant, (notThis.get(g.covenant) ?? new Set()).add(name))
      }
    }
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
  putLineups: async (put, removed) => {
    for (const l of put) {
      const i = lineups.findIndex((x) => x.id === l.id)
      i >= 0 ? (lineups[i] = structuredClone(l)) : lineups.unshift(structuredClone(l))
    }
    lineups = lineups.filter((x) => !removed.includes(x.id))
    return wait(structuredClone(lineups), 60)
  },
  session: () => wait(session),
  saveSession: async (s) => {
    session = structuredClone(s)
  },
  windowCommand: async () => {},
  // 原型里 F2 当作全局快捷键；?hotkeyBusy：改快捷键总是被占用
  exportDiagnostics: () => wait(String.raw`C:\Users\你\Desktop\卫戍协议助手-诊断-2026-11-12.zip`, 600),
  setHotkey: async (combo) => {
    if (location.search.includes('hotkeyBusy')) throw new Error('这个组合被别的软件占用了，换一个试试。')
    settings = { ...settings, toggleHotkey: combo }
  },
  onHotkey: (cb) => {
    const f = (e: KeyboardEvent) => e.key === 'F2' && cb()
    addEventListener('keydown', f)
    return () => removeEventListener('keydown', f)
  },
  addIncome: async (source, elite) => {
    const s = catalog.incomeSources.find((x) => x.name === source)!
    incomes = [...incomes, { id: `i-${Date.now()}`, source, elite, amount: elite ? s.eliteAmount : s.amount, kind: s.kind, round: 6 }]
    match = { ...match, economy: economy() }
    return wait(match, 0)
  },
  removeIncome: async (id) => {
    incomes = incomes.filter((i) => i.id !== id)
    match = { ...match, economy: economy() }
    return wait(match, 0)
  },
  history: () => wait(history, 300),
  deleteRecord: async (id) => {
    history = history.filter((r) => r.id !== id)
  },
  saveSklandRecords: async (records) => {
    const have = new Set(history.flatMap((r) => (r.skland ? [r.skland.id] : [])))
    history = [...history, ...records.filter((r) => !have.has(r.id)).map((r) => ({ id: `h-${r.id}`, time: r.time, local: null, skland: r }))]
    history.sort((a, b) => b.time.localeCompare(a.time))
    return history
  },
  resetShareId: async () => {},
  collapseUp: async () => {},
  // ?deeplink=…：原型里模拟从网站点「在助手里打开」
  onDeepLink: (cb) => {
    const u = new URLSearchParams(location.search).get('deeplink')
    if (u) setTimeout(() => cb(u), 800)
    return () => {}
  },
  onWindow: (cb) => {
    windowListeners.add(cb)
    return () => windowListeners.delete(cb)
  },
  settings: () => wait(settings, 0),
  saveSettings: async (s) => {
    settings = structuredClone(s)
  },
  appInfo: () => wait({ version: '0.4.2', dataDir: String.raw`C:\Users\你\AppData\Roaming\StrongholdAssistant`, hotkeyFailed: false, workbench: location.search.includes('wb') }, 0),
  openDataFolder: async () => {},
  // ?mumu2：模拟多开两个实例
  mumuInstances: () =>
    wait(location.search.includes('mumu2') ? [{ serial: '127.0.0.1:16384', name: 'MuMu安卓设备' }, { serial: '127.0.0.1:16416', name: 'MuMu安卓设备-1' }] : []),
  exportData: () => wait(String.raw`C:\Users\你\Desktop\卫戍协议助手-备份-2026-11-11.json`, 300),
  pickBackup: () =>
    wait({ token: 'mock', summary: { appVersion: '0.2.0', exportedAt: new Date(Date.now() - 3 * 86400_000).toISOString(), lineups: 6, focusCovenants: 2, focusOperators: 4 } }, 300),
  importData: () => wait(undefined, 400),
  // 假数据：每次检查都发现 0.4.3，方便看界面。更新源选 Mirror酱 时：CDK 填 bad 算 CDK 错误，填别的算有效
  checkUpdate: async () => {
    pushUpdate({ state: 'checking' })
    const mirror = settings.updateSource === 'mirrorchyan'
    const cdk = mirror ? settings.mirrorChyanCdk.trim() : ''
    if (cdk.toLowerCase() === 'bad') {
      await new Promise((r) => setTimeout(r, 500))
      const f: UpdateStatus = { state: 'failed', message: 'Mirror酱 CDK 不对，请检查有没有填错。', cdkProblem: true }
      pushUpdate(f)
      return f
    }
    const u: UpdateStatus = {
      state: 'available',
      source: cdk ? 'mirrorchyan' : 'github',
      ...(cdk ? { cdkExpiresAt: new Date(Date.now() + 87 * 86400_000).toISOString() } : {}),
      version: '0.4.3',
      notesUrl: 'https://github.com/LBEILC/StrongholdAssistant/releases',
      notes: {
        schema: 'sa-release-notes/1',
        version: '0.4.3',
        date: '2026-11-20',
        items: [
          { kind: 'improved', title: '假数据：更新内容示例', detail: '这是假数据，用来看「发现新版本」时的更新内容怎么显示。', highlight: true },
          { kind: 'fixed', title: '假数据：修了一个问题', detail: '发现新版本时，宿主会把新版本 Release 附件里的说明一起带过来。' },
        ],
      },
    }
    await new Promise((r) => setTimeout(r, 700))
    pushUpdate(u)
    return u
  },
  applyUpdate: async () => {
    for (let i = 1; i <= 10; i++) {
      await new Promise((r) => setTimeout(r, 150))
      pushUpdate({ state: 'downloading', version: '0.4.3', progress: i / 10 })
    }
    pushUpdate({ state: 'ready', version: '0.4.3' })
  },
  openUrl: async (url) => void window.open(url, '_blank'),
  copyText: (text) => navigator.clipboard.writeText(text),
  // 原型里没有宿主窗口：登录用 prompt 填一个真实凭据（从网站 Cookie 里拿）方便联调，取消就是没登录
  plazaLogin: async () => {
    const t = prompt('原型：粘贴广场登录凭据（sa_session Cookie 的值）')
    if (t) localStorage.setItem('mockPlazaToken', t)
    return t || null
  },
  plazaToken: async () => localStorage.getItem('mockPlazaToken'),
  plazaLogout: async () => localStorage.removeItem('mockPlazaToken'),
  // 假数据：一篇森空岛攻略（编号取自真实页面，文字是示例），1 秒后返回；链接里带 fail 时模拟读取失败，带 multi 时给两个阶段
  sklandGuide: async (url) => {
    await new Promise((r) => setTimeout(r, 1000))
    if (url.includes('fail')) throw new Error('20 秒内没读到攻略内容，可能是森空岛页面改版了，或者这篇攻略已被删除。')
    // 链接里带 broken：模拟带技术细节的错误（第二行起是细节，见 bridge.ts）
    if (url.includes('broken')) throw new Error('森空岛攻略的数据结构已变化，暂时无法读取。\nInvalidOperationException: tabs[4].activeBonds 是 null')
    const op = (id: string, elite = false) => ({ id, elite })
    const formation = {
      type: 'FORMATION',
      startRound: 0,
      endRound: 0,
      operators: ['char_171_bldsk', 'char_206_gnosis', 'char_4027_heyak', 'char_1016_agoat2', 'char_1045_svash2', 'char_1046_sbell2', 'char_391_rosmon', 'char_4244_tocka', 'char_9999_unknown'].map((id, i) => op(id, i === 4 || i === 5)),
      equipment: ['chess_item_6_03_e_a', 'chess_item_3_02_e_a'],
      covenants: ['kjerag', 'preci', 'swift', 'skillful', 'sunt', 'doc', 'arcane'],
      notes: '示例运营思路：前期凑对子升本，迅捷成型后转精准。\n可以把同盟约的其他叠层干员换进来。',
    }
    const early = { ...formation, type: 'TRANSITION', startRound: 1, endRound: 8, operators: formation.operators.slice(0, 4), equipment: [], notes: '示例：前期过渡。' }
    return {
      url,
      title: '示例攻略：谢拉格精准速攻',
      author: '示例作者',
      strategies: ['band_bonus', 'band_support'],
      versionId: 'V3_1',
      stages: url.includes('multi') ? [early, formation] : [formation],
    }
  },
  // 假数据：森空岛历史战绩，1.5 秒后返回三局
  sklandHistory: async () => {
    await new Promise((r) => setTimeout(r, 1500))
    localStorage.setItem('mockSklandAccount', '博士#1234')
    const ids = ['char_1027_greyy2', 'char_171_bldsk', 'char_206_gnosis', 'char_4027_heyak', 'char_391_rosmon', 'char_4244_tocka', 'char_1045_svash2', 'char_1046_sbell2']
    return [8, 0, 1].map((round, i) => ({
      id: `rec-${i}`,
      strategyId: ['band_mech', 'band_bonus', null][i],
      operators: ids.slice(0, [8, 3, 5][i]).map((id) => ({ id, elite: false })),
      equipment: i ? [] : ['chess_item_1_06_e_a', 'chess_item_6_03_e_a'],
      success: i === 2,
      time: new Date(Date.now() - [2, 4, 11][i] * 3600_000).toISOString(),
      mode: i === 1 ? 'SINGLE' : 'MULTI',
      startTime: new Date(Date.now() - [2.6, 4.2, 11.7][i] * 3600_000).toISOString(),
      coinSpent: [95, 4, 232][i],
      kills: [92, 0, 245][i],
      covenants: [{ id: 'skillful', layer: 32 }, { id: 'doc', layer: 16 }],
      bosses: [{ id: 'enemy_1521_dslily', killed: i === 2 }],
      difficulty: 'ABYSS',
      passRound: round,
      versionId: 'V3_1',
    }))
  },
  // 假数据：读过一次历史战绩就算登录了
  sklandAccount: async () => {
    const nickname = localStorage.getItem('mockSklandAccount')
    return nickname ? { nickname } : null
  },
  sklandLogout: async () => localStorage.removeItem('mockSklandAccount'),
  onUpdate: (cb) => {
    updateListeners.add(cb)
    return () => updateListeners.delete(cb)
  },
  onMatch: (cb) => {
    matchListeners.add(cb)
    return () => matchListeners.delete(cb)
  },
  onConnection: () => () => {},
  onScan: (cb) => {
    scanListeners.add(cb)
    return () => scanListeners.delete(cb)
  },
}
