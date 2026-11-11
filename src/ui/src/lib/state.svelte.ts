// 界面状态：从 api 取数据，派生出各页要用的视图
import { api } from './api'
import type { AppInfo, Catalog, Connection, Covenant, CovenantGoal, Lineup, LineupMember, MatchInfo, Operator, ScanStatus, Session, Settings, UpdateStatus } from './api'

export type Tab = 'match' | 'lineup' | 'codex'
export type LineupView = 'target' | 'mine'
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
  /** 持续识别进行中 */
  continuous = $state(false)
  /** 持续识别开始时为了不挡截屏而自动收起的，结束后自动展开 */
  private autoCollapsed = false
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

  target = $derived(this.lineups.find((l) => l.id === this.session.targetLineupId) ?? null)
  banned = $derived(new Set(this.match.bannedOperators))
  owned = $derived(new Set(this.session.owned))
  /** 商店里要标记的干员：目标里想抓的 + 单独追踪 */
  tracked = $derived(new Set([...(this.target?.members.map((m) => m.name) ?? []), ...this.session.tracked]))
  /** 目标里还没持有的干员，关键在前 */
  missing = $derived((this.target?.members.filter((m) => !this.owned.has(m.name)) ?? []).sort((a, b) => Number(!!b.key) - Number(!!a.key)))
  /** 目标盟约的进度，按所有已持有的干员算 */
  goalProgress = $derived(this.target ? goalProgress(this.target.goals, this.target.members, this.session.owned) : [])
  /** 目标盟约里还能抓的其他干员：不在名单里、没持有、没被禁、可招募；覆盖目标盟约越多越靠前 */
  pickable = $derived.by(() => {
    const t = this.target
    if (!t) return []
    return rankForGoals(t.goals, this.catalog.operators).filter(
      (o) => !t.members.some((m) => m.name === o.name) && !this.owned.has(o.name) && !this.banned.has(o.name),
    )
  })
  /** 特别关注（跨局保留） */
  focusOps = $derived(new Set(this.settings.focusOperators))
  focusCovs = $derived(new Set(this.settings.focusCovenants))

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
      if (this.continuous && !s.continuous) this.endContinuous()
      this.continuous = !!s.continuous
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
  /** 本局适不适合用这套阵容：目标盟约阵容不完整的个数优先，其次名单被禁人数；越小越好 */
  lineupPenalty(l: Lineup) {
    return l.goals.filter((g) => this.matchCovenant(g.id)?.incomplete).length * 100 + this.bannedIn(l).length
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
  // 持续识别：点一次开始，用户在游戏里往下滚动禁用列表，宿主每隔一会儿识别一次并合并结果
  // 覆盖模式下侧栏盖在游戏上，先收成细条，免得每次截屏都要把侧栏藏起来造成闪烁
  toggleContinuous() {
    const on = !this.continuous
    this.continuous = on
    if (on) {
      this.scanNote = null
      this.matchEdit = false
      if (this.connection.overlay && !this.collapsed) {
        this.autoCollapsed = true
        this.setCollapsed(true)
      }
    } else this.endContinuous()
    api.scanContinuous(on)
  }
  private endContinuous() {
    if (this.autoCollapsed && this.collapsed) this.setCollapsed(false)
    this.autoCollapsed = false
  }
  async toggleCovenantIncomplete(id: string) {
    this.match = await api.setCovenantIncomplete(id, !this.matchCovenant(id)!.incomplete)
  }
  async toggleBanned(name: string) {
    this.match = await api.setOperatorBanned(name, !this.banned.has(name))
  }

  edit(l: Lineup | null) {
    this.detail = null
    this.editing = l ? structuredClone($state.snapshot(l)) : { id: '', name: '', goals: [], members: [], source: 'user', updatedAt: new Date().toISOString() }
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

  /** 编辑器里切换目标盟约档位：无 → I → II → 无 */
  cycleGoal(id: string) {
    const e = this.editing!
    const max = this.cov(id).thresholds.length >= 2 ? 2 : 1
    const cur = e.goals.find((g) => g.id === id)?.level ?? 0
    const next = (cur + 1) % (max + 1)
    const rest = e.goals.filter((g) => g.id !== id)
    e.goals = next ? [...rest, { id, level: next as 1 | 2 }] : rest
  }
  /** 详情面板里把干员加入 / 移出目标 */
  async toggleMember(name: string) {
    const t = $state.snapshot(this.target!)
    t.members = t.members.some((m) => m.name === name) ? t.members.filter((m) => m.name !== name) : [...t.members, { name }]
    const saved = await api.saveLineup(t)
    this.lineups = this.lineups.map((l) => (l.id === saved.id ? saved : l))
  }
  /** 导入备份后重新读阵容、本局状态和设置 */
  async reloadData() {
    ;[this.lineups, this.session, this.settings] = await Promise.all([api.lineups(), api.session(), api.settings()])
  }
  toggleOnTop() {
    this.settings.alwaysOnTop = !this.settings.alwaysOnTop
    this.saveSettings()
  }
  toggleFocusOp(name: string) {
    const s = this.settings.focusOperators
    this.settings.focusOperators = s.includes(name) ? s.filter((n) => n !== name) : [...s, name]
    this.saveSettings()
  }
  toggleFocusCov(id: string) {
    const s = this.settings.focusCovenants
    this.settings.focusCovenants = s.includes(id) ? s.filter((c) => c !== id) : [...s, id]
    this.saveSettings()
  }
}

const hasCov = (o: Operator, id: string) => o.core.includes(id) || o.extra.includes(id)

export interface GoalProgress {
  covenant: Covenant
  level: 1 | 2
  need: number // 目标档位的人数
  have: number // 已有人数
  active: boolean // 达到第一档
  done: boolean // 达到目标档位
}

/** 目标盟约的人数进度。names 是要计数的干员（已持有的，或编辑时的名单）；名单里的转职盟约也算 */
export function goalProgress(goals: CovenantGoal[], members: LineupMember[], names: Iterable<string>): GoalProgress[] {
  const byName = new Map(members.map((m) => [m.name, m]))
  const list = [...names].map((n) => covenantsOf(byName.get(n) ?? { name: n }))
  return goals.map((g) => {
    const covenant = covenantById.get(g.id)!
    const have = list.filter((cs) => cs.includes(g.id)).length
    const need = covenant.thresholds[g.level - 1]
    return { covenant, level: g.level, need, have, active: have >= covenant.thresholds[0], done: have >= need }
  })
}

/** 按目标盟约挑干员：至少有一个目标盟约、可招募；覆盖目标越多越靠前，同样多按等阶从高到低 */
export function rankForGoals(goals: CovenantGoal[], ops: Operator[]): Operator[] {
  const hits = (o: Operator) => goals.filter((g) => hasCov(o, g.id)).length
  return ops.filter((o) => !o.obtain && hits(o)).sort((a, b) => hits(b) - hits(a) || b.tier - a.tier)
}

export const app = new AppState()
