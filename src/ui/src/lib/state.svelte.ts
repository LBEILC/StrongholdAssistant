// 界面状态：从 api 取数据，派生出各页要用的视图
import { api } from './api'
import { shareText } from './share'
import { unseenNotes } from './changelog'
import type { AppInfo, Catalog, Connection, Covenant, CovenantGoal, Equipment, Lineup, LineupMember, MatchInfo, Operator, ScanStatus, Session, Settings, UpdateStatus } from './api'

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
const equipmentByName = new Map(catalog.equipment.map((e) => [e.name, e]))

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
  /**
   * 「添加禁用干员」面板：盟约 id = 从这个盟约组点开（只列它的干员），true = 不限盟约，false = 关闭；
   * { cov, slot } = 从某组没认出的格子点开，先列这一格的候选，选了就关
   */
  banPicker = $state<string | boolean | { cov: string; slot: number }>(false)
  scanning = $state(false)
  /** 持续识别进行中 */
  continuous = $state(false)
  /** 持续识别开始时为了不挡截屏而自动收起的，结束后自动展开 */
  private autoCollapsed = false
  settings = $state<Settings>(settings)
  appInfo: AppInfo = appInfo
  update = $state<UpdateStatus>({ state: 'idle' })
  settingsOpen = $state(false)
  /** 「导入分享码」面板 */
  importOpen = $state(false)
  /** 更新日志面板 */
  changelogOpen = $state(false)
  /** 更新后第一次打开要弹的「这一版有什么新东西」 */
  whatsNew = $state(unseenNotes(appInfo.version))
  /** 底部轻提示，几秒后自动消失；bad = 操作失败 */
  toast = $state<{ text: string; bad?: boolean } | null>(null)
  private toastTimer = 0
  /** 最近一次识别的结果说明 */
  scanNote = $state<ScanStatus | null>(null)
  /** 收起的分组（本局页等），记在本机，下次打开保持 */
  folded = $state<Record<string, boolean>>(readFolded())
  /** 收起：strip 收成竖条，up 向上收起（只留标题栏和追踪列表），false 展开 */
  collapsed = $state<false | 'strip' | 'up'>(false)
  /** 启动与关闭过渡：boot 等网页可见，in 入场，out 关闭前退场 */
  phase = $state<'boot' | 'in' | 'out'>('boot')

  target = $derived(this.lineups.find((l) => l.id === this.session.targetLineupId) ?? null)
  banned = $derived(new Set(this.match.bannedOperators))
  /** 按格位推测出来、还没确认的禁用干员 */
  inferred = $derived(new Set(this.match.banGroups.flatMap((g) => g.slots.flatMap((s) => (s.inferred && s.name ? [s.name] : [])))))
  // 追踪（2026-11-11 起）：商店里标记的就是 session.tracked / trackedEquipment 这两张表，顶栏原样列出、点一下取消。
  // 设为目标时把名单加进来，取消或换目标时把旧名单移出；干员会反复买卖，所以不记「已持有」
  tracked = $derived(new Set(this.session.tracked))
  trackedEq = $derived(new Set(this.session.trackedEquipment))
  /** 顶栏的追踪列表：目标名单里的在前（关键在最前），再是单独追踪的 */
  trackedOps = $derived.by(() => {
    const order = new Map((this.target?.members ?? []).map((m, i) => [m.name, (m.key ? 0 : 1000) + i]))
    return [...this.session.tracked].sort((a, b) => (order.get(a) ?? 1e6) - (order.get(b) ?? 1e6))
  })
  /** 目标名单里被取消追踪的个数（顶栏给「全部追踪」用） */
  untrackedInTarget = $derived(
    this.target ? this.target.members.filter((m) => !this.tracked.has(m.name)).length + this.target.equipment.filter((n) => !this.trackedEq.has(n)).length : 0,
  )
  /** 目标盟约的人数：按目标名单算（盟约在游戏里有没有生效，游戏里看得到） */
  goalProgress = $derived(this.target ? goalProgress(this.target.goals, this.target.members, this.target.members.map((m) => m.name)) : [])
  /** 目标盟约里还能抓的其他干员：不在名单里、没被禁、可招募；覆盖目标盟约越多越靠前 */
  pickable = $derived.by(() => {
    const t = this.target
    if (!t) return []
    return rankForGoals(t.goals, this.catalog.operators).filter((o) => !t.members.some((m) => m.name === o.name) && !this.banned.has(o.name))
  })
  /** 特别关注（跨局保留） */
  focusOps = $derived(new Set(this.settings.focusOperators))
  focusCovs = $derived(new Set(this.settings.focusCovenants))

  constructor() {
    // 宿主识别到新的开局信息页时推送
    api.onMatch((m) => (this.match = m))
    api.onConnection((c) => (this.connection = c))
    api.onScan((s) => {
      this.scanning = s.busy
      if (s.message) this.scanNote = s
      if (this.continuous && !s.continuous) this.endContinuous()
      this.continuous = !!s.continuous
    })
    api.onUpdate((u) => (this.update = u))
    api.onWindow(() => (this.phase = 'out'))
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

  /** 收起或展开。向上收起时窗口高度由 App 量好内容再发 collapseUp */
  setCollapsed(v: false | 'strip' | 'up') {
    this.collapsed = v
    if (!v) return void api.windowCommand('expand')
    this.setTab(this.tab) // 关掉所有弹层
    if (v === 'strip') api.windowCommand('collapse')
  }
  /** 标题栏的收起按钮：按设置里的收起方式 */
  collapse() {
    this.setCollapsed(this.settings.collapseMode)
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
  eq(name: string): Equipment {
    return equipmentByName.get(name)!
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
    api.saveSession({ ...$state.snapshot(this.session), owned: [] })
  }
  /** 换目标：旧目标的名单移出追踪，新目标的名单全部加进来；单独追踪的不动 */
  setTarget(id: string | null) {
    const old = this.target
    const next = this.lineups.find((l) => l.id === id) ?? null
    this.retrack(old ? { ops: old.members.map((m) => m.name), eqs: old.equipment } : null, next ? { ops: next.members.map((m) => m.name), eqs: next.equipment } : null)
    this.session.targetLineupId = id
    this.save()
  }
  /** 追踪表里先去掉 drop、再加上 add（去重） */
  private retrack(drop: { ops: string[]; eqs: string[] } | null, add: { ops: string[]; eqs: string[] } | null) {
    const s = this.session
    const ops = s.tracked.filter((n) => !drop?.ops.includes(n))
    const eqs = s.trackedEquipment.filter((n) => !drop?.eqs.includes(n))
    s.tracked = [...ops, ...(add?.ops ?? []).filter((n) => !ops.includes(n))]
    s.trackedEquipment = [...eqs, ...(add?.eqs ?? []).filter((n) => !eqs.includes(n))]
  }
  /** 目标名单里取消过追踪的重新全部追踪 */
  trackTarget() {
    const t = this.target!
    this.retrack(null, { ops: t.members.map((m) => m.name), eqs: t.equipment })
    this.save()
  }
  toggleTracked(name: string) {
    const s = this.session.tracked
    this.session.tracked = s.includes(name) ? s.filter((n) => n !== name) : [...s, name]
    this.save()
  }
  toggleTrackedEq(name: string) {
    const s = this.session.trackedEquipment
    this.session.trackedEquipment = s.includes(name) ? s.filter((n) => n !== name) : [...s, name]
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
        this.setCollapsed('strip')
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
  resetMatch() {
    return this.attempt(async () => {
      this.match = await api.resetMatch()
      this.matchEdit = false
      this.scanNote = null
      this.showToast('已清空本局信息')
    })
  }
  async toggleBanned(name: string) {
    this.match = await api.setOperatorBanned(name, !this.banned.has(name))
  }
  /** 明确设成禁用 / 不禁用：确认推测、说「不是他」、从候选里选 */
  async setBanned(name: string, v: boolean) {
    this.match = await api.setOperatorBanned(name, v)
  }

  /** 切换顶部标签：所有弹层（详情、编辑器、添加禁用、导入、设置）一起关掉 */
  setTab(t: Tab) {
    this.detail = null
    this.editing = null
    this.banPicker = false
    this.importOpen = false
    this.settingsOpen = false
    this.changelogOpen = false
    this.tab = t
  }

  showToast(text: string, bad = false) {
    this.toast = { text, bad }
    clearTimeout(this.toastTimer)
    this.toastTimer = window.setTimeout(() => (this.toast = null), bad ? 4500 : 2600)
  }
  /** 宿主回错（文件被占用、数据无效等）时把原因用提示条显示出来，不能让操作没反应 */
  private async attempt(f: () => Promise<void>) {
    try {
      await f()
    } catch (e) {
      this.showToast((e as Error).message, true)
    }
  }
  /** 复制分享码（连同阵容名的一整句，贴到群里也看得懂） */
  copyShare(l: Lineup) {
    return this.attempt(async () => {
      await api.copyText(shareText($state.snapshot(l), this.catalog))
      this.showToast(`已复制「${l.name}」的分享码`)
    })
  }
  /** 分享码导入成新阵容；asTarget 时顺便设为目标 */
  importLineup(l: Lineup, asTarget: boolean) {
    return this.attempt(() => this.doImport(l, asTarget))
  }
  private async doImport(l: Lineup, asTarget: boolean) {
    const saved = await api.saveLineup(l)
    this.lineups = await api.lineups()
    if (asTarget) this.setTarget(saved.id)
    this.importOpen = false
    this.lineupView = asTarget ? 'target' : 'mine'
    this.showToast(asTarget ? `已导入「${saved.name}」并设为目标` : `已导入「${saved.name}」`)
  }

  edit(l: Lineup | null) {
    this.detail = null
    this.editing = l ? structuredClone($state.snapshot(l)) : { id: '', name: '', goals: [], members: [], equipment: [], strategyId: null, source: 'user', updatedAt: new Date().toISOString() }
  }
  /** 编辑器保存；失败时抛出，编辑器自己把原因显示在底栏 */
  async saveEditing() {
    const before = this.target && this.target.id === this.editing!.id ? $state.snapshot(this.target) : null
    const saved = await api.saveLineup($state.snapshot(this.editing!))
    if (before) {
      const names = (l: Lineup) => l.members.map((m) => m.name)
      const gone = { ops: names(before).filter((n) => !names(saved).includes(n)), eqs: before.equipment.filter((n) => !saved.equipment.includes(n)) }
      const added = { ops: names(saved).filter((n) => !names(before).includes(n)), eqs: saved.equipment.filter((n) => !before.equipment.includes(n)) }
      this.retrack(gone, added)
      this.save()
    }
    this.lineups = await api.lineups()
    this.editing = null
    return saved
  }
  deleteLineup(id: string) {
    return this.attempt(async () => {
      await api.deleteLineup(id)
      if (this.session.targetLineupId === id) this.setTarget(null)
      this.lineups = await api.lineups()
      this.editing = null
    })
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
  /** 详情面板里把干员加入 / 移出目标；目标阵容存好了才改追踪 */
  toggleMember(name: string) {
    const t = $state.snapshot(this.target!)
    const has = t.members.some((m) => m.name === name)
    t.members = has ? t.members.filter((m) => m.name !== name) : [...t.members, { name }]
    return this.saveTarget(t, has ? { ops: [name], eqs: [] } : null, has ? null : { ops: [name], eqs: [] })
  }
  /** 详情面板、右键菜单里把装备加入 / 移出目标 */
  toggleTargetEq(name: string) {
    const t = $state.snapshot(this.target!)
    const has = t.equipment.includes(name)
    t.equipment = has ? t.equipment.filter((n) => n !== name) : [...t.equipment, name]
    return this.saveTarget(t, has ? { ops: [], eqs: [name] } : null, has ? null : { ops: [], eqs: [name] })
  }
  private saveTarget(t: Lineup, drop: { ops: string[]; eqs: string[] } | null, add: { ops: string[]; eqs: string[] } | null) {
    return this.attempt(async () => {
      const saved = await api.saveLineup(t)
      this.lineups = this.lineups.map((l) => (l.id === saved.id ? saved : l))
      this.retrack(drop, add)
      this.save()
    })
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
