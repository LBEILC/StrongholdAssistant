// 界面与后端之间的接口定义（设计定义，工程在 C# 侧照着实现）。
// 原型阶段由 mock.ts 实现；接入宿主后换成 WebView2 消息桥的实现，组件不改。

export type CovenantId = string // 盟约 id，取游戏 bondId 去掉 "Ship"，如 "yan"、"siracusa"
export type Tier = 1 | 2 | 3 | 4 | 5 | 6

export interface CovenantEffect {
  condition: string // 空串表示激活后的基础效果
  text: string
}

export interface Covenant {
  id: CovenantId
  name: string
  kind: 'core' | 'extra' // 核心盟约（国家）/ 附加盟约
  thresholds: number[] // 各档激活所需人数，升序
  effects: CovenantEffect[]
}

export interface Trait {
  type: string
  text: string
}

export interface Operator {
  name: string // 名字即 id
  tier: Tier
  core: CovenantId[]
  extra: CovenantId[]
  normal: Trait[]
  elite: Trait[]
  /**
   * 不可招募的特殊干员写获得方式，如「策略「暴躁铁皮」第 1 回合赠送」。有值时不出现在商店，
   * 不参与阵容规划和本局禁用推算；图鉴里单独归到「特殊」组。普通干员没有这个字段。
   */
  obtain?: string
}

export interface Equipment {
  name: string
  tier: Tier
  categories: string[]
  normal: string
  elite: string
}

export interface Boss {
  id: string
  name: string
  abilities: string[]
}

export interface EnemyType {
  code: string
  name: string
}

export interface Strategy {
  id: string
  name: string
  initiator: string
  description: string
}

/** 当期静态数据，启动时取一次 */
export interface Catalog {
  covenants: Covenant[]
  operators: Operator[]
  equipment: Equipment[]
  bosses: Boss[]
  enemyTypes: EnemyType[]
  strategies: Strategy[]
}

/** 游戏窗口连接状态 */
export interface Connection {
  state: 'searching' | 'connected'
  resolution?: string // 游戏客户区尺寸，如 "1920×1080"
  fullscreen?: boolean // 游戏是否全屏
  /**
   * 覆盖模式：游戏全屏，或者游戏窗口旁边放不下侧栏时，侧栏盖在游戏画面左侧。
   * 只有覆盖模式下界面才显示「收起」按钮。
   */
  overlay?: boolean
}

/** 开局信息页识别的进度（自动触发和「重新识别」都会推送） */
export interface ScanStatus {
  busy: boolean
  /** 持续识别进行中为 true（两次识别之间 busy 为 false，continuous 仍为 true）；结束时推送 false */
  continuous?: boolean
  /** 结束时的结果说明：成功时是一句确认，失败时是原因和怎么办，直接显示给用户 */
  message?: string
  ok?: boolean
}

/** 本局开局信息：识别或手动录入 */
export interface MatchInfo {
  source: 'none' | 'recognized' | 'manual' // 识别后又手动改过也算 manual
  recognizedAt?: string // ISO 时间
  strategyId: string | null
  /**
   * 本局盟约。incomplete = 游戏里灰掉、「部分盟约所含干员阵容不完整」的盟约，
   * 即参考项目的 BannedCovenants；核心盟约 incomplete 时规划器不能选它。
   */
  covenants: { id: CovenantId; incomplete: boolean }[]
  /** 本局禁用干员：由 incomplete 的盟约推算，再叠加手动标记（参考项目 BanViewModel） */
  bannedOperators: string[]
  bossId: string | null
  enemyTypes: string[] // 特训敌人类型 code
}

export interface LineupMember {
  name: string
  /** 关键干员（如关键叠层）：待购里排在前面，商店标记和其他想抓的干员一样 */
  key?: boolean
  /** 旧版规划器给这名干员加的转职盟约；新建的阵容不再产生 */
  transform?: CovenantId
}

/** 目标盟约和要达到的档位，人数取 Covenant.thresholds[level - 1] */
export interface CovenantGoal {
  id: CovenantId
  level: 1 | 2
}

/**
 * 阵容（2026-11-11 起）：目标盟约 + 想抓的干员，不限人数。
 * 盟约进度按本局所有已持有的干员算，不只 members。
 */
export interface Lineup {
  id: string
  name: string
  goals: CovenantGoal[]
  members: LineupMember[]
  source: 'user' | 'planned' // planned 是旧版规划器生成的，新建的都是 user
  note?: string
  updatedAt: string // ISO 时间
}

/** 游戏内正在使用的卫戍协议主题：三套主题商店名称栏的位置不同，商店识别要按主题来 */
export type GameTheme = 'terminal' | 'ursus' | 'closure' // 卫戍协议终端 / 乌萨斯齐心团 / 「全能的」可露希尔

export interface Settings {
  gameTheme: GameTheme
  shopMarkers: boolean // 是否在游戏商店里给追踪的干员打标记
  autoCheckUpdate: boolean // 启动时检查新版本
  /**
   * 界面大小（侧栏整体缩放）。'auto' 跟随游戏画面：缩放 = 游戏客户区高度（DIP）/ 1080，限制在 1–2；
   * 数字为固定倍数，取 1、1.25、1.5、1.75、2。侧栏宽度范围也按同样倍数放大。
   */
  uiScale: 'auto' | number
  /** 特别关注（跨局保留）：熟悉的盟约和关键叠层干员。只做醒目提示，不影响商店标记 */
  focusCovenants: CovenantId[]
  focusOperators: string[]
}

export interface AppInfo {
  version: string // 如 "0.1.0"；开发构建为 "0.0.0"
}

/** 自动更新的状态（检查、下载、校验都在宿主里做） */
export interface UpdateStatus {
  state: 'idle' | 'checking' | 'latest' | 'available' | 'downloading' | 'ready' | 'failed'
  version?: string // available 及之后：新版本号
  notesUrl?: string // 新版本的发版页
  progress?: number // downloading 时 0–1
  message?: string // failed 时给用户看的原因
}

/** 本局进行中的状态，界面改动后整体回写 */
export interface Session {
  targetLineupId: string | null
  owned: string[] // 已持有的干员
  tracked: string[] // 单独追踪的干员（目标阵容内的干员自动追踪，不必重复）
}

export interface Api {
  catalog(): Promise<Catalog>
  connection(): Promise<Connection>
  match(): Promise<MatchInfo>
  /** 立即识别当前画面的开局信息页；失败时以错误返回，错误文字直接给用户看 */
  rescan(): Promise<MatchInfo>
  /**
   * 持续识别：on=true 立即识别一次，之后每次识别结束 0.5 秒再识别下一次，结果像 rescan 一样合并（往下滚动禁用列表
   * 就能逐屏补全）。识别中推送 scan（continuous: true）和 match。on=false 停止。
   * 以下情况宿主自动停止：连续 3 次没找到开局信息页、持续满 90 秒、游戏窗口丢失。
   * 停止时推送 scan { busy: false, continuous: false, ok, message }，message 说明本次结果。
   */
  scanContinuous(on: boolean): Promise<void>
  /** 手动修正：盟约是否灰掉（会重新推算禁用干员） */
  setCovenantIncomplete(id: CovenantId, incomplete: boolean): Promise<MatchInfo>
  /** 手动修正：单个干员是否禁用 */
  setOperatorBanned(name: string, banned: boolean): Promise<MatchInfo>
  lineups(): Promise<Lineup[]>
  /** 新建（id 为空串）或覆盖保存，返回保存后的阵容 */
  saveLineup(l: Lineup): Promise<Lineup>
  deleteLineup(id: string): Promise<void>
  session(): Promise<Session>
  saveSession(s: Session): Promise<void>
  settings(): Promise<Settings>
  saveSettings(s: Settings): Promise<void>
  appInfo(): Promise<AppInfo>
  /** 立即检查新版本，返回检查结果；过程中也会推送 update */
  checkUpdate(): Promise<UpdateStatus>
  /** 下载并校验新版本，完成后宿主退出并替换文件、重新启动；失败时推送 failed */
  applyUpdate(): Promise<void>
  /** 用系统默认浏览器打开外部链接（界面里不跳转） */
  openUrl(url: string): Promise<void>
  /** 后端推送：更新状态变化 */
  onUpdate(cb: (u: UpdateStatus) => void): () => void
  /** 无边框窗口的按钮 */
  /**
   * 窗口操作。collapse：覆盖模式下收成竖条（宽 48 DIP × 界面缩放，左边和上边位置不变，高度不变）；
   * expand：恢复原宽度。离开覆盖模式时宿主自动恢复原宽度，并推送 connection。
   */
  windowCommand(cmd: 'minimize' | 'close' | 'collapse' | 'expand'): Promise<void>
  /** 后端推送：识别到新的开局信息 */
  onMatch(cb: (m: MatchInfo) => void): () => void
  /** 后端推送：游戏窗口找到 / 丢失 / 尺寸或全屏变化 */
  onConnection(cb: (c: Connection) => void): () => void
  /** 后端推送：识别开始、结束 */
  onScan(cb: (s: ScanStatus) => void): () => void
}
