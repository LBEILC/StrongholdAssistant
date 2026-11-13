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
  /** 游戏数据表里的干员编号（v0.5.2），如 'char_171_bldsk'；从森空岛导入时用 */
  charId: string
  /**
   * 自选干员（v0.7.0）：开局自选后会在商店刷出（数据表棋子编号 chess_char_…_diy…，如凯尔希·思衡托、予愿安洁莉娜）。
   * 能追踪、能进阵容、商店标记照常；图鉴里单独归到「自选」组。普通干员没有这个字段
   */
  diy?: true
  /** 可选技能（v0.7.0），按游戏里的顺序；素材在 data/skills/<id>.webp */
  skills: OperatorOption[]
  /** 可选模组（v0.7.0），没有就是空数组；素材在 data/modules/<id>.webp（模组类型图标） */
  modules: OperatorOption[]
  /** 数据默认的技能、模组 id（v0.7.0）；没有模组为 null */
  defaultSkill: string
  defaultModule: string | null
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
  /**
   * 游戏数据表里这件装备的道具编号（v0.7.0），如 'chess_item_6_03_e_a'；森空岛攻略和历史战绩里用它表示装备。
   * 同名装备有普通 / 进阶等多个道具编号时取普通的；对照表另外认得进阶版的编号（见 itemAliases）
   */
  itemId: string
  /** 同一件装备的其他道具编号（如进阶版），森空岛数据里可能出现；没有就不写 */
  itemAliases?: string[]
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
  /** 识别不了、要用户自己点上的收入来源（v0.7.0），见 IncomeSource */
  incomeSources: IncomeSource[]
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
  /**
   * 侧栏在游戏的哪一侧（v0.7.5），connected 时才有：贴靠时是游戏窗口外的左 / 右，覆盖时是游戏画面里的左 / 右。
   * 收起竖条贴着游戏窗口那条边：贴右侧、盖左侧时竖条在侧栏左边缘；贴左侧、盖右侧时在右边缘。界面据此决定展开箭头朝向
   */
  side?: 'left' | 'right'
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
  /**
   * 结束时的结果说明：成功时是一句确认，失败时是原因和怎么办，直接显示给用户。
   * v0.7.2：失败时第二行起是技术细节（截到的画面尺寸、界面缩放、哪一步没认出、OCR 读到了什么等），界面折叠显示，同消息桥的错误约定
   */
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
  /** 回合与资金（v0.7.0）：休整期扫描商店时顺带读出；这一局还没读到过为 null。新局、清空本局时清空 */
  economy: Economy | null
}

/**
 * 回合与资金（v0.7.0，设计见 docs/design.md「回合与资金」）。资金都是游戏里的「资金」数
 */
export interface Economy {
  round: number
  /** 最近一次读到的当前资金 */
  money: number
  /** 本回合第一次读到的资金；这回合中途才开始读的为 null */
  startMoney: number | null
  /** 调度中心等级；没读到为 null */
  level: number | null
  /** 升级要花的钱；满级或没读到为 null */
  upgradeCost: number | null
  /** 最近一次读到的时间（ISO）。不在休整期时不更新，界面据此变灰 */
  readAt: string
  /**
   * 下回合预计资金：下回合基础资金 + extra + 策略效果（利滚利按 money 实时算；得闲饮茶第 1、2 回合为 null，
   * 第 3 回合是三回合基础合计）。没有下回合的基础资金数据时为 null
   */
  next: number | null
  /**
   * 本局其他额外收入（击倒等）= 本回合起始资金 − 本回合基础资金 − 本回合生效的 incomes，不小于 0；算不出为 0。
   * 当作下回合也会有，算进 next
   */
  extra: number
  /** 用户自己点上的收入来源（识别不了的经济装备、售出的波登可），算进 next；新局清空 */
  incomes: Income[]
  /**
   * 本局策略的经济规则，界面据此写小字：carry 利滚利、deferred 得闲饮茶、kazimier 业务指标、
   * random 私募基金 / 献身护卫 / 宝藏探机 / 神秘顾客 / 量体裁衣；没有为 null
   */
  rule: 'carry' | 'deferred' | 'kazimier' | 'random' | null
  /** 基础资金来自：own 自己的对局、shared 大家的资金表、none 还没有这个模式的数据 */
  basis: 'own' | 'shared' | 'none'
}

/** 一条用户点上的收入（v0.7.0） */
export interface Income {
  id: string
  /** IncomeSource.name */
  source: string
  elite: boolean
  /** 实际金额（按 elite 取 amount 或 eliteAmount） */
  amount: number
  kind: 'each' | 'once'
  /** 点上时的回合；从下一回合起生效，once 只在下一回合生效 */
  round: number
}

/**
 * 会给以后回合加资金、但识别不了的东西（v0.7.0），由宿主从数据表效果文字里整理：
 * 「之后每回合开始时额外获得 N 资金」→ each，「下回合额外获得 N 资金」→ once。
 * 立即给钱的（盟约之币、驮兽储蓄罐、定期存折）不在里面：当前资金读得到
 */
export interface IncomeSource {
  /** 装备名或干员名 */
  name: string
  /** equipment 装备（装上时）、sell 售出这名干员时 */
  trigger: 'equipment' | 'sell'
  kind: 'each' | 'once'
  amount: number
  /** 进阶版 / 精锐时的金额 */
  eliteAmount: number
}

/** 历史记录里的一局（v0.7.0）：本地自动记录，和森空岛战绩按时间对上后合成一条 */
export interface MatchRecord {
  id: string
  /** 开始时间（ISO）：本地记录为认到这一局的时间，只有森空岛战绩时为它的时间 */
  time: string
  /** 本地记录：开局信息和资金。只有森空岛战绩时为 null */
  local: {
    mode: { id: string; name: string } | null
    solo: boolean
    strategyId: string | null
    covenants: { id: CovenantId; incomplete: boolean }[]
    bannedOperators: string[]
    bossId: string | null
    /** 当时的目标阵容（快照，不随之后的修改变） */
    lineup: Lineup | null
    /** 每回合的起始资金，没读到的回合不在里面 */
    rounds: { round: number; startMoney: number }[]
    /** 最后看到的回合数；没读到过为 null */
    lastRound: number | null
    /** 最后一次读到画面的时间（ISO） */
    lastSeenAt: string
  } | null
  /** 对上的森空岛战绩：结果、最终干员和装备。没对上为 null */
  skland: SklandRecord | null
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
  /** 目标精锐（v0.7.0）：这名干员要凑到精锐（进阶） */
  elite?: true
  /** 技能（Operator.skills 的 id，v0.7.0）；不写 = 用默认（我的默认 → 数据默认） */
  skill?: string
  /** 模组（Operator.modules 的 id，v0.7.0）；不写 = 用默认；'' = 不带模组 */
  module?: string
}

/** 干员可选的一个技能或模组（v0.7.0） */
export interface OperatorOption {
  /** 游戏数据编号，如 'skchr_estell_2'、'uniequip_002_estell'（和森空岛一致） */
  id: string
  name: string
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
/**
 * 阵容的一个回合阶段（v0.7.0，照森空岛攻略的「1-4 回合」页签）。成型阵容不在这里：它就是 Lineup 顶层的 goals / members / equipment
 */
export interface LineupStage {
  /** 阶段名；空串时界面按回合叫「1～4 回合」 */
  name: string
  /** 适用回合（含两端，可以相同），1 起 */
  fromRound: number
  toRound: number
  goals: CovenantGoal[]
  members: LineupMember[]
  equipment: string[]
}

export interface Lineup {
  id: string
  name: string
  /** 成型阵容的目标盟约；有回合阶段时，各阶段自己的在 stages 里 */
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
  /**
   * 成型之前的回合阶段（v0.7.0），按回合先后，个数不限、回合不重叠；没有就不写。宿主原样保存（和 goals 一样整段存取），
   * 旧版本不认这一项，只用顶层（成型阵容）
   */
  stages?: LineupStage[]
  updatedAt: string // ISO 时间
}

/** 游戏内正在使用的卫戍协议主题：三套主题商店名称栏的位置不同，商店识别要按主题来 */
export type GameTheme = 'terminal' | 'ursus' | 'closure' // 卫戍协议终端 / 乌萨斯齐心团 / 「全能的」可露希尔

/** 助手界面的皮肤（v0.8.0）：'follow' 跟着游戏主题，或固定成某一套（和游戏主题分开设置） */
export type UiSkin = 'follow' | GameTheme

export interface Settings {
  gameTheme: GameTheme
  /** 界面皮肤（v0.8.0，默认 'follow'）。只影响助手界面的样子，和商店识别无关 */
  uiSkin: UiSkin
  shopMarkers: boolean // 是否在游戏商店里给追踪的干员打标记
  autoCheckUpdate: boolean // 启动时检查新版本
  /**
   * 更新源（v0.7.2，默认 'github'）。mirrorchyan：检查走 Mirror酱；填了有效 CDK 时从 Mirror酱 下载，
   * 没填就从 GitHub 下载（UpdateStatus.source 告诉界面实际从哪下）
   */
  updateSource: 'github' | 'mirrorchyan'
  /** Mirror酱 CDK（v0.7.2，默认 ''）。宿主不写日志、不进诊断包；导出备份时置空，导入时保留本机的 */
  mirrorChyanCdk: string
  /**
   * 更新用的代理（v0.7.6，默认 ''）。'' 跟随系统代理；否则是 'http://主机:端口' 或 'socks5://主机:端口'（不带账号密码）。
   * 只作用于检查和下载更新（GitHub、Mirror酱），改了立即生效。宿主存之前校验格式；导出备份时置空，导入时保留本机的
   */
  updateProxy: string
  /**
   * 界面大小（侧栏整体缩放）。'auto' 跟随游戏画面：缩放 = 游戏客户区高度（DIP）/ 1080，限制在 1–2；
   * 数字为固定倍数，取 0.5、0.75、1、1.25、1.5、1.75、2（0.5、0.75 是 v0.7.4 加的，给系统缩放开得高的屏幕）。侧栏宽度范围也按同样倍数缩放。
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
   * 贴在游戏哪边（v0.7.5，默认 'auto'）。auto：右边放得下贴右侧，否则左边放得下贴左侧，都放不下进覆盖模式；
   * right / left：只贴这一边，放不下就进覆盖模式。followGame 为 false 时不起作用
   */
  dockSide: 'auto' | 'right' | 'left'
  /** 覆盖模式盖在游戏画面哪一侧（v0.7.5，默认 'left'，纵向仍是客户区 15%～97%）；两侧分别记拖动后的位置 */
  overlaySide: 'left' | 'right'
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
  /**
   * 收起 / 展开的全局快捷键（v0.7.0，默认 'Alt+S'；原定 Alt+Q 在用户电脑上被别的软件占用）。写法：修饰键按 Ctrl、Alt、Shift、Win 的顺序，再接主键，用 + 连接，
   * 如 'Ctrl+Shift+F2'；'' 为关闭。只能经 setHotkey 改，saveSettings 里改了这一项宿主忽略。
   * v0.7.2 起允许单键（不带修饰键，如 'Tab'、'D'、'F2'、'`'）：单键不注册系统热键（那样别的程序里就按不出这个键了），
   * 改为只在游戏或助手在前台时生效，而且不拦截，按键照样传给游戏。带修饰键的仍是全局热键
   */
  toggleHotkey: string
  /**
   * 商店刷新键（v0.7.2，默认 'D'，'' 为关闭）：游戏在前台时按下这个键（游戏里刷新商店），商店标记立刻隐藏，
   * 识别到新的商店画面再显示，免得旧标记挂在新卡上。写法同 toggleHotkey，一般是单键；只看不拦，按键照样传给游戏
   */
  refreshKey: string
  /** 启动时打开（v0.7.2，默认 'remember'）：remember 上次关闭时是侧栏还是工作台就打开哪个；sidebar 总是侧栏；workbench 总是工作台 */
  startupMode: 'remember' | 'sidebar' | 'workbench'
  /**
   * 我的默认（v0.7.0）：干员名 → 默认技能、模组（模组 '' = 不带）。编辑器「设为我的默认」或历史页「从森空岛战绩生成」写入；
   * 加干员时优先用这里，没有再用数据默认。没有就是空对象
   */
  operatorDefaults: Record<string, { skill?: string; module?: string }>
  /** 帮大家补全资金表（v0.7.0，默认 false）：每局结束匿名上报各回合起始资金，见 docs/design.md「回合与资金」 */
  shareIncome: boolean
  /**
   * 账号云同步（v0.7.0）：同步逻辑在界面里（直接调广场的 POST /api/sync），宿主只负责把这一项原样存下来。
   * 默认 { enabled: false, since: null, pushedAt: null, prefsAt: null, deleted: [] }
   */
  cloudSync: CloudSyncState
}

/** 云同步在本机记的状态（v0.7.0），只有界面读写 */
export interface CloudSyncState {
  enabled: boolean
  /** 这份同步状态属于哪个广场账号（Me.id，v0.7.6）；和当前登录的不一样就从头合并。null = 旧版本没记 */
  account?: string | null
  /** 上次同步服务端返回的 now；下次只下载这之后变了的。null = 还没同步过 */
  since: string | null
  /** 上次上传成功时的本机时间；updatedAt 比它新的阵容下次要上传。null = 全部上传 */
  pushedAt: string | null
  /** 本机改同步偏好（特别关注、游戏主题、商店标记、我的默认）的时间；比 pushedAt 新就上传 */
  prefsAt: string | null
  /** 删掉、还没传上去的阵容 */
  deleted: { id: string; at: string }[]
}

export interface AppInfo {
  version: string // 如 "0.1.0"；开发构建为 "0.0.0"
  /** 数据文件夹的完整路径（%AppData%\StrongholdAssistant 展开后），只用来显示 */
  dataDir: string
  /** 启动时快捷键注册失败（被别的软件占用）为 true，设置里红字提示 */
  hotkeyFailed: boolean
  /** 启动时宿主打开的是工作台（v0.7.2，按 Settings.startupMode 决定）；界面据此初始化 app.workbench */
  workbench: boolean
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
  /** available 时：这次实际从哪下载。updateSource 是 mirrorchyan 但没填 CDK 时为 'github' */
  source?: 'github' | 'mirrorchyan'
  /** 带 CDK 检查且 CDK 有效时：CDK 到期时间（ISO）。latest、available 都可能带 */
  cdkExpiresAt?: string
  /** failed 时：是 CDK 的问题（过期、填错、今日次数用完、类型不对、被封），界面在旁边给「改用 GitHub」 */
  cdkProblem?: boolean
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
  /**
   * 目标阵容固定在哪个阶段（v0.7.0）：stages 的下标，等于 stages.length 表示成型阵容；null 或没有 = 自动（按读到的回合数）。
   * 换目标时界面写回 null
   */
  stage?: number | null
}

/**
 * 森空岛阵容攻略（v0.7.0）。编号都是游戏数据表里的原样，界面按目录里的 charId / itemId / 策略 id 换成名字。
 * 来自 zonai.skland.com/web/v1/game/garrison/guide/info 的 data（设计 2026-11-12 在页面里看到的结构）：
 * guide.title、guide.strategyIds[]、guide.tabs[]、guide.versionId、poster.nickname、isInvalid
 */
export interface SklandGuide {
  /** 攻略页地址（只留 guideId） */
  url: string
  title: string
  /** poster.nickname */
  author: string
  /** guide.strategyIds：备选策略，如 'band_bonus'；可能为空 */
  strategies: string[]
  /** guide.versionId，如 'V3_1'；和当期数据不一致时界面提示「这篇攻略是其他赛季的」 */
  versionId: string
  /** guide.tabs：一篇攻略里的几个阵容（阶段），按页面顺序；至少一个 */
  stages: SklandStage[]
}
export interface SklandStage {
  /** tab.tabType（数字）：1 → 'FORMATION'（成型阵容），其他值转成字符串原样给，界面按顺序叫「阶段 N」 */
  type: string
  /** tab.startRound / endRound：适用回合，0 表示没填 */
  startRound: number
  endRound: number
  /** tab.recChars[].charId，按顺序，如 'char_171_bldsk'；elite 来自 mergeLevel（1 = 精锐）；skill / module 来自 skillId / moduleId（空串不写） */
  operators: { id: string; elite: boolean; skill?: string; module?: string }[]
  /** tab.recItems[].itemId，如 'chess_item_6_03_e_a' */
  equipment: string[]
  /** tab.activeBonds 的 seasonBondIds + regularBondIds，去掉 Ship 后缀（只作参考，界面按干员人数自己算档位） */
  covenants: string[]
  /** tab.operationIdea：运营思路，纯文本 */
  notes: string
}

/**
 * 森空岛历史战绩里的一局（v0.7.0）。来自 zonai.skland.com/web/v1/game/garrison/guide/history-log?seasonId=… 的
 * data.records[]（2026-11-12 设计在本机看到的原始结构，见 docs/specs/041）。一次返回本赛季全部，不分页
 */
export interface SklandRecord {
  id: string
  /** bandId；空串为 null */
  strategyId: string | null
  /** chessSquad[].charId，按顺序；elite = chessId 等于 chessInfo.goldenChessId（编号以 _b 结尾）；skill / module 来自 skillId / moduleId（空串不写） */
  operators: { id: string; elite: boolean; skill?: string; module?: string }[]
  /** chessSquad[].equips[].equipId 按干员顺序摊平（森空岛的装备是装在干员身上的） */
  equipment: string[]
  /** completeState === 1 */
  success: boolean
  /** gameEndTs（秒）→ ISO 时间 */
  time: string
  /** gameStartTs（秒）→ ISO 时间 */
  startTime: string
  /** coinCost：这局一共花掉的资金 */
  coinSpent: number
  /** killCnt：击倒敌人数 */
  kills: number
  /** bondInfo[]：结束时的盟约（bondId 去掉 Ship 后缀）和层数 */
  covenants: { id: string; layer: number }[]
  /**
   * acBossInfo[]：这局遇到的首领。bossId 是森空岛的 'boss_2' 这种编号，宿主能对上数据表就换成 Boss.id，对不上原样给；
   * killed = isKilled
   */
  bosses: { id: string; killed: boolean }[]
  /** modeType：'MULTI' 联机 / 'SINGLE' 单人 */
  mode: string
  /** 'NORMAL' / 'HARD' / 'ABYSS' / 'FUNNY' 等，原样给 */
  difficulty: string
  /** 打到第几回合 */
  passRound: number
  versionId: string
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
  /**
   * 手动选本局策略（v0.7.0）：开局识别读不到策略，用户在本局页自己选；null = 清掉。
   * 更新 MatchInfo.strategyId、economy.rule 和当前历史记录，推送 match；新局（开局识别）清空
   */
  setMatchStrategy(id: string | null): Promise<MatchInfo>
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
  /**
   * 新建（id 为空串）或覆盖保存，返回保存后的阵容；updatedAt 改成现在。
   * 整套原样存下，包括回合阶段 stages、成员的 elite / skill / module（v0.7.0）
   */
  saveLineup(l: Lineup): Promise<Lineup>
  /**
   * 云同步写入（v0.7.0）：按 id 原样写入或新建（保留传进来的 id 和 updatedAt，不改成现在），再删掉 removed 里的 id；
   * 返回全部阵容。只有同步逻辑用；目标阵容被删时和 deleteLineup 一样处理
   */
  putLineups(lineups: Lineup[], removed: string[]): Promise<Lineup[]>
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
  /**
   * 读森空岛阵容攻略（v0.7.0）：宿主在一个看不见的 WebView2 里正常打开这个攻略页，由页面自己去请求森空岛的接口，
   * 宿主用 WebResourceResponseReceived 读 guide/info 的返回（不自己调接口、不算签名、不读写凭据），整理成 SklandGuide。
   * url 必须是 https://game.skland.com/arknights/stronghold-guide/detail?…guideId=… ；一次只读一页，20 秒内没拿到数据就失败。
   * 失败时 reject 一个 Error，message 是给用户看的中文（打不开、超时、攻略不存在或已失效）
   */
  sklandGuide(url: string): Promise<SklandGuide>
  /**
   * 森空岛历史战绩（v0.7.0）：宿主弹一个看得见的森空岛小窗口（和 sklandGuide 共用登录状态的存储），打开攻略编辑页
   * https://game.skland.com/arknights/stronghold-guide/edit 。没登录时用户在窗口里自己登录；登录后用户点页面上的
   * 「导入历史战绩」（宿主可以在页面就绪后替用户点一次），宿主读 history-log 的返回，关窗，返回这些对局，新到旧。
   * 用户关掉窗口返回 null。不在森空岛里保存草稿、不发布任何东西。
   * 041 起先在看不见的页面里读，读不到（没登录）才弹窗。silent（v0.7.0，历史页打开时自动更新用）：读不到就返回 null，不弹窗
   */
  sklandHistory(opts?: { silent?: boolean }): Promise<SklandRecord[] | null>
  /**
   * 森空岛账号（v0.8.0）：读攻略、历史战绩时森空岛告诉我们的游戏角色（角色绑定接口返回里的昵称，如「博士#1234」），
   * 宿主每次读到就记在本机，退出登录时清掉。没登录过、没读到过为 null。只看本机记录，不为此打开森空岛
   */
  sklandAccount(): Promise<{ nickname: string } | null>
  /**
   * 退出森空岛登录（v0.8.0）：清掉森空岛页面的登录状态（SklandWebView2 的 Cookie 等）和记下的角色。
   * 森空岛窗口开着就先关掉（正在等的 sklandHistory 返回 null）。下次读历史战绩时会弹窗重新登录
   */
  sklandLogout(): Promise<void>
  /**
   * 改收起 / 展开快捷键（v0.7.0）：先注册新组合，成功才保存进 Settings.toggleHotkey 并注销旧的；'' 为关闭。
   * 被占用或写法不对时 reject，message 给用户看（如「这个组合被别的软件占用了，换一个试试。」），旧的照旧生效
   */
  setHotkey(combo: string): Promise<void>
  /**
   * 导出诊断包（v0.7.2）：弹保存对话框，把日志、最近几次识别失败的截图和原因、版本、画面和设置摘要打成一个 zip，返回保存路径；取消返回 null。
   * 不含广场和森空岛凭据、云同步状态、CDK，路径里的用户名打码
   */
  exportDiagnostics(): Promise<string | null>
  /** 点上一条收入来源（v0.7.0），记在本回合；返回更新了 economy 的 MatchInfo，也会推送 match */
  addIncome(source: string, elite: boolean): Promise<MatchInfo>
  removeIncome(id: string): Promise<MatchInfo>
  /**
   * 后端推送：用户按了快捷键（v0.7.0）。窗口最小化时宿主自己恢复并放到前面，不推送；否则推送，界面决定：
   * 侧栏展开 → 收起，收起 → 展开，工作台 → minimize。宿主处理快捷键时不激活助手窗口，焦点留在游戏上
   */
  onHotkey(cb: () => void): () => void
  /** 历史记录（v0.7.0），新到旧 */
  history(): Promise<MatchRecord[]>
  deleteRecord(id: string): Promise<void>
  /**
   * 把 sklandHistory 拿到的战绩存进历史（v0.7.0）：按森空岛编号去重，和本地记录按时间对上就合成一条
   * （森空岛时间在本地开始之后、lastSeenAt 后 30 分钟内，单人或联机一致），返回合并后的全部历史
   */
  saveSklandRecords(records: SklandRecord[]): Promise<MatchRecord[]>
  /** 重新生成匿名上报用的本机随机编号（v0.7.0） */
  resetShareId(): Promise<void>
  /** 后端推送：更新状态变化 */
  onUpdate(cb: (u: UpdateStatus) => void): () => void
  /** 无边框窗口的按钮 */
  /**
   * 窗口操作。ready：见 onWindow。collapse：收成竖条（宽 48 DIP × 界面缩放，左边和上边位置不变，高度不变）；
   * expand：恢复收起前的宽度和高度。v0.4.2 起任何模式都能收起；进出覆盖模式、游戏窗口丢失都不再自动展开，
   * 只有界面发 expand 才展开。
   * workbench（v0.7.0）：进工作台。窗口脱离游戏，用上次工作台的位置和大小（第一次：1280×800 DIP × 界面缩放，
   * 在游戏所在的屏幕居中；放不下就按屏幕工作区缩小），可拖动改大小（最小 960×640）；期间不跟随游戏、不自动进覆盖模式，
   * 置顶按用户设置。sidebar：回到侧栏，位置和大小照「跟随游戏窗口」设置恢复（跟随就重新贴到游戏旁，不跟随就回到侧栏的自由位置）。
   * 每次启动都是侧栏；收起（collapse / collapseUp）只在侧栏里用
   */
  windowCommand(cmd: 'minimize' | 'close' | 'collapse' | 'expand' | 'ready' | 'workbench' | 'sidebar'): Promise<void>
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
