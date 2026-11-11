// 界面状态：从 api 取数据，派生出各页要用的视图
import { api } from './api'
import type { AppInfo, Catalog, Connection, Covenant, Lineup, LineupMember, MatchInfo, Operator, PlannedLineup, PlanResult, ScanStatus, Session, Settings, UpdateStatus } from './api'

export type Tab = 'match' | 'lineup' | 'codex'
export type LineupView = 'target' | 'plan' | 'mine'
export type Detail =
  | { kind: 'operator'; name: string }
  | { kind: 'equipment'; name: string }
  | { kind: 'covenant'; id: string }
  | { kind: 'boss'; id: string }
  | null

const [catalog, connection, match, lineups, session, settings, appInfo] = await Promise.all([
  api.catalog(),
  api.connection(),
  api.match(),
  api.lineups(),
  api.session(),
  api.settings(),
  api.appInfo(),
])

const covenantById = new Map(catalog.covenants.map((c) => [c.id, c]))
const operatorByName = new Map(catalog.operators.map((o) => [o.name, o]))

/** 干员带上转职后的全部盟约 */
const FOLD_KEY = 'stronghold.folded'
function readFolded(): Record<string, boolean> {
  try {
    return JSON.parse(localStorage.getItem(FOLD_KEY) ?? '{}')
  } catch {
    return {}
  }
}

export const covenantsOf = (m: LineupMember) => {
  const o = operatorByName.get(m.name)!
  return m.transform ? [...o.core, ...o.extra, m.transform] : [...o.core, ...o.extra]
}

class AppState {
  catalog: Catalog = catalog
  connection = $state<Connection>(connection)
  match = $state<MatchInfo>(match)
  lineups = $state<Lineup[]>(lineups)
  session = $state<Session>(session)
  tab = $state<Tab>((location.hash.slice(1) as Tab) || 'match')
  lineupView = $state<LineupView>('target')
  detail = $state<Detail>(null)
  /** 正在编辑的阵容草稿；新建时 id 为空串 */
  editing = $state<Lineup | null>(null)
  matchEdit = $state(false)
  scanning = $state(false)
  settings = $state<Settings>(settings)
  appInfo: AppInfo = appInfo
  update = $state<UpdateStatus>({ state: 'idle' })
  settingsOpen = $state(false)
  /** 最近一次识别的结果说明 */
  scanNote = $state<ScanStatus | null>(null)
  /** 收起的分组（本局页等），记在本机，下次打开保持 */
  folded = $state<Record<string, boolean>>(readFolded())
  /** 覆盖模式下收成竖条 */
  collapsed = $state(false)

  // 阵容规划
  goals = $state<Record<string, 1 | 2>>({})
  locked = $state<string[]>([])
  people = $state<(8 | 9)[]>([8, 9])
  transforms = $state<(0 | 1 | 2)[]>([0, 1])
  planResult = $state<PlanResult | null>(null)
  planning = $state(false)

  target = $derived(this.lineups.find((l) => l.id === this.session.targetLineupId) ?? null)
  banned = $derived(new Set(this.match.bannedOperators))
  owned = $derived(new Set(this.session.owned))
  /** 商店里要标记的干员：目标阵容 + 单独追踪 */
  tracked = $derived(new Set([...(this.target?.members.map((m) => m.name) ?? []), ...this.session.tracked]))
  /** 目标阵容里还没持有的干员 */
  missing = $derived(this.target?.members.filter((m) => !this.owned.has(m.name)) ?? [])
  targetBonds = $derived(this.target ? bondProgress(this.target.members, this.owned) : [])

  constructor() {
    // 宿主识别到新的开局信息页时推送
    api.onMatch((m) => (this.match = m))
    api.onConnection((c) => {
      this.connection = c
      if (!c.overlay) this.collapsed = false // 宿主离开覆盖模式时已经恢复宽度
    })
    api.onScan((s) => {
      this.scanning = s.busy
      if (s.message) this.scanNote = s
    })
    api.onUpdate((u) => (this.update = u))
    if (this.settings.autoCheckUpdate) this.checkUpdate()
  }

  saveSettings() {
    api.saveSettings($state.snapshot(this.settings))
  }
  async checkUpdate() {
    this.update = await api.checkUpdate()
  }
  applyUpdate() {
    api.applyUpdate()
  }

  setCollapsed(v: boolean) {
    this.collapsed = v
    if (v) this.settingsOpen = false
    api.windowCommand(v ? 'collapse' : 'expand')
  }

  toggleFold(key: string) {
    this.folded[key] = !this.folded[key]
    try {
      localStorage.setItem(FOLD_KEY, JSON.stringify(this.folded))
    } catch {}
  }

  cov(id: string): Covenant {
    return covenantById.get(id)!
  }
  op(name: string): Operator {
    return operatorByName.get(name)!
  }
  matchCovenant(id: string) {
    return this.match.covenants.find((c) => c.id === id)
  }
  bannedIn(l: { members: LineupMember[] }) {
    return l.members.filter((m) => this.banned.has(m.name))
  }

  private save() {
    api.saveSession($state.snapshot(this.session))
  }
  setTarget(id: string | null) {
    this.session.targetLineupId = id
    this.save()
  }
  toggleOwned(name: string) {
    const s = this.session.owned
    this.session.owned = s.includes(name) ? s.filter((n) => n !== name) : [...s, name]
    this.save()
  }
  toggleTracked(name: string) {
    const s = this.session.tracked
    this.session.tracked = s.includes(name) ? s.filter((n) => n !== name) : [...s, name]
    this.save()
  }

  async rescan() {
    this.scanning = true
    this.scanNote = null
    try {
      this.match = await api.rescan()
      this.matchEdit = false
    } catch (e) {
      this.scanNote = { busy: false, ok: false, message: (e as Error).message }
    } finally {
      this.scanning = false
    }
  }
  async toggleCovenantIncomplete(id: string) {
    this.match = await api.setCovenantIncomplete(id, !this.matchCovenant(id)!.incomplete)
  }
  async toggleBanned(name: string) {
    this.match = await api.setOperatorBanned(name, !this.banned.has(name))
  }

  edit(l: Lineup | null) {
    this.detail = null
    this.editing = l ? structuredClone($state.snapshot(l)) : { id: '', name: '', members: [], source: 'user', updatedAt: new Date().toISOString() }
  }
  async saveEditing() {
    const saved = await api.saveLineup($state.snapshot(this.editing!))
    this.lineups = await api.lineups()
    this.editing = null
    return saved
  }
  async deleteLineup(id: string) {
    await api.deleteLineup(id)
    if (this.session.targetLineupId === id) this.setTarget(null)
    this.lineups = await api.lineups()
    this.editing = null
  }

  cycleGoal(id: string) {
    const max = this.cov(id).thresholds.length >= 2 ? 2 : 1
    const next = ((this.goals[id] ?? 0) + 1) % (max + 1)
    const { [id]: _, ...rest } = this.goals
    this.goals = next ? { ...rest, [id]: next as 1 | 2 } : rest
  }
  toggleLocked(name: string) {
    this.locked = this.locked.includes(name) ? this.locked.filter((n) => n !== name) : [...this.locked, name]
  }
  async runPlan() {
    this.planning = true
    this.planResult = await api.plan({
      goals: Object.entries(this.goals).map(([id, level]) => ({ id, level })),
      locked: $state.snapshot(this.locked),
      people: $state.snapshot(this.people),
      transforms: $state.snapshot(this.transforms),
    })
    this.planning = false
  }
  /** 规划结果存为阵容并设为目标 */
  async adopt(c: PlannedLineup, asTarget: boolean) {
    const name = Object.entries(this.goals)
      .map(([id, lv]) => `${this.cov(id).name}${lv === 2 ? 'II' : ''}`)
      .join(' · ')
    const saved = await api.saveLineup({ id: '', name, members: c.members, source: 'planned', updatedAt: new Date().toISOString() })
    this.lineups = await api.lineups()
    if (asTarget) this.setTarget(saved.id)
    return saved
  }
}

export interface BondProgress {
  covenant: Covenant
  planned: number // 阵容里有这个盟约的人数（含转职）
  owned: number // 其中已持有
  active: boolean // 已持有人数达到第一档
}

/** 阵容的盟约进度，只列阵容人数够第一档的盟约 */
export function bondProgress(members: LineupMember[], owned: Set<string>): BondProgress[] {
  const planned = new Map<string, number>()
  const have = new Map<string, number>()
  for (const m of members)
    for (const c of covenantsOf(m)) {
      planned.set(c, (planned.get(c) ?? 0) + 1)
      if (owned.has(m.name)) have.set(c, (have.get(c) ?? 0) + 1)
    }
  return [...planned]
    .map(([id, p]) => {
      const covenant = covenantById.get(id)!
      const h = have.get(id) ?? 0
      return { covenant, planned: p, owned: h, active: h >= covenant.thresholds[0] }
    })
    .filter((b) => b.planned >= b.covenant.thresholds[0])
    .sort((a, b) => (a.covenant.kind === b.covenant.kind ? b.planned - a.planned : a.covenant.kind === 'core' ? -1 : 1))
}

export const app = new AppState()
