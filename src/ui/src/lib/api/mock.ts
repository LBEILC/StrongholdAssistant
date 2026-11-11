// 原型用的假后端：数据取自参考项目的当期数据表，本局信息仿照 TestData/info-ingame-1920x1080.png。
// 禁用推算是粗略的假实现，只为让界面能走通；真实逻辑在参考项目的 BanScreenAnalyzer。
import type { Api, Catalog, CovenantGoal, Lineup, MatchInfo, ScanStatus, Session, Settings, UpdateStatus } from './types'
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

// 样例阵容：目标盟约 + 这些盟约里等阶高的干员（不限人数），前两名标为关键
function build(id: string, name: string, goals: CovenantGoal[], note?: string): Lineup {
  const members = [...catalog.operators]
    .filter((o) => goals.some((g) => hasCov(o, g.id)))
    .sort((a, b) => goals.filter((g) => hasCov(b, g.id)).length - goals.filter((g) => hasCov(a, g.id)).length || b.tier - a.tier)
    .slice(0, 12)
    .map((o, i) => ({ name: o.name, ...(i < 2 && { key: true }) }))
  return { id, name, goals, members, equipment: [], strategyId: null, source: 'user', note, updatedAt: new Date(Date.now() - 86400_000).toISOString() }
}

const lineups: Lineup[] = [
  build('l-siracusa', '叙拉古 · 家族', [{ id: 'siracusa', level: 2 }, { id: 'stead', level: 1 }]),
  build('l-yan', '炎 · 元素', [{ id: 'yan', level: 2 }]),
  build('l-lethania', '莱塔尼亚 · 叠层', [{ id: 'lethania', level: 1 }, { id: 'visi', level: 1 }]),
  build('l-sargon', '萨尔贡 · 流沙', [{ id: 'sargon', level: 1 }], '后期补伊比利亚'),
]
// 样例：目标阵容带两件装备，另外单独追踪一件
lineups[0].equipment = catalog.equipment.filter((e) => e.tier >= 4).slice(0, 2).map((e) => e.name)
lineups[0].strategyId = catalog.strategies[0].id

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
  shopMarkers: true,
  autoCheckUpdate: true,
  uiScale: 'auto',
  alwaysOnTop: false,
  focusCovenants: ['sargon', 'visi'],
  focusOperators: autoBanned().slice(0, 2),
}
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
  setCovenantIncomplete: (id, v) => {
    v ? incomplete.add(id) : incomplete.delete(id)
    match = computeMatch('manual', match.recognizedAt!)
    return wait(match, 60)
  },
  resetMatch: () => {
    incomplete.clear()
    manualBan.clear()
    manualUnban.clear()
    match = { source: 'none', strategyId: null, covenants: [], bannedOperators: [], bossId: null, enemyTypes: [] }
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
  session: () => wait(session),
  saveSession: async (s) => {
    session = structuredClone(s)
  },
  windowCommand: async () => {},
  onWindow: (cb) => {
    windowListeners.add(cb)
    return () => windowListeners.delete(cb)
  },
  settings: () => wait(settings, 0),
  saveSettings: async (s) => {
    settings = structuredClone(s)
  },
  appInfo: () => wait({ version: '0.4.1', dataDir: String.raw`C:\Users\你\AppData\Roaming\StrongholdAssistant` }, 0),
  openDataFolder: async () => {},
  exportData: () => wait(String.raw`C:\Users\你\Desktop\卫戍协议助手-备份-2026-11-11.json`, 300),
  pickBackup: () =>
    wait({ token: 'mock', summary: { appVersion: '0.2.0', exportedAt: new Date(Date.now() - 3 * 86400_000).toISOString(), lineups: 6, focusCovenants: 2, focusOperators: 4 } }, 300),
  importData: () => wait(undefined, 400),
  // 假数据：每次检查都发现 0.2.0，方便看界面
  checkUpdate: async () => {
    pushUpdate({ state: 'checking' })
    const u: UpdateStatus = {
      state: 'available',
      version: '0.4.2',
      notesUrl: 'https://github.com/LBEILC/StrongholdAssistant/releases',
      notes: {
        schema: 'sa-release-notes/1',
        version: '0.4.2',
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
      pushUpdate({ state: 'downloading', version: '0.4.2', progress: i / 10 })
    }
    pushUpdate({ state: 'ready', version: '0.4.2' })
  },
  openUrl: async (url) => void window.open(url, '_blank'),
  copyText: (text) => navigator.clipboard.writeText(text),
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
