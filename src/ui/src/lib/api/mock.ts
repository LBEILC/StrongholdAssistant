// 原型用的假后端：数据取自参考项目的当期数据表，本局信息仿照 TestData/info-ingame-1920x1080.png。
// 禁用推算是粗略的假实现，只为让界面能走通；真实逻辑在参考项目的 BanScreenAnalyzer。
import type { Api, BanGroup, BoardView, Catalog, CollectionMatch, CovenantGoal, Economy, EmulatorInstance, FieldState, PlacementPlan, Income, Lineup, MatchInfo, MatchRecord, Roster, RosterOperator, RosterScanStatus, RoundEnemies, RoundEnemy, RoundEnemyOptions, ScanStatus, Session, Settings, UpdateStatus } from './types'
import raw from '../mock/catalog.json'
import boardData from '../mock/board.json'
import intel from '../mock/enemy-intel.json'

// 目录数据重新生成前先用这份收入来源（生成后以数据为准）
const catalog = {
  incomeSources: [
    { name: '精打细算玩偶', trigger: 'equipment', kind: 'each', amount: 1, eliteAmount: 2 },
    { name: '见钱眼开玩偶', trigger: 'equipment', kind: 'once', amount: 2, eliteAmount: 4 },
    { name: '波登可', trigger: 'sell', kind: 'once', amount: 1, eliteAmount: 2 },
  ],
  ...raw,
} as unknown as Catalog
// 悬赏（任务 201）和本回合怪组（任务 200）：数据生成前用游戏数据里抽出来的一份；悬赏怪不在敌人目录里的先补上
if (!catalog.bounties) catalog.bounties = intel.bounties as Catalog['bounties']
for (const e of intel.enemies as Catalog['enemies']) if (!catalog.enemies.some((x) => x.id === e.id)) catalog.enemies.push(e)
// 敌人回合加成：数据生成前的假表（每回合生命 ×1.2、攻击 ×1.1 累计），只为界面能走通
if (!catalog.enemyBuffs) {
  const t = Array.from({ length: 15 }, (_, i) => ({ hp: +(1.2 ** (i + 1)).toFixed(3), atk: +(1.1 ** (i + 1)).toFixed(3), attackSpeed: i >= 9 ? 10 : 0, speed: 1 }))
  catalog.enemyBuffs = { mode_single_16: t, mode_multi_normal: t }
}
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

// 本回合敌人（任务 200）：读到 3 个，第一个是选了悬赏的泥岩巨像，别的被挤到下面没读到。?renemy=0 看「本回合还没读到」
const enemyRef = (name: string, source: RoundEnemy['source']): RoundEnemy => ({ id: catalog.enemies.find((e) => e.name === name)?.id ?? null, name, source })
let roundEnemies: RoundEnemies | null = new URLSearchParams(location.search).get('renemy') === '0' ? null : {
  round: 6,
  at: new Date().toISOString(),
  enemies: ['泥岩巨像', '萨卡兹枯朽战士', '萨卡兹枯朽战士组长'].map((n) => enemyRef(n, 'read')),
  removed: [],
}
function setRoundEnemy(name: string, present: boolean) {
  const round = match.economy!.round
  const r: RoundEnemies = roundEnemies?.round === round ? roundEnemies : { round, at: '', enemies: [], removed: [] }
  let enemies = r.enemies, removed = r.removed
  if (present) {
    const back = removed.find((e) => e.name === name)
    removed = removed.filter((e) => e.name !== name)
    if (!enemies.some((e) => e.name === name)) enemies = back ? [...enemies.filter((e) => e.source === 'read'), back, ...enemies.filter((e) => e.source === 'manual')] : [...enemies, enemyRef(name, 'manual')]
  } else {
    const e = enemies.find((x) => x.name === name)
    enemies = enemies.filter((x) => x.name !== name)
    if (e?.source === 'read') removed = [...removed, e]
  }
  roundEnemies = { round, at: new Date().toISOString(), enemies, removed }
  match = { ...match, roundEnemies }
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
    strategySource: 'recognized',
    roundEnemies,
    customPicks: { 5: ['凯尔希·思衡托'], 6: [] },
    banGroups: g.groups,
    solo: false,
    awaitingDetail: false,
    bansScanned: !location.search.includes('rule'), // ?rule：只做过入场自动识别
    mode: { id: 'mode_single_16', name: '规程等级16' },
    economy: economy(),
  }
}
// 回合与资金：?econ=carry|deferred|kazimier|random 换策略规则，?econ=none 没有基础资金数据
const maxListeners = new Set<(m: boolean) => void>()
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
    basis: q === 'none' ? 'none' : 'table',
    estimated: q !== 'none',
  }
}
const emptyMatch = (): MatchInfo => ({ source: 'none', strategyId: null, covenants: [], bannedOperators: [], bossId: null, enemyTypes: [], banGroups: [], solo: false, awaitingDetail: false, bansScanned: false, mode: null, economy: null, strategySource: null, roundEnemies: null, customPicks: null })
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
lineups[0].description = '前期先凑叙拉古 2 人，见到家族盟约的干员优先拿，金币够就升人口。\n过渡阶段别急着卖低费，等关键干员到位再换。\n成型后主 C 站中间，两边放阻挡，首领回合留一个减速。'
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
  gameThemeAuto: true,
  markerStyles: { key: { show: true, color: '#F3B322' }, tracked: { show: true, color: '#3CF0C2' }, pickable: { show: true, color: '#A98BFF' }, labels: true },
  uiFont: '',
  uiFontWeight: 'normal',
  uiSkin: (new URLSearchParams(location.search).get('skin') as Settings['uiSkin'] | null) ?? 'follow', // ?skin=ursus：官网截图用
  uiScheme: (new URLSearchParams(location.search).get('scheme') as Settings['uiScheme'] | null) ?? 'system', // ?scheme=light
  uiAccent: new URLSearchParams(location.search).get('accent') ?? '', // ?accent=%2322c55e
  shopMarkers: true,
  shopScanSpeed: 'normal',
  routes: { enabled: false, display: 'always', peekSeconds: 6, hotkey: 'Alt+R', overlap: 'cycle', cycleSeconds: 2.2, fly: 'main', side: 'auto' },
  overlayLayers: { placement: true, pending: true, upgrade: true },
  placementGoal: 'balanced',
  fieldAuto: false,
  fieldHotkey: 'Alt+F',
  collect: location.search.includes('collect'),
  // 官网上的侧栏演示（/demo/?demo）不弹「发现新版本」
  autoCheckUpdate: !location.search.includes('demo'),
  updateSource: 'github',
  mirrorChyanCdk: '',
  githubToken: '',
  githubTokenExpiresAt: '',
  updateProxy: '',
  autoDownloadUpdate: true,
  uiScale: 'auto',
  alwaysOnTop: false,
  followGame: true,
  dockSide: 'auto',
  overlaySide: 'left',
  collapseMode: 'strip',
  gameSource: 'auto',
  emulatorSerial: '',
  adbAddress: '',
  focusCovenants: ['sargon', 'visi'],
  focusOperators: autoBanned().slice(0, 2),
  toggleHotkey: 'Alt+S',
  refreshKey: 'D',
  runAsAdmin: true,
  startupMode: 'remember',
  shareIncome: false,
  cloudSync: { enabled: false, since: null, pushedAt: null, prefsAt: null, deleted: [] },
  plazaNotices: true,
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
let collections: CollectionMatch[] = [
  { id: 'c2', startedAt: '2026-11-16T20:31:00+08:00', ongoing: true, mode: '终极模拟', rounds: 9, frames: 31, answers: 4, bytes: 11_800_000 },
  { id: 'c1', startedAt: '2026-11-16T19:42:00+08:00', ongoing: false, mode: '终极模拟', rounds: 25, frames: 84, answers: 37, bytes: 27_300_000 },
]
const pushUpdate = (u: UpdateStatus) => updateListeners.forEach((cb) => cb(u))
let mockUpdateReady = false
let mockDownloading: Promise<void> | null = null
const mockDownload = () =>
  (mockDownloading ??= (async () => {
    for (let i = 1; i <= 10; i++) {
      await new Promise((r) => setTimeout(r, 150))
      pushUpdate({ state: 'downloading', version: '0.4.3', progress: i / 10 })
    }
    mockUpdateReady = true
    pushUpdate({ state: 'ready', version: '0.4.3' })
  })())

const scanListeners = new Set<(s: ScanStatus) => void>()
const windowListeners = new Set<(e: 'closing') => void>()
const matchListeners = new Set<(m: MatchInfo) => void>()
const wait = <T>(v: T, ms = 120) => new Promise<T>((r) => setTimeout(() => r(structuredClone(v)), ms))

// 假数据：仿录屏第 6 回合休整期。待确认（云迹 / 承曦格雷伊 / 真理）、自动填（风絮）、认不出（一个 I 阶）各一格，其余是确定的
const fieldListeners = new Set<(f: FieldState) => void>()
const sure = (zone: 'bench' | 'board', col: number, row: number, tier: number, operator: string, source: FieldState['units'][number]['source'] = 'drag') =>
  ({ zone, col, row, tier, operator, source, status: 'sure' as const, candidates: [] })
let field: FieldState = {
  mapId: 'm01',
  round: 6,
  updatedAt: '2026-11-14T10:06:57Z',
  units: [
    { zone: 'bench', col: 3, row: 0, tier: 1, operator: null, source: null, status: 'unknown', candidates: [] },
    { zone: 'bench', col: 6, row: 0, tier: 3, operator: '风絮', source: 'sprite', status: 'auto', candidates: [{ operator: '风絮', score: 0.81 }, { operator: '极光', score: 0.52 }, { operator: '耶拉', score: 0.4 }] },
    { zone: 'bench', col: 9, row: 0, tier: 2, operator: '云迹', source: 'sprite', status: 'pending', candidates: [{ operator: '云迹', score: 0.62 }, { operator: '承曦格雷伊', score: 0.6 }, { operator: '真理', score: 0.51 }] },
    sure('board', 1, 0, 2, '莎草', 'purchase'),
    sure('board', 2, 3, 2, '承曦格雷伊'),
    sure('board', 4, 2, 1, '灰喉'),
    sure('board', 5, 3, 1, '慕斯'),
    sure('board', 5, 1, 1, '拜松', 'merge'),
    sure('board', 3, 0, 3, '极光', 'carry'),
  ],
  summons: [{ zone: 'board', col: 4, row: 3, name: '鸟笼', owner: null }],
  roster: [
    { operator: '灰喉', source: 'purchase', at: '2026-11-14T10:00:45Z', sold: false },
    { operator: '慕斯', source: 'purchase', at: '2026-11-14T10:00:47Z', sold: false },
    { operator: '云迹', source: 'purchase', at: '2026-11-14T10:00:50Z', sold: false },
    { operator: '承曦格雷伊', source: 'strategy', at: '2026-11-14T10:00:51Z', sold: false },
    { operator: '真理', source: 'equipment', at: '2026-11-14T10:00:52Z', sold: false },
    { operator: '莎草', source: 'purchase', at: '2026-11-14T10:02:10Z', sold: false },
    { operator: '拜松', source: 'purchase', at: '2026-11-14T10:03:02Z', sold: false },
    { operator: '夏栎', source: 'purchase', at: '2026-11-14T10:03:30Z', sold: true },
    { operator: '极光', source: 'purchase', at: '2026-11-14T10:04:40Z', sold: false },
    { operator: '风絮', source: 'custom', at: '2026-11-14T10:05:12Z', sold: false },
    { operator: '耶拉', source: 'purchase', at: '2026-11-14T10:05:50Z', sold: false },
  ],
  log: [
    { at: '2026-11-14T10:06:57Z', kind: 'gain', operator: null, from: null, to: { zone: 'bench', col: 9, row: 0 } },
    { at: '2026-11-14T10:06:40Z', kind: 'move', operator: '慕斯', from: { zone: 'bench', col: 9, row: 0 }, to: { zone: 'board', col: 5, row: 3 } },
    { at: '2026-11-14T10:00:49Z', kind: 'move', operator: '灰喉', from: { zone: 'bench', col: 10, row: 0 }, to: { zone: 'board', col: 4, row: 2 } },
    { at: '2026-11-14T10:00:47Z', kind: 'buy', operator: '慕斯', from: null, to: { zone: 'bench', col: 9, row: 0 } },
    { at: '2026-11-14T10:00:45Z', kind: 'buy', operator: '灰喉', from: null, to: { zone: 'bench', col: 10, row: 0 } },
  ],
}

// ?bonds：仿险境录屏第 10 回合（out/159/stills/1222.png 顶栏）的中期场面，看本局页「盟约状态」。
// 顶栏数值照录屏；奥术是上回合读到、这回合被截断没看见的
if (location.search.includes('bonds')) {
  const at = '2026-11-15T10:20:22Z'
  const u = (zone: 'bench' | 'board', col: number, row: number, operator: string | null, status: FieldState['units'][number]['status'] = 'sure'): FieldState['units'][number] => ({
    zone, col, row, tier: operator ? catalog.operators.find((o) => o.name === operator)!.tier : 3, operator, source: operator ? (status === 'sure' ? 'drag' : 'sprite') : null, status,
    candidates: status === 'pending' ? [{ operator: operator!, score: 0.7 }, { operator: '妮芙', score: 0.64 }, { operator: '逻各斯', score: 0.6 }] : status === 'auto' ? [{ operator: operator!, score: 0.9 }] : [],
  })
  field = {
    ...field,
    round: 10,
    updatedAt: at,
    units: [
      u('bench', 6, 0, '霜叶'), u('bench', 8, 0, '寒檀'), u('bench', 9, 0, null),
      u('board', 3, 2, '星极'), u('board', 4, 2, '云迹', 'auto'), u('board', 5, 2, '承曦格雷伊'), u('board', 6, 2, '空弦'),
      u('board', 3, 3, '霍尔海雅'), u('board', 4, 3, '遥', 'pending'), u('board', 5, 3, '褐果'), u('board', 6, 3, '初雪'), u('board', 7, 3, '塞雷娅'),
    ],
  }
  match.economy = match.economy && { ...match.economy, round: 10 }
  const b = (id: string, level: number | null, layers: number | null, round = 10) => ({ id, level, layers, round, at })
  match.covenantBar = [b('mira', 1, 48), b('skillful', 2, 45), b('preci', 2, 41), b('doc', 1, 18), b('visi', 1, 12), b('empty', 1, null), b('sunt', 1, null), b('arcane', 1, 6, 9)]
}

// 小棋盘（任务 169）：从真数据裁了 m01、m05 两张图的第 6 回合和铳的首领回合（board.json）。
// ?boss 看联机首领回合（两边棋盘），?solo 单人；?placement 给一份假的站位推荐（推荐还没接，平时为 null）
const boardListeners = new Set<(b: BoardView | null) => void>()
function board(): BoardView | null {
  const m = (boardData as Record<string, any>)[field.mapId ?? '']
  if (!m) return null
  const boss = location.search.includes('boss')
  const lv = m.levels[boss ? 'h07_02' : '06']
  const side = settings.routes.side === 'auto' ? (boss ? 'right' : null) : settings.routes.side
  return { mapId: field.mapId!, round: boss ? 14 : 6, bossRound: boss, solo: location.search.includes('solo'), walkable: m.walkable, cells: m.cells ?? null, bossWalkable: m.bossWalkable, routes: lv.routes, bossSpots: lv.bossSpots, side: boss ? side : null }
}
const placementListeners = new Set<(p: PlacementPlan | null) => void>()
const placement = (): PlacementPlan | null => {
  if (!location.search.includes('placement')) return null
  const at = (zone: 'bench' | 'board', col: number, row: number) => ({ zone, col, row })
  const go = (operator: string, from: [string, number, number], to: [string, number, number], facing: PlacementPlan['moves'][number]['facing'] = 'left') =>
    ({ operator, from: at(from[0] as 'board', from[1], from[2]), to: at(to[0] as 'board', to[1], to[2]), facing: to[0] === 'bench' ? null : facing })
  return {
    basedOn: field.updatedAt,
    computedAt: new Date().toISOString(),
    moves: [
      go('承曦格雷伊', ['board', 2, 3], ['board', 1, 3]),
      go('灰喉', ['board', 4, 2], ['board', 4, 2], 'up'),
      go('极光', ['board', 3, 0], ['board', 3, 1], 'down'),
      go('慕斯', ['board', 5, 3], ['board', 5, 3]),
      go('拜松', ['board', 5, 1], ['board', 5, 0]),
      go('云迹', ['bench', 9, 0], ['board', 6, 3]),
      go('莎草', ['board', 1, 0], ['bench', 2, 0]),
      go('风絮', ['bench', 6, 0], ['bench', 6, 0]),
    ],
    guessed: 1,
    skipped: [at('bench', 3, 0)],
    goal: settings.placementGoal,
    recommended: {
      enemies: 41.5,
      leaked: settings.placementGoal === 'layers' ? 1.6 : 0.6,
      streams: [
        { label: '萨卡兹大剑组长 + 源石虫', chance: 0.6, enemies: 40, leaked: 0.3 },
        { label: '宿主士兵 + 源石虫', chance: 0.4, enemies: 44, leaked: 1.1 },
      ],
      layers: [{ covenant: 'siracusa', layers: settings.placementGoal === 'layers' ? 9 : 6 }, { covenant: 'stead', layers: 3 }],
    },
    current: {
      enemies: 41.5,
      leaked: 2.4,
      streams: [
        { label: '萨卡兹大剑组长 + 源石虫', chance: 0.6, enemies: 40, leaked: 1.8 },
        { label: '宿主士兵 + 源石虫', chance: 0.4, enemies: 44, leaked: 3.3 },
      ],
      layers: [{ covenant: 'siracusa', layers: 4 }, { covenant: 'stead', layers: 2 }],
    },
    assumptions: ['潜能都按 1 算', '盟约层数按 0 算', '出怪按本回合可能的怪组平均'],
  }
}

// 我的干员（任务 163）的假数据：开始是没读过；「读取」每 0.4 秒读到 9 名，读到头结束。
// 皮肤：每人默认、精二，霜叶、幽灵鲨多一套时装（假 id，没有图时界面退回干员头像）；少数几名故意认不出皮肤或技能
for (const o of catalog.operators)
  o.skins = [
    { id: `${o.charId}#1`, name: '默认服装' },
    ...(o.tier >= 4 ? [{ id: `${o.charId}#2`, name: '精英化二' }] : []),
    ...(o.name === '霜叶' || o.name === '幽灵鲨' ? [{ id: `${o.charId}@mock#1`, name: '假时装' }] : []),
  ]
let roster: Roster = { readAt: null, operators: [], customSquad: [] }
let rosterTimer = 0
let rosterRead = 0
const rosterListeners = new Set<(r: Roster) => void>()
const rosterScanListeners = new Set<(s: RosterScanStatus) => void>()
const pushRoster = () => rosterListeners.forEach((cb) => cb(structuredClone(roster)))
const pushRosterScan = (s: RosterScanStatus) => rosterScanListeners.forEach((cb) => cb(s))
function readOne(o: Catalog['operators'][number], i: number): RosterOperator {
  const skins = o.skins!
  return {
    name: o.name,
    skin: i % 23 === 7 ? null : (skins[skins.length > 2 ? 2 : i % skins.length].id),
    skill: i % 31 === 5 ? null : o.skills[i % o.skills.length].id,
    module: o.modules.length ? o.modules[0].id : '',
    potential: (i % 6) + 1,
    readAt: new Date().toISOString(),
  }
}
function stopRoster(reason: RosterScanStatus['reason'], read: number) {
  clearInterval(rosterTimer)
  rosterTimer = 0
  roster.readAt = new Date().toISOString()
  pushRoster()
  const message = { end: `读完了，一共 ${read} 名`, user: `已停止，这次读到 ${read} 名`, left: '画面离开了物资调配页，已停止', input: '你动了鼠标，已停止', lost: '游戏窗口不见了，已停止', failed: '读取出错了' }[reason!]
  pushRosterScan({ running: false, read, reason, message })
}

export const mockApi: Api = {
  catalog: () => wait(catalog, 0),
  // 地址带 ?overlay 时模拟覆盖模式，方便看收起竖条
  // ?overlay：覆盖模式；?sideright / ?sideleft：侧栏在游戏哪一侧（不写时贴靠在右、覆盖在左）
  connection: () => {
    const q = location.search
    const overlay = q.includes('overlay')
    const side = q.includes('sideright') ? 'right' : q.includes('sideleft') ? 'left' : overlay ? 'left' : 'right'
    // 游戏默认按管理员身份算（PC 客户端实际如此）；?gameNormal：游戏不是管理员，单键都能用
    return wait({ state: 'connected', resolution: '1920×1080', side, theme: 'closure' as const, gameElevated: !q.includes('gameNormal'), ...(overlay && { fullscreen: true, overlay: true }) })
  },
  match: () => wait(match),
  field: () => wait(field),
  // 假数据：当成认了一次，刷新时间、推送场上和推荐
  recognizeField: () => {
    field = { ...field, updatedAt: new Date().toISOString() }
    fieldListeners.forEach((cb) => cb(structuredClone(field)))
    setTimeout(() => placementListeners.forEach((cb) => cb(placement())), 600)
    return wait(field, 700)
  },
  // 假数据：?collect 时采集开着、有两局；「场上都对了」不在棋盘画面时报错用 ?notRest
  confirmField: async () => {
    await new Promise((r) => setTimeout(r, 300))
    if (!settings.collect) throw new Error('先打开设置里的「帮助改进识别」')
    if (location.search.includes('notRest')) throw new Error('现在不在休整期的棋盘画面，先关掉干员详情等面板、回到棋盘再点')
    collections[0] = { ...collections[0], answers: collections[0].answers + field.units.filter((u) => u.operator).length }
  },
  collections: () => wait(structuredClone(collections)),
  exportCollection: (id) => wait(String.raw`C:\Users\你\Desktop\高效雅雅-采集-${collections.find((c) => c.id === id)!.startedAt.slice(0, 16).replace(/[-:]/g, '').replace('T', '-')}.zip`, 800),
  clearCollections: async () => {
    collections = collections.filter((c) => c.ongoing).map((c) => ({ ...c, rounds: 0, frames: 0, answers: 0, bytes: 0 }))
  },
  setFieldOperator: (cell, operator) => {
    field = {
      ...field,
      updatedAt: new Date().toISOString(),
      units: field.units.some((u) => u.zone === cell.zone && u.col === cell.col && u.row === cell.row)
        ? field.units
            .filter((u) => !(u.zone === cell.zone && u.col === cell.col && u.row === cell.row && u.source === 'manual' && !operator))
            .map((u) => (u.zone === cell.zone && u.col === cell.col && u.row === cell.row ? { ...u, operator, source: operator ? 'manual' : null, status: operator ? 'sure' : 'unknown', candidates: [] } : u))
        : operator
          ? [...field.units, { ...cell, tier: catalog.operators.find((o) => o.name === operator)!.tier, operator, source: 'manual', status: 'sure', candidates: [], elite: null, equipment: null }]
          : field.units,
    }
    fieldListeners.forEach((cb) => cb(structuredClone(field)))
    return wait(field, 0)
  },
  board: () => wait(board(), 0),
  placement: () => wait(placement(), 0),
  onBoard: (cb) => {
    boardListeners.add(cb)
    return () => boardListeners.delete(cb)
  },
  onPlacement: (cb) => {
    placementListeners.add(cb)
    return () => placementListeners.delete(cb)
  },
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
  setMatchStrategy: async (id) => {
    match = { ...match, strategyId: id, strategySource: id ? 'manual' : null }
    return wait(match, 0)
  },
  setCovenantIncomplete: (id, v) => {
    v ? incomplete.add(id) : incomplete.delete(id)
    match = computeMatch('manual', match.recognizedAt!)
    return wait(match, 60)
  },
  setRoundEnemy: (name, present) => {
    if (!match.economy) return Promise.reject(new Error('还不知道现在是第几回合，等助手读到回合数再补'))
    setRoundEnemy(name, present)
    return wait(match, 60)
  },
  roundEnemyOptions: () => wait(match.economy?.round === intel.round ? (intel as unknown as RoundEnemyOptions) : null, 120),
  resetMatch: () => {
    roundEnemies = null
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
  windowCommand: async (cmd) => {
    if (cmd === 'maximize' || cmd === 'restore') for (const cb of maxListeners) cb(cmd === 'maximize')
  },
  resizeWindow: async () => {},
  onMaximized: (cb) => {
    maxListeners.add(cb)
    return () => maxListeners.delete(cb)
  },
  // 原型里 F2 当作全局快捷键；?hotkeyBusy：改快捷键总是被占用
  exportDiagnostics: () => wait(String.raw`C:\Users\你\Desktop\高效雅雅-诊断-2026-11-12.zip`, 600),
  setHotkey: async (combo) => {
    if (location.search.includes('hotkeyBusy')) throw new Error('这个组合被别的软件占用了，换一个试试。')
    settings = { ...settings, toggleHotkey: combo }
  },
  // 原型不会真的重启：?admin 当作已经是管理员身份；?adminDeny：用户不同意系统确认
  setRunAsAdmin: async (on) => {
    await wait(null, 500)
    if (on && location.search.includes('adminDeny')) throw new Error('没有同意管理员授权，助手还是普通身份运行。')
    settings = { ...settings, runAsAdmin: on }
  },
  onHotkey: (cb) => {
    const f = (e: KeyboardEvent) => e.key === 'F2' && cb()
    addEventListener('keydown', f)
    return () => removeEventListener('keydown', f)
  },
  // 假数据：浏览器里按 Alt+F 当作快捷键
  onFieldHotkey: (cb) => {
    const f = (e: KeyboardEvent) => e.altKey && e.code === 'KeyF' && (e.preventDefault(), cb())
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
    const side = settings.routes.side
    const goal = settings.placementGoal
    settings = structuredClone(s)
    // 联机首领回合「我在哪一侧」改了，小棋盘跟着换边（宿主同样会推 board）
    if (settings.routes.side !== side) boardListeners.forEach((cb) => cb(board()))
    // 推荐目标改了，宿主重算后推新的推荐
    if (settings.placementGoal !== goal) setTimeout(() => placementListeners.forEach((cb) => cb(placement())), 400)
  },
  appInfo: () => wait({ version: '0.4.2', dataDir: String.raw`C:\Users\你\AppData\Roaming\StrongholdAssistant`, hotkeyFailed: false, workbench: location.search.includes('wb'), elevated: location.search.includes('admin') && !location.search.includes('adminDeny') }, 0),
  openDataFolder: async () => {},
  // ?mumu2：模拟多开两个 MuMu 实例；?ld2：再加两个雷电实例
  emulatorInstances: () =>
    wait<EmulatorInstance[]>([
      ...(location.search.includes('mumu2')
        ? [{ kind: 'mumu' as const, serial: '127.0.0.1:16384', name: 'MuMu安卓设备' }, { kind: 'mumu' as const, serial: '127.0.0.1:16416', name: 'MuMu安卓设备-1' }]
        : []),
      ...(location.search.includes('ld2')
        ? [{ kind: 'ldplayer' as const, serial: 'emulator-5554', name: '雷电模拟器' }, { kind: 'ldplayer' as const, serial: 'emulator-5556', name: '雷电模拟器-1' }]
        : []),
    ]),
  exportData: () => wait(String.raw`C:\Users\你\Desktop\高效雅雅-备份-2026-11-11.json`, 300),
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
    // GitHub Token 的假数据：填 bad 算无效；填别的算有效（假数据按默认 Token 到期时间算两周后）
    const token = mirror ? '' : settings.githubToken.trim()
    if (token.toLowerCase() === 'bad') {
      await new Promise((r) => setTimeout(r, 500))
      const f: UpdateStatus = { state: 'failed', message: 'GitHub Token 无效或已过期，请检查设置里填的 Token。', githubTokenProblem: true }
      pushUpdate(f)
      return f
    }
    const tokenExpiry = token ? new Date(Date.now() + 14 * 86400_000).toISOString() : undefined
    const u: UpdateStatus = {
      state: 'available',
      source: cdk ? 'mirrorchyan' : 'github',
      ...(cdk ? { cdkExpiresAt: new Date(Date.now() + 87 * 86400_000).toISOString() } : {}),
      ...(tokenExpiry ? { githubTokenExpiresAt: tokenExpiry } : {}),
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
  // 假数据：填 bad 算无效，填别的算有效、按 30 天后到期
  validateGithubToken: async (token) => {
    await new Promise((r) => setTimeout(r, 400))
    const t = token.trim()
    if (t.length === 0) return { valid: false, expiresAt: null, message: '先填 Token 再验证。' }
    if (t.toLowerCase() === 'bad') return { valid: false, expiresAt: null, message: 'Token 无效，请检查输入是否正确。' }
    return { valid: true, expiresAt: new Date(Date.now() + 30 * 86400_000).toISOString(), message: '' }
  },
  // 假数据：下载 1.5 秒；点过更新的下完就「重启」（原型里只停在 ready）
  applyUpdate: async () => {
    if (mockUpdateReady) return
    await mockDownload()
  },
  downloadUpdate: () => mockDownload(),
  openUrl: async (url) => void window.open(url, '_blank'),
  // 原型里拿不到本机字体列表，给几种 Windows 常见的
  listFonts: () => wait(['Arial', 'Bahnschrift', 'Consolas', 'Segoe UI', '等线', '仿宋', '黑体', '楷体', '宋体', '微软雅黑', '微软雅黑 Light', '新宋体', '幼圆', '华文中宋', '思源黑体', '霞鹜文楷'].sort((a, b) => a.localeCompare(b, 'zh-CN'))),
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
  // 假数据：森空岛攻略列表（v0.9.2）。按核心盟约各编几篇，每页 10 条；?sklandGuest：没登录（只有攻略大全第一页）；
  // 关键词里带 fail：模拟读取失败
  sklandGuides: async (q) => {
    await new Promise((r) => setTimeout(r, 600))
    if (q.keyword?.includes('fail')) throw new Error('森空岛攻略列表读不出来，稍后再试。')
    const guest = location.search.includes('sklandGuest')
    if (guest && (q.section !== 'all' || q.sort === 'latest')) return { items: [], pageToken: null, loginRequired: true }
    const ops = catalog.operators.filter((o) => o.charId && !o.obtain)
    const cores = catalog.covenants.filter((c) => c.kind === 'core')
    const titles = ['开荒稳过', '叠层速通', '前期兜底后期斩首', '二本轮椅', '抢播报', '无脑成型', '低费运营']
    let all = Array.from({ length: q.section === 'all' ? 34 : q.section === 'collected' ? 4 : 2 }, (_, i) => {
      const core = cores[i % cores.length]
      const mine = ops.filter((o) => o.core.includes(core.id))
      const members = [...mine.slice(i % 3, (i % 3) + 6), ...ops.filter((o) => !o.core.includes(core.id)).slice(i * 2, i * 2 + 2)].slice(0, 8)
      const guide = {
        url: `https://game.skland.com/arknights/stronghold-guide/detail?guideId=${9000 + i}`,
        title: `${core.name}${titles[i % titles.length]}`,
        author: ['海盐虾条', '东湖628', '诗生歆乃', 'Vaeklay', '十三天天'][i % 5],
        strategies: [catalog.strategies[i % catalog.strategies.length].id],
        versionId: 'V3_1',
        stages: [{ type: 'FORMATION', startRound: 0, endRound: 0, operators: members.map((o, j) => ({ id: o.charId!, elite: j < 2 })), equipment: [], covenants: [], notes: '示例：前期凑对子升本，成型后转叠层。' }],
      }
      return {
        id: String(9000 + i),
        url: guide.url,
        title: guide.title,
        author: guide.author,
        editedAt: new Date(Date.now() - i * 3.6e6).toISOString(),
        phaseName: '序列I',
        stats: { likes: 1200 - i * 31, collections: 3100 - i * 70, views: 20000 - i * 400 },
        collected: q.section === 'collected',
        guide: i === 7 ? null : guide,
        _core: core.id,
        _ops: members,
      }
    })
    if (q.section === 'all') {
      if (q.core) all = all.filter((g) => g._core === q.core)
      for (const e of q.extras ?? []) all = all.filter((g) => g._ops.filter((o) => o.extra.includes(e)).length >= 2)
      if (q.strategyId) all = all.filter((g) => g.guide?.strategies[0] === q.strategyId)
      if (q.sort === 'latest') all.reverse()
    }
    if (q.keyword?.trim()) all = all.filter((g) => g.title.includes(q.keyword!.trim()))
    const from = Number(q.pageToken ?? 0)
    const page = all.slice(from, from + 10).map(({ _core, _ops, ...g }) => g)
    const more = from + 10 < all.length && !(guest && from === 0)
    return { items: page, pageToken: more ? String(from + 10) : null, loginRequired: guest }
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
  // 假数据：等一会儿当作在弹出的窗口里登录好了
  sklandLogin: async () => {
    await new Promise((r) => setTimeout(r, 1200))
    localStorage.setItem('mockSklandAccount', '博士#1234')
    return { nickname: '博士#1234' }
  },
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
  onField: (cb) => {
    fieldListeners.add(cb)
    return () => fieldListeners.delete(cb)
  },
  roster: () => wait(roster, 0),
  // ?notRoster：模拟画面不是物资调配页
  startRosterScan: async () => {
    if (rosterTimer) return
    await new Promise((r) => setTimeout(r, 300))
    if (location.search.includes('notRoster')) throw new Error('先在游戏里打开卫戍协议的「物资调配」页，再点读取。')
    const list = catalog.operators.filter((o) => !o.obtain)
    let n = (rosterRead = 0)
    const squad = new Set<string>()
    pushRosterScan({ running: true, read: 0 })
    rosterTimer = setInterval(() => {
      for (const o of list.slice(n, n + 9)) {
        const i = list.indexOf(o)
        roster.operators = [...roster.operators.filter((x) => x.name !== o.name), readOne(o, i)]
        if (o.diy && squad.size < 6) squad.add(o.name)
      }
      n = rosterRead = Math.min(n + 9, list.length)
      roster.customSquad = n === list.length ? [...squad] : [...new Set([...roster.customSquad, ...squad])]
      pushRoster()
      if (n === list.length) stopRoster('end', n)
      else pushRosterScan({ running: true, read: n })
    }, 400)
  },
  stopRosterScan: async () => {
    if (!rosterTimer) return
    stopRoster('user', rosterRead)
  },
  setRosterOperator: (name, patch) => {
    const old = roster.operators.find((o) => o.name === name) ?? { name, skin: null, skill: null, module: null, potential: null, readAt: '' }
    const edited = [...new Set([...(old.edited ?? []), ...(Object.keys(patch) as NonNullable<RosterOperator['edited']>)])]
    const next = { ...old, ...patch, edited, readAt: new Date().toISOString() }
    roster.operators = roster.operators.some((o) => o.name === name) ? roster.operators.map((o) => (o.name === name ? next : o)) : [...roster.operators, next]
    pushRoster()
    return wait(roster, 0)
  },
  onRoster: (cb) => {
    rosterListeners.add(cb)
    return () => rosterListeners.delete(cb)
  },
  onRosterScan: (cb) => {
    rosterScanListeners.add(cb)
    return () => rosterScanListeners.delete(cb)
  },
}
