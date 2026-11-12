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
  /** 装备栏数（v0.5.0）：普通干员 2 / 2，暴躁铁皮 4 / 6（数据表 charChessDataDict.equipSlotNum） */
  equipSlots: { normal: number; elite: number }
  /** 这名干员会提供的装备（v0.5.0，整备能力「获得一件××」，含获得时和进入休整期时）：给的是一个系列时写系列里的基础件名。没有就不写 */
  givesEquipment?: string[]
}

export interface Equipment {
  name: string
  tier: Tier
  categories: string[]
  normal: string
  /** 进阶效果；空串 = 这件装备没有进阶版（界面写「没有进阶效果」） */
  elite: string
  /** 合成（v0.5.0）：和 with 同时持有时组合成 result（都是装备名）。不能合成的不写 */
  fuse?: { with: string; result: string }
  /** 装在变形同构体上时提供的盟约（v0.5.0）。不提供的不写 */
  transformBond?: CovenantId
  /** 来源（v0.5.0）：获得时会给这件装备的干员（Operator.givesEquipment 的反查，含给同系列装备的）。没有就不写 */
  sources?: string[]
}

export interface Boss {
  id: string
  name: string
  abilities: string[]
  /** 有隐秘核心（v0.5.0） */
  hidingCore: boolean
  /** 各模式的血量（v0.5.0），按游戏里模式的顺序：极速模拟、规程等级 1–20、标准 / 险境 / 绝境 / 终极模拟 */
  hp: BossHp[]
}

/** 首领在一个模式下的血量 */
export interface BossHp {
  mode: string // 游戏模式 id，如 "mode_single_16"、"mode_multi_normal"
  name: string // 模式名，如「规程等级16」「险境模拟」
  group: 'single' | 'multi'
  hp: number
  /** 隐秘核心的血量（有隐秘核心、且数据里有这个模式时） */
  coreHp?: number
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
  /** 连上的是哪种游戏（v0.5.0）：PC 客户端，或 MuMu 模拟器里的安卓版。connected 时才有 */
  source?: 'pc' | 'mumu'
  /**
   * 找到游戏但用不了时给用户看的原因和办法（v0.5.0），state 为 searching 时才有，例如
   * 「MuMu 画面不是横屏 16:9，请在模拟器设置里改成 1920×1080 之类的分辨率」「找到 2 个 MuMu 实例，请在设置里选一个」
   */
  problem?: string
}

/** 正在运行的一个 MuMu 实例（v0.5.0） */
export interface MumuInstance {
  /** ADB 地址，如 "127.0.0.1:16384"，存进 Settings.mumuSerial */
  serial: string
  /** 给用户看的名字：MuMu 窗口标题或实例名 */
  name: string
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
  /**
   * 游戏「本局禁用干员」列表按盟约分组的格子（v0.4.2）：只来自头像识别，按游戏里的顺序。
   * 界面据此提示「第几格没认出」并给出候选。新局、清空本局时清空；没识别过的盟约不在里面
   */
  banGroups: BanGroup[]
  /**
   * 单人模式（v0.5.0）：这一局的开局页是单人的（核心盟约二选一）。单人开局页不读，等对局里的「对局详情」页。
   * 识别到下一局开局页（单人或联机）、清空本局时重置
   */
  solo: boolean
  /**
   * 单人开局后还在等对局详情页：界面在本局页提示「进入对局后点顶部回合数左边的放大镜打开对局详情」。
   * 这一局第一次截到对局详情页、宿主自动开始持续识别时变为 false（之后再打开详情页不再自动开始）；
   * 用户手动识别成功也变为 false
   */
  awaitingDetail: boolean
  /**
   * 这一局用持续识别看过禁用列表（v0.5.0）：用户点「持续识别」或单人对局详情自动开始的那次，至少成功识别一次就为 true。
   * false 时禁用干员主要是按盟约规则推算的（开局页自动识别只看得到列表开头），界面提示去对局详情往下翻核对。
   * 新局、清空本局时回到 false
   */
  bansScanned: boolean
  /** 本局的游戏模式（v0.5.0）：开局页左上角读到的「规程等级16」「险境模拟」等，对上 BossHp.mode；没读到为 null */
  mode: { id: string; name: string } | null
}

/** 禁用列表里一个盟约组 */
export interface BanGroup {
  covenant: CovenantId
  /** 从组的第一格起连续看到过的格子，按游戏顺序 */
  slots: BanSlot[]
  /** 这一组的结尾也看到了（后面一格的位置在画面内且是空的或下一组），slots.length 就是这组的总格数 */
  complete: boolean
}

/** 禁用列表里的一格 */
export interface BanSlot {
  /** 这一格的干员；null = 没认出 */
  name: string | null
  /** 这一格读到的星级 1–6 */
  tier: number
  /**
   * 没认出（或推测出）时的候选：同盟约、同星级、本组其他格没出现过、用户没说过「不是」，
   * 能确认游戏排序时再限定在前后两格之间。按可能性排，最多 6 个
   */
  candidates: string[]
  /** 只剩一个候选时宿主把它填进 name 并算进 bannedOperators，标为推测，等用户确认 */
  inferred?: boolean
}

export interface LineupMember {
  name: string
  /** 关键干员（如关键叠层）：待购里排在前面，商店标记和其他想抓的干员一样 */
  key?: boolean
  /** 旧版规划器给这名干员加的转职盟约；新建的阵容不再产生 */
  transform?: CovenantId
  /**
   * 候选装备（v0.5.0），按添加顺序，同一件不重复。priority = 「优先」（商店里按关键标记，金色括号），
   * 其余是「候选」（普通追踪标记）。优先的件数可以超过装备栏，界面只提醒。没有候选装备时可以不写
   */
  equipment?: MemberEquip[]
}

/** 干员的一件候选装备 */
export interface MemberEquip {
  name: string
  priority: boolean
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
  /**
   * 通用装备（装备名，不限数量）：不指定给哪名干员的。v0.5.0 前叫「想要的装备」，旧阵容的装备都在这里。
   * 设为目标时，这里和每名干员的候选装备一起加进追踪。旧数据没有这一项时宿主补空数组
   */
  equipment: string[]
  /** 配合的策略（Strategy.id），可不选；主要给分享阵容用。旧数据没有这一项时宿主补 null */
  strategyId: string | null
  source: 'user' | 'planned' // planned 是旧版规划器生成的，新建的都是 user
  note?: string
  /** 简介（v0.6），纯文本最多 500 字、可换行；发布到广场时默认带上。不进分享码 */
  description?: string
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
  /** 侧栏窗口置顶（总在其他窗口之上），默认 false。覆盖模式下宿主总是置顶，不看这一项 */
  alwaysOnTop: boolean
  /**
   * 跟随游戏窗口（v0.4.2，默认 true）：侧栏贴在游戏右侧、随游戏移动，放不下或全屏时切覆盖模式。
   * false：宿主不再移动侧栏、不进覆盖模式（connection.overlay 总是 false），侧栏停在用户拖到的位置和大小，
   * 下次启动还在那里；界面大小 'auto' 仍按游戏画面高度算
   */
  followGame: boolean
  /**
   * 收起方式（v0.4.2，默认 'strip'）。strip：收成左侧竖条；up：向上收起，只留标题栏和追踪列表。
   * 两种都随时可以收起，不再限于覆盖模式
   */
  collapseMode: 'strip' | 'up'
  /**
   * 游戏来源（v0.5.0，默认 'auto'）。auto：先找 PC 客户端，没有再找 MuMu；pc / mumu：只找这一种。
   * MuMu 用 ADB 截图（窗口被挡住也能识别），商店标记对准 MuMu 窗口里的游戏画面
   */
  gameSource: 'auto' | 'pc' | 'mumu'
  /** 多开 MuMu 时用哪个实例（MumuInstance.serial）；'' = 自动，只有一个实例时用它 */
  mumuSerial: string
  /** 特别关注（跨局保留）：熟悉的盟约和关键叠层干员。只做醒目提示，不影响商店标记 */
  focusCovenants: CovenantId[]
  focusOperators: string[]
}

export interface AppInfo {
  version: string // 如 "0.1.0"；开发构建为 "0.0.0"
  /** 数据文件夹的完整路径（%AppData%\StrongholdAssistant 展开后），只用来显示 */
  dataDir: string
}

/** 备份文件的预览：导入前给用户确认 */
export interface BackupSummary {
  appVersion: string // 导出它的版本
  exportedAt: string // ISO 时间
  lineups: number
  focusCovenants: number
  focusOperators: number
}

/**
 * 导入方式。merge：阵容按 id 合并（文件里的覆盖同 id 的，其余加进来），关注取并集，偏好设置保持当前的；
 * replace：阵容、关注、偏好设置都换成文件里的。两种都不动本局状态（已持有、追踪），
 * 目标阵容在导入后不存在时取消目标。
 */
export type ImportMode = 'merge' | 'replace'

/**
 * 一个版本的更新说明（结构化）。仓库里每版一个 `src/ui/release-notes/<版本>.json`，随界面打包作历史日志；
 * 发版时同一份作为 Release 附件 `release-notes.json` 上传，旧版本发现更新时由宿主下载给界面显示。
 */
export interface ReleaseNotes {
  schema: 'sa-release-notes/1'
  version: string
  date: string // YYYY-MM-DD
  items: {
    kind: 'new' | 'improved' | 'fixed'
    title: string
    detail: string
    /** 重点：更新后弹出的「这一版有什么新东西」里排在前面 */
    highlight?: boolean
  }[]
}

/** 自动更新的状态（检查、下载、校验都在宿主里做） */
export interface UpdateStatus {
  state: 'idle' | 'checking' | 'latest' | 'available' | 'downloading' | 'ready' | 'failed'
  version?: string // available 及之后：新版本号
  notesUrl?: string // 新版本的发版页
  /** available 时：新版本 Release 附件里的 release-notes.json；没有这个附件或下载失败就不带，界面只给发版页链接 */
  notes?: ReleaseNotes
  progress?: number // downloading 时 0–1
  message?: string // failed 时给用户看的原因
}

/** 本局进行中的状态，界面改动后整体回写 */
export interface Session {
  targetLineupId: string | null
  /** 已持有的干员。2026-11-11 起界面不再使用（干员会反复买卖，记不准），保留字段兼容旧数据，界面写回空数组 */
  owned: string[]
  /**
   * 追踪的干员 = 商店里标记的干员（2026-11-11 起）。设为目标时界面把目标名单加进来，取消或换目标时移出，
   * 用户可以逐个取消；宿主只按这张表标记，不再自动加目标名单。
   */
  tracked: string[]
  /** 追踪的装备，规则同 tracked。旧数据没有这项时宿主补空数组 */
  trackedEquipment: string[]
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
  /**
   * 手动修正：单个干员是否禁用。同时更新 banGroups：banned=true 时填进各组第一个候选里有他的空格（推测格被确认则去掉 inferred）；
   * banned=false 时他所在的格子变回没认出，并且这格以后不再把他当候选
   */
  setOperatorBanned(name: string, banned: boolean): Promise<MatchInfo>
  /**
   * 清空本局：盟约状态、禁用干员（含手动标记和同局累计）、策略、首领、特训敌人全部清掉，source 回到 'none'。
   * 打完一局后编辑阵容时，被禁的干员不再变暗挡着。持续识别进行中时界面不让点
   */
  resetMatch(): Promise<MatchInfo>
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
  /** 用资源管理器打开数据文件夹 */
  openDataFolder(): Promise<void>
  /** 列出正在运行的 MuMu 实例（v0.5.0），设置里多开时让用户选；没有就是空数组 */
  mumuInstances(): Promise<MumuInstance[]>
  /**
   * 弹出系统保存对话框导出备份（.json：我的阵容、特别关注、偏好设置；不含本局状态、窗口位置）。
   * 默认文件名「卫戍协议助手-备份-YYYY-MM-DD.json」。用户取消返回 null，否则返回保存的完整路径。
   */
  exportData(): Promise<string | null>
  /**
   * 弹出系统打开对话框选备份文件并读取预览。用户取消返回 null。
   * 文件不是有效备份时报错，错误文字直接给用户看。token 交给 importData 用，只在本次运行内有效。
   */
  pickBackup(): Promise<{ token: string; summary: BackupSummary } | null>
  /** 导入 pickBackup 选的文件。导入前宿主先把当前数据导出到数据文件夹的 backups/ 里 */
  importData(token: string, mode: ImportMode): Promise<void>
  /** 用系统默认浏览器打开外部链接（界面里不跳转） */
  openUrl(url: string): Promise<void>
  /** 把文字写进系统剪贴板（分享码等）。由宿主写，不用网页的剪贴板接口，免得受 WebView2 焦点和权限影响 */
  copyText(text: string): Promise<void>
  /**
   * 阵容广场登录（v0.6）：宿主弹一个小窗口打开 https://sa.lubeiluchen.com/login?app=1，网站登录成功后把凭据交给宿主，
   * 宿主加密保存并关窗，返回凭据；用户直接关掉窗口返回 null。界面拿凭据带 Authorization: Bearer 调广场接口
   */
  plazaLogin(): Promise<string | null>
  /** 已保存的广场凭据（启动时取一次）；没登录过为 null */
  plazaToken(): Promise<string | null>
  /** 删除本地保存的广场凭据（界面先调后端的退出接口） */
  plazaLogout(): Promise<void>
  /** 后端推送：更新状态变化 */
  onUpdate(cb: (u: UpdateStatus) => void): () => void
  /** 无边框窗口的按钮 */
  /**
   * 窗口操作。ready：见 onWindow。collapse：收成竖条（宽 48 DIP × 界面缩放，左边和上边位置不变，高度不变）；
   * expand：恢复收起前的宽度和高度。v0.4.2 起任何模式都能收起；进出覆盖模式、游戏窗口丢失都不再自动展开，
   * 只有界面发 expand 才展开
   */
  windowCommand(cmd: 'minimize' | 'close' | 'collapse' | 'expand' | 'ready'): Promise<void>
  /**
   * 向上收起（v0.4.2）：宽度和左上角不变，高度改成 height（界面 CSS 像素，宿主乘界面缩放）。
   * 收起期间内容高度变了（追踪列表增减）界面会再发一次；跟随游戏时上边仍和游戏对齐。expand 恢复
   */
  collapseUp(height: number): Promise<void>
  /**
   * 启动与关闭过渡。界面挂载后立刻发 windowCommand('ready')，宿主把原生骨架换成网页（网页之前是隐藏的，
   * 隐藏页面里 requestAnimationFrame 不触发，所以不能等动画帧）；网页变为可见后自己播入场动画。
   * 关闭时宿主先推送 'closing'，等界面播完退场（约 150ms）再隐藏窗口。
   */
  onWindow(cb: (e: 'closing') => void): () => void
  /**
   * 从网站点「在助手里打开」（v0.6）：stronghold://import?post=<帖子 id> 或 stronghold://import?code=<分享码>。
   * 宿主把助手弹到前台并原样推送这个网址，界面自己解析；启动参数里带的也在界面就绪后推一次
   */
  onDeepLink(cb: (url: string) => void): () => void
  /** 后端推送：识别到新的开局信息 */
  onMatch(cb: (m: MatchInfo) => void): () => void
  /** 后端推送：游戏窗口找到 / 丢失 / 尺寸或全屏变化 */
  onConnection(cb: (c: Connection) => void): () => void
  /** 后端推送：识别开始、结束 */
  onScan(cb: (s: ScanStatus) => void): () => void
}
