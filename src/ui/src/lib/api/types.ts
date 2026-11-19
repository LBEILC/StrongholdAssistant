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
  /**
   * 皮肤（任务 163）：游戏 skin_table 里这名干员的全部皮肤，默认、精二在前，再按上架时间；id 是 skinId，
   * name 是皮肤名（默认写「默认服装」、精二写「精英化二」）。头像素材在 data/skins/<id>.webp（文件名里的 # @ 原样保留）。
   * 宿主还没生成这份数据时可以不写
   */
  skins?: OperatorOption[]
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
  /**
   * 这种特训敌人在前半段、后半段可能是哪一种（v0.9.0），一局里各随机出一种，weight 是权重（同一段里按权重算出现概率）。
   * 来自 data/enemy_types.json 的 firstHalfVariants / secondHalfVariants
   */
  firstHalf: EnemyVariant[]
  secondHalf: EnemyVariant[]
}

/**
 * 敌人的简单资料（v0.9.5），来自游戏数据表 enemy_handbook_table（图鉴）和 enemy_database（属性）。
 * 属性取第 0 级的基础值；卫戍协议里还会按模式和回合加成，界面写明是基础属性
 */
export interface Enemy {
  id: string // 如 enemy_1010_demon_2；图标 asset.enemy(id)
  name: string
  /** 普通 / 精英 / 领袖（handbook enemyLevel） */
  level: 'NORMAL' | 'ELITE' | 'BOSS'
  /** 攻击方式原文，如「近战」「远程」「不攻击」（handbook attackType） */
  attackType: string
  /** 伤害类型，中文：物理、法术、治疗、无（handbook damageType 逐项翻译） */
  damage: string[]
  /** 地面 / 空中（enemy_database motion：WALK / FLY） */
  motion: 'WALK' | 'FLY'
  hp: number
  atk: number
  def: number
  res: number // 法术抗性
  /** 攻击间隔，秒（baseAttackTime） */
  attackTime: number
  /** 移动速度（moveSpeed） */
  speed: number
  /** 重量等级（massLevel） */
  weight: number
  /** 能力说明，每条一句，去掉富文本标签（handbook abilityList[].text） */
  abilities: string[]
  /** 图鉴描述（handbook description），可能为空串 */
  description: string
}

export interface EnemyVariant {
  id: string // 敌人 id，如 enemy_1041_lazerd；图标 asset.enemy(id)
  name: string
  weight: number
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
  /** 敌人（v0.9.5）：特训敌人的全部变体和首领，按 id 查；详情里显示，另给按钮打开 PRTS 对应页面 */
  enemies: Enemy[]
  /** 悬赏（任务 201）：悬赏决策里能选的全部项，按 id 排序；图鉴「敌人 · 悬赏」列出来，敌人详情里写奖励 */
  bounties: Bounty[]
  /** 识别不了、要用户自己点上的收入来源（v0.7.0），见 IncomeSource */
  incomeSources: IncomeSource[]
  /**
   * 敌人回合加成（v1.0.0）：卫戍协议每个模式每回合的累计加成，键是模式 id（同 BossHp.mode，如 "mode_multi_normal"），
   * 数组下标 = 回合 − 1。超出表的回合用最后一项。没有数据的模式不在里面，界面只显示基础属性
   */
  enemyBuffs: Record<string, EnemyBuff[]>
}

/**
 * 一个回合的敌人累计加成。实际属性：生命 = 基础 × hp，攻击 = 基础 × atk，
 * 攻击间隔 = 基础 ÷ ((100 + attackSpeed) / 100)，移速 = 基础 × speed；防御、法抗、重量不变（数据里有别的加成时工程在汇报里提）
 */
export interface EnemyBuff {
  hp: number
  atk: number
  /** 攻速加数（游戏里的攻击速度，100 为基准） */
  attackSpeed: number
  speed: number
  /**
   * 不吃部分加成的敌人（v1.0.0）：游戏的加成效果带排除名单（如部分生命加成不加在器伥上），
   * 这些敌人这一回合的累计加成另算，键是敌人 id。不在里面的按上面的通用值
   */
  overrides?: Record<string, { hp: number; atk: number; attackSpeed: number; speed: number }>
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
  /** 连上的是哪种游戏（v0.5.0）：PC 客户端，或 MuMu / 雷电模拟器（v0.9.4）里的安卓版。connected 时才有 */
  source?: 'pc' | EmulatorKind
  /**
   * 游戏是不是以管理员身份运行（v0.9.1），connected 时才有；模拟器为 false。
   * 游戏是管理员、助手不是时，Windows 不把游戏前台时的按键交给助手：单独一个键（Alt、D、Tab 这类）收不到，
   * 只有系统注册的组合键（Alt+S 这类）能用。界面据此限制单键和「按住才显示」，见 AppInfo.elevated
   */
  gameElevated?: boolean
  /**
   * 找到游戏但用不了时给用户看的原因和办法（v0.5.0），state 为 searching 时才有，例如
   * 「MuMu 画面不是横屏 16:9，请在模拟器设置里改成 1920×1080 之类的分辨率」「找到 2 个模拟器实例，请在设置里选一个」
   */
  problem?: string
  /**
   * 从游戏画面认出的主题（v0.9.2），connected 且已经认出来时才有；认到的和上次不同时宿主推一次 onConnection。
   * gameThemeAuto 时商店识别和「跟随游戏主题」的界面皮肤都用它
   */
  theme?: GameTheme
}

/** 支持的安卓模拟器（v0.9.4 加雷电） */
export type EmulatorKind = 'mumu' | 'ldplayer'

/** 正在运行的一个模拟器实例（v0.5.0 只有 MuMu，v0.9.4 起含雷电，原名 MumuInstance） */
export interface EmulatorInstance {
  /** 哪种模拟器 */
  kind: EmulatorKind
  /** ADB 地址，如 MuMu "127.0.0.1:16384"、雷电 "emulator-5554"，存进 Settings.emulatorSerial */
  serial: string
  /** 给用户看的名字：模拟器里的实例名，如「MuMu安卓设备」「雷电模拟器-1」（不带「-LDPlayer」这类后缀） */
  name: string
}

/** 开局信息页识别的进度（自动触发和「重新识别」都会推送） */
export interface ScanStatus {
  busy: boolean
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
  /**
   * strategyId 从哪来（v0.9.8）：'recognized' = 选策略页或对局右侧策略头像认出来的；'manual' = 用户在本局页选的。
   * 每局只自动填一次（本局第一次认出时），之后用户手动改了就以手动为准，不再被识别覆盖。新局、清空本局时为 null
   */
  strategySource: 'recognized' | 'manual' | null
  /**
   * 本回合敌人（v0.9.8）：休整期玩家点回合数右边的放大镜打开敌人列表时读到的。
   * round 和当前回合（economy.round）不一样时界面当作「本回合还没看」。新局、清空本局时为 null
   */
  roundEnemies: RoundEnemies | null
  /**
   * 本局自选干员（v1.0.0）：开局「确定自选编队」页（第 3/3 步）选完时读到的。玩家最多带 6 名自选干员进来，
   * V 阶、VI 阶各选 0～2 名。键是阶数，值是干员名（和 bannedOperators 一样用名字）。
   * null = 这一局没读到这一页；读到了但一个没选是 { 5: [], 6: [] }。新局、清空本局时为 null
   */
  customPicks: { 5: string[]; 6: string[] } | null
  /**
   * 顶栏盟约（任务 168，设计见 docs/design.md「盟约信息显示」）：对局里顶部那排盟约图标读到的档位和层数。
   * 按 id 合并：每次读到的覆盖同 id 的旧值，这次没看见的（顶栏放不下被截断、或画面没有顶栏）保留上次的。
   * 宿主还没接上时为 undefined；新局、清空本局时为 []
   */
  covenantBar?: CovenantBarItem[]
  /**
   * 开局选策略推荐（决策 S2，任务 364/365）：宿主在本局模式、盟约、禁用、首领有变化时重算并随 match 推送。
   * 宿主还没接上时为 undefined；新局、清空本局时为 null
   */
  strategyAdvice?: StrategyAdvice | null
  /**
   * 机变推荐（任务 373）：游戏在机变页（单人第 3、9、11 回合休整前）读到卡时宿主算出并推送；
   * 离开机变页后保留到这一回合休整期结束。宿主还没接上时为 undefined；下一回合开始、新局时为 null
   */
  adaptAdvice?: AdaptAdvice | null
}

/** 机变推荐。数值和 StrategyAdvice 一样是复刻里自带机器人选了这张卡后接着打的估计 */
export interface AdaptAdvice {
  status: 'ok' | 'unavailable'
  /** unavailable 时给玩家看的一句话，如「这类机变还认不出」「游戏数据版本和推荐模型对不上」 */
  reason?: string
  round: number
  /** 种类：悬赏决策 / 道具补给 / 机密商店 / 战术决策；认不出为 null */
  family: 'bounty' | 'supply' | 'shop' | 'tactic' | null
  /** 估计按的难度，同 StrategyAdvice.basis */
  basis: string
  /** 画面上的卡，从左到右；index 对应 GameAction pickAdapt.index。认不出的卡 id、name 为 null，不排名 */
  cards: {
    index: number
    id: string | null
    name: string | null
    /** 卡上的效果原文（数据表里的，去掉富文本标记）；认不出为 null */
    desc: string | null
    clearRate: number | null
    reachedRound: number | null
    /** 1 起；认不出的卡为 null */
    rank: number | null
  }[]
  assumptions: string[]
}

/**
 * 开局选策略推荐。数值是在第三方复刻里让自带机器人用这个策略打出来的估计，不是玩家本人的胜率；
 * 界面始终把 basis 和 assumptions 给玩家看得到
 */
export interface StrategyAdvice {
  /** unavailable：这一局给不了推荐（游戏数据版本对不上、模式没读到等），reason 直接显示 */
  status: 'ok' | 'unavailable'
  reason?: string
  /** 估计按的难度，如「规程等级16」；多人模式写「按单人规程等级 n 估」 */
  basis: string
  /** 全部候选策略，按推荐顺序（第 0 个最推荐）。clearRate 0～1；reachedRound 预计撑到的回合（可带小数） */
  items: { strategyId: string; clearRate: number; reachedRound: number }[]
  /** 所有候选的平均，用来显示「比平均高多少」 */
  average: { clearRate: number; reachedRound: number }
  /** 用到的假设，逐条显示，如「按复刻自带机器人的打法估计」「多人模式按单人估」 */
  assumptions: string[]
}

/** 顶栏一个盟约图标读到的状态 */
export interface CovenantBarItem {
  id: CovenantId
  /** 图标上方的罗马数字：激活到第几档（1 起）；读不出为 null */
  level: number | null
  /** 图标里的数字：当前层数；这个盟约没有层数（图标里没数字）或读不出为 null */
  layers: number | null
  /** 最近一次读到时的回合（economy.round）；不知道为 null */
  round: number | null
  /** 最近一次读到的时间（ISO） */
  at: string
}

/**
 * 本回合敌人（v0.9.8 读到的；任务 200 起玩家可以在本局页手动补上 / 去掉）。
 * 读不全很常见：敌人多时列表要往下滚；选了悬赏后悬赏怪排第一个，把别的挤到下面
 */
export interface RoundEnemies {
  round: number
  at: string // ISO 时间，最后一次读到或手动改动
  /**
   * 合并后的名单，站位推荐用这份：读到的（去掉玩家去掉的）在前，按列表从上到下；玩家补上的在后，按补上的先后。
   * 同一回合多次读到（列表要往下翻）时合并去重。补上的后来又读到了，source 改成 'read'，位置不动
   */
  enemies: RoundEnemy[]
  /**
   * 玩家去掉的（读错了、不是这回合的），之后再读到也不加回来；界面可以点「恢复」放回去（setRoundEnemy(name, true)）。
   * 只记读到过的；补上的再去掉直接删，不进这里。没有为 []
   */
  removed: RoundEnemy[]
}

export interface RoundEnemy {
  /** Catalog.enemies 里的 id，名字对不上时为 null（同名多个 id 取目录第一项） */
  id: string | null
  name: string
  /** read = 游戏敌人列表读到的；manual = 玩家在本局页补上的 */
  source: 'read' | 'manual'
}

/**
 * 本回合可能出现的敌人（任务 200）：站位推荐筛怪组用的那份怪组表（本回合、本局模式、本局特训敌人类型；首领回合是首领关），
 * 界面拿来做补选候选、算「名单对得上几种怪组」
 */
export interface RoundEnemyOptions {
  round: number
  /** 本回合可能的怪组；weight 是出现权重（各组加起来为 1） */
  groups: {
    /** 如「飞行·法术大师A1」，同站位推荐 PlacementStream.label */
    label: string
    weight: number
    /** 这组出的敌人，按名字去重，count 是这一组一共出几只 */
    enemies: { id: string | null; name: string; count: number }[]
  }[]
}

/**
 * 悬赏（任务 201）：对局中途「悬赏决策」框里能选的一项。选了以后自己接下来的作战多出指定的敌人，
 * 打掉（或完美作战）拿资金。来自 activity_table 的 effectInfoDataDict（effectType = ENEMY_GAIN）和 effectBuffInfoDataDict
 */
export interface Bounty {
  /** 效果 id，如 enemyeffect_16_1 */
  id: string
  /** 游戏里的名字，如「悬赏·特异III」「鸭爵·悬赏」「战术特训·飞行I」「多轮悬赏 · 假想敌：蚀裂」 */
  name: string
  /**
   * kill = 悬赏：击倒它的人拿钱（自己漏了、被队友打掉，钱归队友）；
   * perfect = 战术特训：各自行动阶段就完美作战才拿钱
   */
  kind: 'kill' | 'perfect'
  /** 加的敌人，id 在 Catalog.enemies 里（工程保证都收进去了） */
  enemyId: string
  enemyName: string
  /** 一次加几只 */
  count: number
  /** 奖励资金，0 = 只加怪不给钱 */
  coin: number
  /** 持续几场：1 = 下场，2 = 接下来两场，null = 之后每场（数据里 round = 99） */
  rounds: 1 | 2 | null
  /** 名字里的类别：飞行、频次、损伤、持续、隐匿、折射、特异；没有（如「鸭爵·悬赏」）为 null */
  tag: string | null
  /** 名字末尾的 I / II / III → 1～3；没有为 null */
  grade: 1 | 2 | 3 | null
  /** 游戏原文，去掉富文本标签，如「为自身接下来两场作战添加1只暴鸰，将其击倒者获得2资金」 */
  text: string
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
  /**
   * 基础资金来自：table 固定资金表 data/round_income.json（v0.9.8 起只用这个）；none 表里没有下回合的数。
   * own / shared（自己学的、大家的资金表）是旧值，v0.9.8 起宿主不再给出
   */
  basis: 'table' | 'own' | 'shared' | 'none'
  /**
   * 下回合的基础资金是暂定值（v0.9.8）。2026-11-15 用险境整局录屏核对后界面不再标「估」，
   * 字段保留不用；以后宿主可以不再给（一律 false）
   */
  estimated: boolean
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
/**
 * 一组干员（v1.0.1，用户 2026-11-15 提，设计见 docs/design.md「阵容里的一组干员」）：这几个里任意 need 个就行，
 * 如「能天使 / 空弦 / 灰喉 任 1」。组员和固定干员（members）一样可以标关键、配候选装备、定精锐 / 技能 / 模组；
 * 同一名干员不会既在 members 又在组里，也不会在两个组里。2 ≤ members.length，1 ≤ need < members.length。
 * - 进度：组里本局拿到的人照常算盟约人数；
 * - 追踪和商店标记：组里本局拿到的人数（FieldState 的本局有的人，没卖的）< need 时，组里还没拿到、没被禁的人都按普通追踪标
 *   （标了关键的按关键标）；够了就不再标组里剩下的人；
 * - 本局适配（禁用）：组里没被禁的人数 ≥ need 才算这一组完整
 */
export interface LineupGroup {
  need: number
  members: LineupMember[]
}

/** 升本计划的一步（v1.0.1）：到第 round 回合时调度中心该升到 level 级 */
export interface LineupUpgrade {
  round: number
  level: number
}

export interface LineupStage {
  /** 阶段名；空串时界面按回合叫「1～4 回合」 */
  name: string
  /** 适用回合（含两端，可以相同），1 起 */
  fromRound: number
  toRound: number
  goals: CovenantGoal[]
  members: LineupMember[]
  equipment: string[]
  /** 这个阶段的「一组」干员（v1.0.1）；没有就不写 */
  groups?: LineupGroup[]
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
  /** 成型阵容的「一组」干员（v1.0.1）；没有就不写。宿主原样保存，旧版本不认这一项 */
  groups?: LineupGroup[]
  /**
   * 升本计划（v1.0.1）：整套阵容一份，和回合阶段无关。按 round 升序、round 不重复、level 递增（2～调度中心最高级）。
   * 设为目标后，本局到了那个回合、调度中心还没到那一级时，本局页资金行和游戏里的升级按钮上提示。没有就不写
   */
  upgrades?: LineupUpgrade[]
  updatedAt: string // ISO 时间
}

/** 敌人路线覆盖的设置（v0.9.0），宿主改了马上生效 */
export interface RouteOverlaySettings {
  enabled: boolean // 总开关，默认 false
  /** 'always' 休整期一直显示（按快捷键临时隐藏 / 再显示）；'hold' 按住快捷键时显示；'peek' 新回合先显示 peekSeconds 秒再淡出，之后按住快捷键还能看。默认 'always' */
  display: 'always' | 'hold' | 'peek'
  peekSeconds: number // 3～15，默认 6
  /**
   * 快捷键，写法同 toggleHotkey：单个键（'Alt'、'Tab'、'R'，Alt / Ctrl / Shift 单独按也算）或组合键（'Alt+R'）。默认 'Alt+R'（v0.9.1，原来是 'Alt'）。
   * 单键和「按住才显示」要收到按下、松开，只在收得到单键时可用（见 Connection.gameElevated）；只看不拦，按键照样传给游戏。
   * 收不到单键时组合键注册成系统热键，只有「按下」：一直显示 = 按一下隐藏 / 再显示，新回合先看几秒 = 按一下再看几秒
   */
  hotkey: string
  /** 路线多的时候：'cycle' 依次循环，一次突出一条（默认）；'all' 全部一起显示 */
  overlap: 'cycle' | 'all'
  cycleSeconds: number // 每条停留秒数，1～5，默认 2.2
  /** 飞行路线：'main' 只显示敌人最多的一条（默认）；'all' 全部（重合的合并）；'off' 不显示 */
  fly: 'main' | 'all' | 'off'
  /** 联机首领回合自己在哪一侧：'auto' 自动识别（认不出时两边都画，默认）；'left' / 'right' 手动指定 */
  side: 'auto' | 'left' | 'right'
}

/** 游戏内正在使用的卫戍协议主题：三套主题商店名称栏的位置不同，商店识别要按主题来 */
export type GameTheme = 'terminal' | 'ursus' | 'closure' // 卫戍协议终端 / 乌萨斯齐心团 / 「全能的」可露希尔

/** 助手界面的皮肤（v0.8.0）：'follow' 跟着游戏主题，或固定成某一套（和游戏主题分开设置） */
/**
 * 界面皮肤：三套游戏主题，加「经典」（v0.8.1，助手 0.8.0 之前的样子：终端配色，图标用 CSS 画），
 * 加两套不照游戏做的通用风格（v0.9.8）：glass 毛玻璃、round 圆润，各有深色、浅色（见 Settings.uiScheme）
 */
export type SkinId = GameTheme | 'classic' | 'glass' | 'round'
export type UiSkin = 'follow' | SkinId

/** 拖边改大小的边或角：上下左右、左上右上左下右下 */
export type ResizeEdge = 'n' | 's' | 'w' | 'e' | 'nw' | 'ne' | 'sw' | 'se'

export interface Settings {
  /**
   * 游戏主题。gameThemeAuto 为 true 时是宿主最近一次认到的主题（宿主认到新主题就改这一项并保存，下次启动先用它）；
   * 为 false 时是用户手动选的，宿主不改。商店识别按它来
   */
  gameTheme: GameTheme
  /** 自动识别游戏主题（v0.9.2，默认 true）。界面里选了某个主题就设成 false，选「自动识别」设回 true */
  gameThemeAuto: boolean
  /** 界面皮肤（v0.8.0，默认 'follow'）。只影响助手界面的样子，和商店识别无关 */
  uiSkin: UiSkin
  /**
   * 通用风格主题（glass、round）的配色（v0.9.8，默认 'system'）：跟随系统（Windows「应用模式」深浅）/ 深色 / 浅色。
   * 游戏主题和经典总是深色，不看这一项。宿主只存；不进云同步
   */
  uiScheme: 'system' | 'dark' | 'light'
  /**
   * 通用风格主题的主题色（v0.9.8，默认 ''）：'' 用主题自带的（毛玻璃蓝、圆润紫），否则是 '#rrggbb'（小写）。
   * 界面按深浅配色自己换算亮度。宿主只存，校验格式；不进云同步
   */
  uiAccent: string
  /**
   * 界面字体（v0.9.5）：本机装的字体族名（listFonts 返回的写法）；空串 = 默认（随界面打包的 MiSans）。
   * 选的字体缺字时界面自己回落到 MiSans。宿主只存，不校验字体在不在
   */
  uiFont: string
  /** 界面字重（v0.9.5）：'normal' 标准（默认）、'medium' 稍粗、'bold' 加粗。宿主只存 */
  uiFontWeight: 'normal' | 'medium' | 'bold'
  shopMarkers: boolean // 是否在游戏商店里给追踪的干员打标记
  /** 三种商店标记各自的开关和颜色、框上的文字标签（v0.9.5）。shopMarkers 是总开关 */
  markerStyles: MarkerStyles
  /**
   * 商店识别速度（v0.8.3）：'normal' 标准（默认，和以前差不多的占用）；'fast' 快速（识别得更勤，换商店后标记更快跟上，
   * 多占一些 CPU）。宿主按这个调识别节奏，改了马上生效
   */
  shopScanSpeed: 'normal' | 'fast'
  /** 敌人路线覆盖（v0.9.0）：在游戏棋盘上画本回合敌人会走的路线。本局页「敌方情报」里的快速开关就是 routes.enabled */
  routes: RouteOverlaySettings
  /**
   * 游戏覆盖层新加的两项各自的开关（v1.0.0，任务 169，设计见 docs/design.md「本局页小棋盘」）。
   * 覆盖层每项都能单独开关：商店标记仍是 shopMarkers，敌人路线仍是 routes.enabled，这里只放新加的。
   * placement：在游戏棋盘格子上标推荐放谁（有站位推荐时才画），默认 true；
   * pending：在待确认的小人头上标问号，玩家到侧栏小棋盘里点选，默认 true；
   * upgrade（v1.0.1）：目标阵容的升本计划到了这一回合、还没升时，在游戏商店的升级按钮上标「升本」，默认 true（只在商店展开时画）。
   * 两项都只在休整期、棋盘主画面、没有面板盖住时画（同路线）。旧设置文件没有这一项时宿主补默认值
   */
  overlayLayers: { placement: boolean; pending: boolean; upgrade: boolean }
  /**
   * 站位推荐按什么目标算（v1.0.0，任务 170，默认 'balanced'）：漏怪最少 / 平衡 / 叠层最多。
   * 小棋盘推荐区里切换；改了宿主重算。跟着设置文件存，进云同步和备份
   */
  placementGoal: PlacementGoal
  /**
   * 场上自动识别（v1.0.1，用户 2026-11-15 提）。v1.2.4 起暂时固定为 false、设置里不给开关（一拖人就重认、推荐位一直变），
   * 宿主读写设置时都按 false。true：休整期照常自动认场上的人、自动重算推荐。
   * false：宿主不在扫描循环里认场上（商店、资金、回合等照常读，买人仍记进本局有的人），场上状态和推荐停在上一次，
   * 只有玩家点本局页「场上」的「识别」（recognizeField）时认一次、算一次推荐。旧设置文件没有这一项时按 true
   */
  fieldAuto: boolean
  /**
   * 「识别场上」快捷键（v1.0.1，用户 2026-11-15 提），默认 'Alt+F'，'' 为关闭。写法和接收方式同 routes.hotkey
   * （单键只在收得到时可用，收不到时组合键注册成系统热键；只看不拦，按键照样传给游戏）。
   * 按下时宿主推送 fieldHotkey，界面调 recognizeField，和点「识别」按钮完全一样。自动识别开着关着都能用
   */
  fieldHotkey: string
  /**
   * 帮助改进识别（v1.0.5，默认 false）：打开后宿主在对局里存下认人用的截图和记录（任务 235），最近 3 局，
   * 一局约 40 MB，存在本机数据文件夹的 collect/ 下；玩家在设置里把某一局导出成 zip 发给我们。关掉时不删已存的
   */
  collect: boolean
  autoCheckUpdate: boolean // 启动时和开着时每两小时检查新版本，发现了就在后台下载好（v1.0.4）
  /** 自动检查发现新版后是否在后台下好（v1.2.8，默认 true）；false = 只在横幅提示，点了才下载 */
  autoDownloadUpdate: boolean
  /**
   * 更新源（v0.7.2，默认 'github'）。mirrorchyan：检查走 Mirror酱；填了有效 CDK 时从 Mirror酱 下载，
   * 没填就从 GitHub 下载（UpdateStatus.source 告诉界面实际从哪下）
   */
  updateSource: 'github' | 'mirrorchyan'
  /** Mirror酱 CDK（v0.7.2，默认 ''）。宿主不写日志、不进诊断包；导出备份时置空，导入时保留本机的 */
  mirrorChyanCdk: string
  /**
   * GitHub Token（v1.0.2，默认 ''，参考 MAA 的 GitHubToken）。'' = 匿名：GitHub 按 IP 每小时只给 60 次 API 额度，
   * 反复检查更新容易用完。填了之后检查更新的请求带 Authorization，按 Token 计额度（每小时 5000 次）。
   * 只发给 api.github.com，下载包（github.com）不带这个头。宿主加密存在设置文件里、不写日志、不进诊断包；
   * 导出备份时置空，导入时保留本机的
   */
  githubToken: string
  /** GitHub Token 的到期时间（v1.0.2，ISO 8601，默认 ''）。宿主从响应头 GitHub-Authentication-Token-Expiration 拿到；'' = 永不过期或还没校验 */
  githubTokenExpiresAt: string
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
   * 游戏来源（v0.5.0，默认 'auto'；v0.9.4 加 'ldplayer'）。auto：先找 PC 客户端，没有再找 MuMu 和雷电里正在运行明日方舟的实例；
   * pc / mumu / ldplayer：只找这一种。模拟器用 ADB 截图（窗口被挡住也能识别），商店标记对准模拟器窗口里的游戏画面
   */
  gameSource: 'auto' | 'pc' | EmulatorKind
  /**
   * 多开模拟器时用哪个实例（EmulatorInstance.serial）；'' = 自动，只有一个实例在运行明日方舟时用它。
   * v0.9.4 前叫 mumuSerial，旧设置文件里的 mumuSerial 读进这里
   */
  emulatorSerial: string
  /**
   * 自定义 ADB 地址（v1.2.8）：'' = 自动发现；填了就只连这个地址（如 '192.168.1.5:5555'、'emulator-5554'），
   * 优先于 emulatorSerial。宿主保存时校验格式：'' 或 主机:端口（端口 1～65535）或 emulator-数字
   */
  adbAddress: string
  /**
   * 去掉黄框（v1.3.3，默认 false）：PC 客户端不用窗口捕获，只用前台截屏（BitBlt）。
   * Windows 10（build 20348 以前）上窗口捕获会在游戏窗口外画一圈黄框且去不掉，介意的玩家自己打开。
   * 代价：游戏窗口要在最前面、不被别的窗口挡住，否则截不到。只影响 PC 来源，模拟器走 ADB 不受影响
   */
  hideCaptureBorder: boolean
  /**
   * 允许多开（v1.3.3，默认 false，只看默认配置里的这一项）：关着时再双击助手只激活已开的那个；
   * 开着时再双击会新开一个具名配置（「2」「3」…，没有就新建），各用各的设置和数据，第二个起默认不注册全局快捷键。
   * 命令行 --profile 名字 不受这个开关限制，agent 和脚本随时可以拉起指定配置
   */
  multiInstance: boolean
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
  /**
   * 以管理员身份运行（v0.9.1；v1.0.5 起默认 true，启动时弹系统确认，玩家不同意就按普通身份运行）：游戏是管理员身份运行时，助手也要是管理员才收得到单独按的键（路线的 Alt、商店刷新键 D、单键的收起键）。
   * 打开后每次启动弹一次系统确认。只能经 setRunAsAdmin 改，saveSettings 里改了这一项宿主忽略
   */
  runAsAdmin: boolean
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
  /**
   * 广场新消息提醒（v0.9.3，默认 true）：登录广场后，标题栏显示消息铃铛和未读数，启动时和每 10 分钟问一次未读数。
   * 关掉就不在后台联网问、标题栏不显示铃铛；消息仍能在广场分段里打开看。不进云同步
   */
  plazaNotices: boolean
  /**
   * 自动操作（任务 390～393，设计见 docs/自动操作.md）。旧设置文件没有这一项时宿主补默认值：
   * { enabled: false, consent: 0, external: false, mode: 'dryRun', stopHotkey: 'Ctrl+Alt+Q', api: false }。
   * 不进云同步、不进备份（换电脑要重新看风险页）
   */
  control: ControlSettings
}

/** 自动操作的设置（任务 391） */
export interface ControlSettings {
  /** 自动操作总开关，默认 false。界面只有在 consent 等于当前风险说明版本（CONTROL_CONSENT_VERSION）时才让打开 */
  enabled: boolean
  /** 用户勾选同意的风险说明版本，0 = 没同意过。界面在风险页勾选并点「开启」时写入 */
  consent: number
  /** 允许外部程序通过本机接口下发操作，默认 false；enabled 为 false 时宿主按 false 处理 */
  external: boolean
  /** 执行模式上限：dryRun 试运行（默认）、confirm 逐步确认、auto 自动。界面和本机接口下发时都不能比它更激进 */
  mode: ControlMode
  /** 停止键，默认 'Ctrl+Alt+Q'，写法同 fieldHotkey；'' 为不用（界面不允许清空，只能改）。低级钩子监听，不拦截，多开时每个实例都响应 */
  stopHotkey: string
  /** 本机接口（HTTP，任务 392）是否监听，默认 false。关着时 sa 命令行连不上 */
  api: boolean
}

/** 执行模式，从保守到激进：dryRun 只在截图上标出会点哪，不发输入；confirm 每个动作等用户点「执行」；auto 一口气做完（仍逐个核对） */
export type ControlMode = 'dryRun' | 'confirm' | 'auto'

/** 朝向，和 PlacementMove.facing 一致 */
export type Facing = 'up' | 'down' | 'left' | 'right'

/**
 * 一个对游戏的操作（docs/自动操作.md「操作清单」）。格子同 FieldCell；商店卡位 slot 0～5 从左到右，同 ShopLayout。
 * expect：执行前核对的名字（卡名 / 干员名），对不上就不做、整批停下；不给就不核对名字
 */
export type GameAction =
  | { type: 'buy'; slot: number; expect?: string }
  | { type: 'sell'; from: FieldCell; expect?: string }
  | { type: 'refresh' }
  | { type: 'freeze'; on: boolean }
  | { type: 'upgrade' }
  | { type: 'move'; from: FieldCell; to: FieldCell; facing?: Facing; expect?: string }
  | { type: 'face'; at: FieldCell; facing: Facing }
  | { type: 'ready'; on: boolean }
  | { type: 'pickStrategy'; strategyId: string }
  | { type: 'pickAdapt'; index: number }
  | { type: 'pickCustom'; tier: number; operators: string[] }

/** 一批操作的执行情况（任务 391）。一个实例同时只有一批 */
export interface ControlJob {
  id: string
  /** 谁下发的：界面按钮（如「照推荐摆」）还是本机接口 */
  source: 'ui' | 'api'
  /** 实际用的模式（下发要求和设置上限里更保守的那个） */
  mode: ControlMode
  /**
   * running 正在做；waitingConfirm 逐步确认模式下等用户点「执行」或「跳过」；
   * done 全部做完（试运行做完也是 done）；stopped 被停下；failed 画面不符、核对没通过或出错，停在某一步
   */
  state: 'running' | 'waitingConfirm' | 'done' | 'stopped' | 'failed'
  steps: ControlStep[]
  /**
   * 停下或失败的原因：user 界面点停止；hotkey 停止键；input PC 上用户动了鼠标或键盘；api 本机接口要求停；
   * screen 画面不是该动作能做的页面；mismatch 目标对不上（expect、格子里没人等）；verify 做完核对没通过；
   * lost 游戏窗口或模拟器断了；limit 超过限速或单个休整期的动作上限；disabled 自动操作被关掉；error 其他错误
   */
  reason: 'user' | 'hotkey' | 'input' | 'api' | 'screen' | 'mismatch' | 'verify' | 'lost' | 'limit' | 'disabled' | 'error' | null
  /** 给人看的一句话，如「第 3 步：B3 没有人，场上可能变了」；正常做完为 null */
  message: string | null
  startedAt: string
  endedAt: string | null
}

export interface ControlStep {
  action: GameAction
  /** 给人看的描述，如「把 能天使 从 B3 拖到 C5，朝右」「买第 2 张卡（深靛）」「刷新商店」 */
  text: string
  state: 'pending' | 'running' | 'done' | 'failed' | 'skipped' | 'cancelled'
  /** 这一步的问题（核对没通过、对不上），没有为 null */
  problem: string | null
  /** 标注图的地址（在执行前的截图上画出会点 / 拖哪里），界面用 <img> 显示；还没截图为 null */
  preview: string | null
  /** 核对结果：true 通过；false 没通过；null 没核对（试运行、跳过、读不出来，如朝向） */
  verified: boolean | null
}

/** 本机接口的状态（任务 392）。api 关着时 address 为 null */
export interface ControlApiStatus {
  /** 如 'http://127.0.0.1:51234'；口令不给界面 */
  address: string | null
  /** 本实例的配置名（默认配置为 'default'），sa 命令行用 --profile 指它 */
  profile: string
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
  /** 助手现在是不是以管理员身份运行（v0.9.1）。Settings.runAsAdmin 打开但用户没同意系统确认时仍是 false */
  elevated: boolean
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

/** 一种商店标记的样式（v0.9.5） */
export interface MarkerStyle {
  /** 关掉就不标这一种 */
  show: boolean
  /** 主色 '#RRGGBB'：框、角标括号、发光、标签底色都从它来（括号提亮、标签字压暗，宿主算） */
  color: string
}

/**
 * 商店标记样式（v0.9.5）。三种标记互不影响：关掉「关键」后关键干员不标（想要不区分就把两种设成同色）。
 * 默认：关键 { true, '#F3B322' }，追踪 { true, '#3CF0C2' }，可抓 { true, '#A98BFF' }（以前可抓和追踪同为薄荷绿、只差虚线，分不清），labels true
 */
export interface MarkerStyles {
  key: MarkerStyle // 目标阵容里标了关键的、优先装备
  tracked: MarkerStyle // 追踪表里的其他
  pickable: MarkerStyle // 目标盟约里还能抓的（次级标记，虚线框）
  /** 框上方的「★ 关键」「★ 追踪」「可抓」文字标签 */
  labels: boolean
}

/** 一局采集（v1.0.5，任务 235） */
export interface CollectionMatch {
  id: string
  startedAt: string // 这局开始的时间，ISO
  ongoing: boolean // 正在打的这局
  mode: string | null // 本局模式名，如「终极模拟」；没读到为 null
  rounds: number // 存到了第几回合
  frames: number // 存了几张画面
  /** 有答案的格子数：玩家改过的、确认过的、详情面板读到的，加「场上都对了」记下的 */
  answers: number
  bytes: number // 占用的磁盘大小
}

/** 自动更新的状态（检查、下载、校验都在宿主里做） */
export interface UpdateStatus {
  state: 'idle' | 'checking' | 'latest' | 'available' | 'downloading' | 'ready' | 'failed'
  version?: string // available 及之后：新版本号
  notesUrl?: string // 新版本的发版页
  /** available 时：新版本 Release 附件里的 release-notes.json；没有这个附件或下载失败就不带，界面只给发版页链接 */
  notes?: ReleaseNotes
  progress?: number // downloading 时 0–1
  // ready：下好了。v1.0.4 起后台下载的不自动重启，等玩家点 applyUpdate
  message?: string // failed 时给用户看的原因
  /** available 时：这次实际从哪下载。updateSource 是 mirrorchyan 但没填 CDK 时为 'github' */
  source?: 'github' | 'mirrorchyan'
  /** 带 CDK 检查且 CDK 有效时：CDK 到期时间（ISO）。latest、available 都可能带 */
  cdkExpiresAt?: string
  /** failed 时：是 CDK 的问题（过期、填错、今日次数用完、类型不对、被封），界面在旁边给「改用 GitHub」 */
  cdkProblem?: boolean
  /** 带 Token 检查、且 GitHub 在响应头里给了到期时间时：Token 到期时间（ISO）。latest、available 都可能带 */
  githubTokenExpiresAt?: string
  /** failed 时：Token 无效或已过期（GitHub 返回 401），界面在 Token 输入框旁边提示换一个 */
  githubTokenProblem?: boolean
  /**
   * failed 时：GitHub 请求额度用完了（v1.0.2）。没填 Token 是每小时 60 次、带 Token 是 5000 次，
   * 界面提示等一会儿或在设置里填 Token
   */
  githubRateLimited?: boolean
}

/** GitHub Token 校验结果（v1.0.2） */
export interface GitHubTokenCheck {
  valid: boolean
  /** 有效且 GitHub 给了到期时间时：ISO 8601；null = 永不过期 */
  expiresAt: string | null
  /** valid 为 false 时给用户看的原因 */
  message: string
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
/**
 * 森空岛攻略列表的查询（v0.9.2，阵容 → 森空岛，见 099）。宿主在隐藏的官方攻略页里调用页面自己的攻略服务，
 * 登录和签名都由官方页面处理；只在用户翻页、搜索、改筛选时调用，一次一页（10 条）
 */
export interface SklandGuideQuery {
  /** all 攻略大全；collected 我的收藏；published 我的发布（后两个要登录） */
  section: 'all' | 'collected' | 'published'
  /** 只对 all 有效：recommended 推荐（默认）/ latest 最新（最新要登录） */
  sort?: 'recommended' | 'latest'
  /** 标题关键词：森空岛按文字模糊匹配，不是干员搜索；空串当没传 */
  keyword?: string
  /** 以下只对 all 有效，用助手自己的编号，宿主换成森空岛的（找不到对应的就当没选）：核心盟约单选；附加盟约多选（都要包含）；策略单选 */
  core?: CovenantId | null
  extras?: CovenantId[]
  strategyId?: string | null
  /** 下一页：上一页返回的 pageToken 原样传回；第一页不传 */
  pageToken?: string
}
export interface SklandGuideItem {
  /** guideId，字符串 */
  id: string
  /** 详情页地址，和 sklandGuide 的入参同一格式 */
  url: string
  title: string
  author: string
  /** 最后编辑时间，ISO */
  editedAt: string
  /** 赛季阶段，如「序列I」 */
  phaseName: string
  stats: { likes: number; collections: number; views: number }
  /** 当前账号收藏过（没登录为 false） */
  collected: boolean
  /** 列表里自带的整篇攻略，导入直接用，不再读详情；数据不完整或攻略已失效时为 null */
  guide: SklandGuide | null
}
export interface SklandGuidePage {
  items: SklandGuideItem[]
  /** 还有下一页时给，原样传回；没有为 null */
  pageToken: string | null
  /**
   * 要登录才能继续（v0.9.2）：没登录时攻略大全只给第一页（pageToken 为 null）、「最新」和个人列表给空列表，这一项为 true。
   * 不为此报错；界面显示登录按钮（sklandLogin）
   */
  loginRequired: boolean
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

/**
 * 场上与整备区（任务 137，设计见 docs/design.md「站位推荐与场上干员识别」）：休整期识别出的每格占用和身份。
 * 有没有人看角标；是谁靠买进时的商店槽位、拖动时详情面板的名字、或用户手动指定。认不出就是 null，不猜。
 * 只在内存里，宿主重启后从空开始（之后看到的人都先是未知）；新局（开局识别）、清空本局时清空
 */
export interface FieldState {
  /** 棋盘识别出的关卡地图 id（如 'm01'）；还没认出过棋盘为 null */
  mapId: string | null
  /** 读到这次状态时的回合；没读到为 null */
  round: number | null
  /** 最近一次变化的时间（ISO） */
  updatedAt: string
  /** 有人的格子，空格不列。顺序：整备区左到右，再棋盘按行、列 */
  units: FieldUnit[]
  /** 最近的事件，新的在前，最多 20 条 */
  log: FieldEvent[]
  /**
   * 本局有的人（v1.0.0）：小人比对只在这里面挑。按得到的先后；卖掉的不删（sold 为 true，比对时排后），
   * 新局、清空本局时清空
   */
  roster: FieldRosterEntry[]
  /**
   * 场上的召唤物（任务 291，如鸟笼）：不是干员，不进 units、不算本局名单；占着格子，站位推荐要避开。
   * 没有这个字段或空数组 = 没认出召唤物（旧宿主同样当作没有）
   */
  summons?: FieldSummon[]
}

export interface FieldSummon extends FieldCell {
  /** 召唤物名（游戏数据里的名字，如「鸟笼」） */
  name: string
  /** 召唤它的干员；推不出来为 null */
  owner: string | null
}

export interface FieldRosterEntry {
  operator: string
  /** purchase 商店买的；strategy 策略送的；equipment 装备给的（简易通讯机等）；dispatch 零费调度挑的；ethan 卖伊桑给的；custom 自选编队选的；manual 玩家确认格子时补进来的 */
  source: 'purchase' | 'strategy' | 'equipment' | 'dispatch' | 'ethan' | 'custom' | 'manual'
  at: string
  sold: boolean
}

/** 格子。整备区 zone='bench'，col 为 1～10（左到右），row 为 0；棋盘 zone='board'，col / row 与敌人路线用的格坐标相同 */
export interface FieldCell {
  zone: 'bench' | 'board'
  col: number
  row: number
}

export interface FieldUnit extends FieldCell {
  /** 头上角标的阶数 1～6 */
  tier: number
  /** 干员名；不知道是谁为 null */
  operator: string | null
  /**
   * 身份从哪来：purchase 买进时的商店卡、drag 拖动时的详情面板、manual 用户指定（含点选确认）；
   * v1.0.0 起另有：carry 从别的格子挪过来跟着走的、merge 三合一后沿用、only 这一阶本局只有这一个人、sprite 小人比对。
   * operator 为 null 时也为 null
   */
  source: 'purchase' | 'drag' | 'manual' | 'carry' | 'merge' | 'only' | 'sprite' | null
  /**
   * 确定程度（v1.0.0，设计见 docs/design.md「场上认人：什么时候让玩家 3 选 1」）：
   * sure = 商店卡、拖动面板、挪格、三合一、唯一、手动这些确定来源；auto = 小人比对把握大、自动填的（玩家可改）；
   * pending = 待玩家确认，operator 暂填第 1 名、candidates 给前 3；unknown = 本局有的人里一个都对不上（operator 为 null）
   */
  status: 'sure' | 'auto' | 'pending' | 'unknown'
  /** auto / pending 时的候选：本局有的人里同阶前 3 名，按把握从大到小（score 只用来排序）；其他情况为空 */
  candidates: { operator: string; score: number }[]
  /**
   * 身上带的装备（v1.0.0）：玩家点开或拖动这个人时，详情面板左侧装备栏读到的装备名，按栏位顺序，空栏不列。
   * 一般干员 2 栏，暴躁铁皮 6 栏。没有这个字段或 null = 还没看到过这个人的详情面板；[] = 看到了，没带装备。
   * 跟着这个人走（挪格、三合一后沿用），这格变空后作废。
   * v1.0.0 起每件带 elite（进阶装备，数值更高）：按面板上这件的效果描述对 equipment.json 的 normal / elite 描述定；对不上为 null
   */
  equipment?: { name: string; elite: boolean | null }[] | null
  /**
   * 是不是精锐（v1.0.0，卫戍协议里三合一后的金色棋子）：三合一事件、买进金卡、详情面板上的精锐标记都能定。
   * 没有这个字段或 null = 不知道（站位推荐按普通算）
   */
  elite?: boolean | null
}

/**
 * 场上发生的事。kind：
 * - buy：商店卡被拿走，整备区多出一人（含免费挑选、打包速递，资金不一定变）；operator 为卡上名字
 * - gain：整备区多出一人，但没有商店卡被拿走（策略或干员特质给的）；operator 一般为 null
 * - move：拖动，from → to；operator 为详情面板上读到的名字
 * - sell：拖进售出区后这人消失
 * - lost：有人消失但没看到拖动或售出（漏看了，或同名合成之类）
 */
export interface FieldEvent {
  at: string
  kind: 'buy' | 'gain' | 'move' | 'sell' | 'lost'
  operator: string | null
  from: FieldCell | null
  to: FieldCell | null
}

/**
 * 小棋盘的地图和本回合路线（v1.0.0，任务 169，设计见 docs/design.md「本局页小棋盘」）。
 * 宿主按 FieldState.mapId 和本局模式、回合、首领选关卡，和覆盖层画路线用同一份数据（garrison_routes.json）；
 * 不管覆盖层的路线开没开都给。还没认出过地图为 null。坐标都是「格坐标」：同 FieldCell 的棋盘 col / row，
 * 格 (c, r) 的中心是 (c + 0.5, r + 0.5)；能放人的格子在列 0～6、行 0～3 里（具体哪格能放看 cells），行 4 整行不能放，
 * 列 −1 是防守点那列，列 7 是出生点那列
 */
export interface BoardView {
  mapId: string
  /** 路线对应的回合；没读到回合为 null，这时 routes、bossSpots 为空 */
  round: number | null
  /** 首领回合（关卡 h07_*、h08_*）：地形用 bossWalkable */
  bossRound: boolean
  /** 单人模式 */
  solo: boolean
  /** 普通回合的可走格（地面敌人能走），5 行 × 12 列：walkable[row][col + 2]，col −2～9、row 0～4 */
  walkable: boolean[][]
  /**
   * 自己棋盘每格能不能放人（v1.0.0），5 个字符串 × 12 个字符：cells[row][col + 2]，col −2～9、row 0～4，
   * 原样取 data/garrison_maps.json 这张图的 cells（任务 170 带进来，来自 CarlosDJY）：
   * 'X' 不能放；'G' 地面（近战、远程都能放）；'H' 高台（只能放远程）；'T' 特殊地面（能放，地图有特殊效果）；
   * 'D' 防守点；'S' 出生点。这张图没有数据时为 null（界面按列 0～6、行 0～3 都能放处理）。首领回合也用这一份
   */
  cells: string[] | null
  /**
   * 首领回合的可走格，两边棋盘连成一块，6 行 × 17 列：bossWalkable[row + 1][i]，row −1～4。
   * i = col + 1 是自己这边（列 −1～7，i 0～8），i 9～16 是另一边（镜像，i = 15 − col），中间 i = 8 是出生点那列
   */
  bossWalkable: boolean[][]
  routes: BoardRoute[]
  /** 不走动的大体型首领站的位置（格中心坐标） */
  bossSpots: { col: number; row: number }[]
  /** 联机首领回合自己在哪一侧：认出来的，或设置里 routes.side 手选的；单人、普通回合、认不出为 null */
  side: 'left' | 'right' | null
}

/** 一条敌人路线（同 garrison_routes.json 里的一条，按本回合挑出来） */
export interface BoardRoute {
  motion: 'walk' | 'fly'
  boss: boolean
  /** 联机首领回合两只首领：'left' / 'right' 是哪一侧的那只（右边那只经过另一边的段已镜像回自己的棋盘）；其他为 null */
  side: 'left' | 'right' | null
  /** 巡逻：在检查点之间来回，不走向防守点 */
  patrol: boolean
  /** 地面路线在有多条等长路时是推算的 */
  approx: boolean
  /** 这条路线的敌人数 */
  count: number
  /** 折线，点是格中心坐标；传送（消失再出现）处断开成几段 */
  segments: { col: number; row: number }[][]
}

/**
 * 站位推荐（152 方案 B，任务 170 接）。场上的人、地图、回合、模式、首领、本回合敌人、盟约层数、我的干员变了时，
 * 宿主防抖约 1 秒后在后台重算，算完推送；算不了（没地图、没回合、场上没人）、新局、清空本局时为 null。
 * 界面拿 basedOn 和 FieldState.updatedAt 比，对不上就标「场上变了，等重新算」
 */
export interface PlacementPlan {
  /** 依据的场上状态（FieldState.updatedAt） */
  basedOn: string
  /** 算完的时间（ISO） */
  computedAt: string
  /** 这次按哪个目标算的（Settings.placementGoal） */
  goal: PlacementGoal
  /** 参与推荐的每个人一条 */
  moves: PlacementMove[]
  /** 其中待确认、按第 1 名算的人数（「有 n 格是猜的」） */
  guessed: number
  /** 认不出是谁、或没有计算资料、没算进去的格子 */
  skipped: FieldCell[]
  /** 照推荐摆的模拟结果（近似）；算不了为 null */
  recommended: PlacementOutcome | null
  /** 不挪人、照现在摆的模拟结果（近似；现在的朝向看不到，由计算挑或按默认）；算不了为 null */
  current: PlacementOutcome | null
  /** 这次用了哪些假设，每条一句话，给玩家看，如「潜能按 1 算」「盟约层数按 0 算」 */
  assumptions: string[]
}

/** 站位推荐的目标：leak 漏怪最少；balanced 平衡（默认）；layers 叠层最多 */
export type PlacementGoal = 'leak' | 'balanced' | 'layers'

/** 一种摆法的模拟结果（近似）：按真实强度 */
export interface PlacementOutcome {
  /** 本回合总敌人数（各种可能的怪组按概率平均，保留一位小数） */
  enemies: number
  /** 预计漏怪数（同上） */
  leaked: number
  /** 每种可能的怪组各自的结果，chance 0～1，按 chance 从大到小 */
  streams: { label: string; chance: number; enemies: number; leaked: number }[]
  /** 这一回合各盟约预计能叠几层，没有的不列 */
  layers: { covenant: CovenantId; layers: number }[]
}

export interface PlacementMove {
  operator: string
  /** 现在在哪 */
  from: FieldCell
  /** 推荐站哪；留在或下到整备区的 to.zone = 'bench'（原来在整备区的 to = from） */
  to: FieldCell
  /** 上场的朝向；整备区为 null */
  facing: 'up' | 'down' | 'left' | 'right' | null
}

/**
 * 我的干员（任务 162、163，设计见 docs/design.md「我的干员：读物资调配页」）：从卫戍协议「物资调配」页读到的
 * 每名干员的当前配置。宿主存在数据文件夹（roster.json），重启后还在；不进云同步、不进备份（重新读一遍就有）。
 * 用途：小人比对按皮肤选模板；站位推荐和伤害计算要技能、模组、潜能、等级；自选编队缩小自选候选
 */
export interface Roster {
  /** 最近一次读取结束的时间（ISO）；没读过为 null */
  readAt: string | null
  /** 读到过的干员，按物资调配页列表的顺序；同一名干员只有一条，以最近一次读到的为准 */
  operators: RosterOperator[]
  /**
   * 自选编队（页顶「自选编队 n/12」）：列表里带橙框和「+」的自选干员名，按列表顺序。
   * 这次读取滑到头了就整体换成这次看到的；中途停下的只把看到的加进去。没读过为 []
   */
  customSquad: string[]
}

export interface RosterOperator {
  /** 干员名（Operator.name） */
  name: string
  /**
   * 卡面头像对应的皮肤：Operator.skins 的 id（游戏 skin_table 的 skinId，如默认 'char_1012_skadi2#1'、
   * 精二 'char_1012_skadi2#2'、时装 'char_1012_skadi2@boc#4'）。认不出为 null
   */
  skin: string | null
  /** 打勾的技能（Operator.skills 的 id）；认不出为 null */
  skill: string | null
  /** 打勾的模组（Operator.modules 的 id）；'' = 没有选模组（含这名干员没有模组）；认不出为 null */
  module: string | null
  /**
   * 潜能 1～6；认不出为 null。卡上的星形图标潜能 n 有 n−1 笔（最新一笔蓝色），满潜五笔全白，潜能 1 没有图标。
   * 卡上竖着的两个等级数字是模式定死的（用户 2026-11-15 确认），不读
   */
  potential: number | null
  /**
   * 原型干员（任务 279）：玩家没有这名干员时，物资调配页上这张卡显示成游戏的默认干员（如 'Sharp'），
   * 但盟约、技能图标仍是原干员的；读到这种卡时按阶数、位置和盟约图标反推出原干员，这里记顶替它的默认干员名。
   * 场上这名干员长的是默认干员的样子，小人比对用它的模板。null 或没有这个字段 = 自己有这名干员（或旧数据）
   */
  prototype?: string | null
  /** 这一条最近一次读到的时间（ISO）；用户手动改过也更新 */
  readAt: string
  /** 用户手动改过的项；再读一遍时这些项照样用读到的覆盖，并从这里去掉 */
  edited?: ('skin' | 'skill' | 'module' | 'potential')[]
}

/** 「读取我的干员」的进度（任务 163），开始、每读完一屏、结束时推送 */
export interface RosterScanStatus {
  running: boolean
  /** 这次读到的干员数（去重） */
  read: number
  /**
   * 结束原因，running 为 false 时才有：end 滑到头了；user 点了停止；left 画面不是物资调配页了；
   * input 用户动了鼠标或按了键；lost 游戏窗口丢了；failed 出错
   */
  reason?: 'end' | 'user' | 'left' | 'input' | 'lost' | 'failed'
  /** 结束时给用户看的一句话；failed 时第二行起是技术细节（同消息桥的错误约定） */
  message?: string
}

export interface Api {
  /** 当前配置名，默认配置为 null。 */
  currentProfile(): Promise<string | null>
  /** 列出已有具名配置，默认配置由菜单单列。 */
  listProfiles(): Promise<string[]>
  /** 从默认设置创建，初始快捷键关闭。名称限 1–24 个英文字母、数字或短横线，统一小写；重名或 Windows 保留名返回错误。 */
  createProfile(name: string): Promise<void>
  /** 启动或激活配置，null 表示默认配置。 */
  launchProfile(name: string | null): Promise<void>
  catalog(): Promise<Catalog>
  connection(): Promise<Connection>
  match(): Promise<MatchInfo>
  /** 当前场上与整备区（任务 137）；还没识别过为 null */
  field(): Promise<FieldState | null>
  /**
   * 手动指定某格是谁（operator 为 null = 改回未知），source 记为 manual。这格的人被拖走后身份跟着走；
   * 这格变空后作废。返回更新后的状态，也会推送 field。
   * 任务 278 起空格子也能指定（手动加人，阶数按干员表）：识别看不到这格时也保留，看到别人才换；
   * 对只有手动加、识别从没看到的格子传 null 就是移除
   */
  setFieldOperator(cell: FieldCell, operator: string | null): Promise<FieldState>
  /**
   * 现在认一次场上（v1.0.1）：拿当前画面读角标、认人（沿用本局有的人、确认过的人、个人模板），然后算一次站位推荐。
   * 自动识别开着关着都能用。返回认出的场上状态，也推送 field 和 placement。
   * 任务 272 起名字先出、站位后到：推送 field 后就返回，站位推荐在后台算，算好再推送 placement（可能晚一两秒）。
   * 不在休整期的棋盘主画面（战斗中、面板盖住、没连上游戏）时以错误返回，错误文字直接给用户看，
   * 如「现在不在休整期的棋盘画面，先关掉干员详情等面板、回到棋盘再点」
   */
  recognizeField(): Promise<FieldState>
  /**
   * 「场上都对了」（v1.0.5，任务 235）：把现在的场上和整备区名单连同这一帧记成这局的答案（玩家先改掉认错的再点）。
   * 要开着 collect、在休整期的棋盘主画面；不满足时以错误返回，错误文字直接给用户看，如「先打开设置里的「帮助改进识别」」
   * 「现在不在休整期的棋盘画面，先关掉干员详情等面板、回到棋盘再点」。待确认的格子也按现在显示的名字记
   */
  confirmField(): Promise<void>
  /** 已存的采集对局（v1.0.5），新的在前，最多 3 局；正在打的这局也在（ongoing: true） */
  collections(): Promise<CollectionMatch[]>
  /**
   * 把一局采集导出成 zip（v1.0.5）：弹保存对话框，默认放桌面，文件名「高效雅雅-采集-yyyyMMdd-HHmm.zip」（这局开始的时间），
   * 返回保存的完整路径；玩家取消返回 null。正在打的这局也能导出（导出到此刻为止的）
   */
  exportCollection(id: string): Promise<string | null>
  /** 删掉全部已存的采集（v1.0.5）；正在打的这局从此刻重新开始存 */
  clearCollections(): Promise<void>
  /** 小棋盘的地图和本回合路线（任务 169）；还没认出地图为 null */
  board(): Promise<BoardView | null>
  /** 站位推荐（任务 169 只定接口，还没接）：现在总是 null */
  placement(): Promise<PlacementPlan | null>
  /** 立即识别当前画面的开局信息页；失败时以错误返回，错误文字直接给用户看 */
  rescan(): Promise<MatchInfo>
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
   * 手动补上 / 去掉本回合的一个敌人（任务 200），只改 economy.round 这一回合，规则见 RoundEnemies：
   * present=true：不在名单里就加在最后（source 'manual'）；在 removed 里就从 removed 拿回原位置。
   * present=false：读到的移进 removed，补上的直接删。
   * roundEnemies 是上一回合的或为 null 时，先换成这一回合的空名单再改。economy 为 null（不知道第几回合）时以错误返回。
   * name 用 Catalog.enemies 的名字。更新 roundEnemies.at，推送 match，站位推荐按新名单重算
   */
  setRoundEnemy(name: string, present: boolean): Promise<MatchInfo>
  /** 本回合可能出现的敌人（任务 200），见 RoundEnemyOptions；不知道回合、没有这回合的怪组数据时为 null */
  roundEnemyOptions(): Promise<RoundEnemyOptions | null>
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
  /**
   * 校验填的 GitHub Token（v1.0.2）：有效时返回到期时间（永不过期时 expiresAt 为 null），
   * 无效时 message 直接给用户看。检查更新本身也会顺带校验，这里给用户在输入框旁边即时反馈
   */
  validateGithubToken(token: string): Promise<GitHubTokenCheck>
  /** 玩家点「更新」：没下载就下载并校验，已经下好就直接装；装的时候宿主退出、替换文件、重新启动。失败时推送 failed */
  applyUpdate(): Promise<void>
  /** 后台下载并校验新版本（v1.0.4，开着自动检查更新时发现新版就调）：下好推送 ready，不重启，等玩家点 applyUpdate */
  downloadUpdate(): Promise<void>
  /** 用资源管理器打开数据文件夹 */
  openDataFolder(): Promise<void>
  /** 列出正在运行的模拟器实例（MuMu 和雷电，v0.9.4 前叫 mumuInstances），设置里多开时让用户选；没有就是空数组 */
  emulatorInstances(): Promise<EmulatorInstance[]>
  /**
   * 弹出系统保存对话框导出备份（.json：我的阵容、特别关注、偏好设置；不含本局状态、窗口位置）。
   * 默认文件名「高效雅雅-备份-YYYY-MM-DD.json」。用户取消返回 null，否则返回保存的完整路径。
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
  /**
   * 本机装的字体族名（v0.9.5），设置里选界面字体用。去重、按名字排序；有中文本地化名的给中文名
   * （如「微软雅黑」而不是 Microsoft YaHei），写法要能直接放进 CSS font-family。不含纵排用的「@」字体
   */
  listFonts(): Promise<string[]>
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
   * 森空岛攻略列表的一页（v0.9.2，见 SklandGuideQuery 和 099）。界面丢弃过期的结果，宿主不用排队取消；
   * 打不开、被限流、数据结构变了时 reject，message 是给用户看的中文
   */
  sklandGuides(q: SklandGuideQuery): Promise<SklandGuidePage>
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
   * 登录森空岛（v0.9.0，设置「森空岛账号」里的「登录」）：已经登录就直接返回角色（顺便刷新记下的昵称），不弹窗；
   * 没登录时弹出看得见的森空岛小窗口（和 sklandGuide / sklandHistory 共用登录状态），用户在里面登录，
   * 宿主认出已登录、读到角色后关窗返回 { nickname } 并记下。用户关掉窗口返回 null。不读攻略、不读历史战绩
   */
  sklandLogin(): Promise<{ nickname: string } | null>
  /**
   * 改收起 / 展开快捷键（v0.7.0）：先注册新组合，成功才保存进 Settings.toggleHotkey 并注销旧的；'' 为关闭。
   * 被占用或写法不对时 reject，message 给用户看（如「这个组合被别的软件占用了，换一个试试。」），旧的照旧生效
   */
  setHotkey(combo: string): Promise<void>
  /**
   * 打开 / 关闭「以管理员身份运行」（v0.9.1）。打开：保存后以管理员身份重启助手（弹系统确认）；用户不同意时不保存、
   * reject，message 给用户看（「没有同意管理员授权，助手还是普通身份运行。」）。关闭：保存，重启成普通身份；做不到就下次启动生效。
   * 重启时窗口会关掉重开，界面不用另做处理
   */
  setRunAsAdmin(on: boolean): Promise<void>
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
  /** 后端推送：用户按了「识别场上」快捷键（Settings.fieldHotkey，v1.0.1）。界面收到后调 recognizeField；宿主不激活助手窗口 */
  onFieldHotkey(cb: () => void): () => void
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
   * redock（v0.8.2）：清掉跟随、覆盖时拖动记下的位置（四个偏移都清），侧栏马上贴回默认位置。设置里点「贴在游戏」「盖在画面」的选项时发
   * maximize / restore（v0.9.8）：工作台最大化（铺满当前屏幕的工作区，不盖任务栏）/ 还原。只在工作台里发；
   * 最大化时收回侧栏先还原，侧栏回原来的位置；下次进工作台还是最大化（记在工作台位置里）
   */
  windowCommand(cmd: 'minimize' | 'close' | 'collapse' | 'expand' | 'ready' | 'workbench' | 'sidebar' | 'redock' | 'maximize' | 'restore'): Promise<void>
  /**
   * 拖边改大小（工作台 v0.9.8；侧栏 v1.0.0）：界面在窗口四边、四角画了透明的拖动区，按下鼠标时发，
   * 宿主接着按系统的方式拖这条边或这个角（鼠标还按着）。工作台最大化时、侧栏收起时界面不发。
   * 工作台最小尺寸照 windowCommand('workbench') 说明；侧栏宽 320～720、高至少 480（DIP × 界面缩放），
   * 拖完记住，贴着游戏、盖在画面、自由放置各记各的，见任务 165
   */
  resizeWindow(edge: ResizeEdge): Promise<void>
  /** 后端推送：工作台最大化 / 还原（v0.9.8，含双击标题栏、Win+↑、拖标题栏离开最大化）；进工作台时也推一次当前状态 */
  onMaximized(cb: (maximized: boolean) => void): () => void
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
  /** 后端推送：场上与整备区有变化（任务 137）；同样的状态不重复推 */
  onField(cb: (f: FieldState) => void): () => void
  /** 后端推送：小棋盘的地图或本回合路线变了（认出新地图、换回合、首领、单人联机、自己在哪一侧变了）；同样的不重复推 */
  onBoard(cb: (b: BoardView | null) => void): () => void
  /** 后端推送：站位推荐变了（任务 169 只定接口，还没接，现在不推） */
  onPlacement(cb: (p: PlacementPlan | null) => void): () => void
  /** 我的干员（任务 163）；没读过是 { readAt: null, operators: [], customSquad: [] } */
  roster(): Promise<Roster>
  /**
   * 开始「读取我的干员」（任务 163）：先截一张，认不出是物资调配页就 reject（message 给用户看，如
   * 「先在游戏里打开卫戍协议的「物资调配」页，再点读取。」）。游戏是管理员身份、助手不是时也 reject
   * （「游戏是以管理员身份运行的，助手也要以管理员身份运行才能滑动列表。」，162 实测这时发不进操作）。认出了就 resolve，之后在后台：
   * 排序是「按盟约」时先点一下右上角的等阶图标切到「按等阶」（列表短一半，用户 2026-11-15 同意助手点这一下），
   * 再把列表滑到最左，一屏一屏往右滑、截图、识别，读到的随时并进 Roster 并推送 roster、rosterScan。
   * 除了这一下排序，只在列表区域横向滑动，不点任何卡片和按钮。滑到头、画面离开这一页、用户动了鼠标或按键、游戏窗口丢了就停，
   * 推送 rosterScan { running: false, reason, message }。已经在读时再调直接 resolve
   */
  startRosterScan(): Promise<void>
  /** 停止读取（任务 163）；没在读时什么也不做。停下后推送 rosterScan { running: false, reason: 'user' } */
  stopRosterScan(): Promise<void>
  /**
   * 手动改一名干员（任务 163，图鉴详情里）：改的项写进 edited，readAt 改成现在；Roster 里还没有这名干员就新建一条
   * （没给的项为 null）。返回更新后的 Roster，也推送 roster
   */
  setRosterOperator(name: string, patch: Partial<Pick<RosterOperator, 'skin' | 'skill' | 'module' | 'potential'>>): Promise<Roster>
  /** 后端推送：我的干员有变化（读取中每读完一屏、手动改了之后） */
  onRoster(cb: (r: Roster) => void): () => void
  /** 后端推送：读取我的干员的进度 */
  onRosterScan(cb: (s: RosterScanStatus) => void): () => void
  /**
   * 下发一批操作（任务 391）。要 settings.control.enabled；mode 不给就用设置里的，给了取它和设置里更保守的那个。
   * 已有一批在跑时 reject（「上一批操作还没做完」）。立刻 resolve 返回刚建的 ControlJob，之后进度推送 controlJob。
   * PC 客户端且模式不是 dryRun 时，宿主开始前把游戏切到前台
   */
  runActions(actions: GameAction[], mode?: ControlMode): Promise<ControlJob>
  /**
   * 照站位推荐在游戏里摆（任务 392）：宿主取当前 PlacementPlan，basedOn 和当前 FieldState.updatedAt 对不上时 reject
   * （「场上变了，等重新算完再摆」）；没有推荐、不用挪人时 reject。把 from ≠ to 的人排好顺序转成 move 动作
   * （PlacementActions，任务 390），其余同 runActions
   */
  runPlacement(mode?: ControlMode): Promise<ControlJob>
  /** 逐步确认模式下回答当前这一步：run 执行；skip 跳过这一步继续下一步。不在 waitingConfirm 时什么也不做 */
  confirmStep(jobId: string, answer: 'run' | 'skip'): Promise<void>
  /** 停止当前这批（reason 'user'）；没在跑时什么也不做 */
  stopControl(): Promise<void>
  /** 当前或最近一批（重启后为 null） */
  controlJob(): Promise<ControlJob | null>
  /** 后端推送：批次状态或任意一步有变化 */
  onControlJob(cb: (job: ControlJob) => void): () => void
  /** 本机接口状态（任务 392） */
  controlApi(): Promise<ControlApiStatus>
}
