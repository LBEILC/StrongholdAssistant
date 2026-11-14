// 阵容广场的接口定义（设计定义，工程照着在 Pages Functions 里实现）。
// 网站（src/plaza/site）和助手界面（src/ui）都按这里的类型调用。
//
// 约定：
// - 路径都在同一个域名下的 /api/ 里，JSON 进出，时间是 ISO 字符串。
// - 登录凭据：助手放在请求头 `Authorization: Bearer <token>`；网站用 HttpOnly Cookie（`sa_session`），两种都认。
// - 出错时 HTTP 状态码非 2xx，响应体是 ApiError；message 是直接给用户看的中文。
// - 需要登录的接口没登录返回 401，被封禁返回 403。

import type { CovenantGoal, Lineup, LineupMember, LineupStage, Settings } from '../../ui/src/lib/api/types'

export interface ApiError {
  /** 机器可读的错误码，例如 'rate_limited'、'bad_code'、'season_mismatch'、'nickname_taken'、'blocked_words' */
  error: string
  message: string
}

// ── 账号 ─────────────────────────────────────────────

/** POST /api/auth/code：给邮箱发 6 位验证码。同一邮箱 60 秒内只能发一次、每天最多 10 次，同一 IP 每天最多 30 次 */
export interface SendCodeRequest {
  email: string
  /** Cloudflare Turnstile 的令牌（网站和助手都用同一个站点密钥的小部件取得） */
  turnstileToken: string
}
export interface SendCodeResponse {
  /** 多少秒后可以再发 */
  retryAfter: number
}

/** POST /api/auth/verify：验证码 10 分钟有效，同一个码最多试 5 次。邮箱第一次登录时自动建账号 */
export interface VerifyRequest {
  email: string
  code: string
}
export interface VerifyResponse {
  /** 登录凭据，30 天有效；每次使用时离上次续期超过 1 天就顺延到 30 天后。网站同时收到 Set-Cookie */
  token: string
  me: Me
}

/** POST /api/auth/logout：让当前凭据失效。无响应体 */

/** GET /api/me：当前账号 */
export interface Me {
  id: string
  /** 新账号为 null：界面先让用户起昵称，起好之前不能发布、点赞 */
  nickname: string | null
  /** 只给本人看，例如 "ab***@qq.com" */
  emailMasked: string
  createdAt: string
  /** 设了登录密码（可选，v0.6）；没设只能用验证码登录 */
  hasPassword: boolean
}

/**
 * POST /api/auth/password：用密码登录。同样要过 Turnstile。没有这个账号、没设密码、密码不对都返回 401 + `bad_credentials`
 * 「邮箱或密码不对。」，不区分。同一邮箱连续输错 5 次锁 15 分钟、同一 IP 每天最多 30 次，超了返回 429 + `rate_limited`。
 * 成功返回 VerifyResponse，和验证码登录一样
 */
export interface PasswordLoginRequest {
  email: string
  password: string
  turnstileToken: string
}

/**
 * PUT /api/me/password：设置或修改密码，返回最新的 Me。DELETE /api/me/password（请求体同样可带 currentPassword）：删除密码，无响应体（204）。
 * 已有密码时要带 currentPassword；例外：当前登录是 30 分钟内用验证码登录的（忘了密码用验证码登录后可以直接重设）。
 * 密码 8～64 位、不能和邮箱相同（不分大小写），否则 400 + `weak_password`。currentPassword 不对 403 + `bad_credentials`。
 * 设置、修改、删除后，这个账号其他的登录凭据全部失效，当前这个保留
 */
export interface SetPasswordRequest {
  password: string
  currentPassword?: string
}

/** PATCH /api/me：改昵称。2～16 个字，全站唯一（不分大小写），过敏感词 */
export interface UpdateMeRequest {
  nickname: string
}

/** DELETE /api/me：注销。删除邮箱、昵称和这个账号发的全部帖子、点赞、评论、举报，凭据全部失效。无响应体 */

// ── 帖子 ─────────────────────────────────────────────

/** 帖子里的阵容：发布时服务端用当期数据解码分享码得到，存下来，换赛季后也能照常显示 */
export interface PostLineup {
  name: string
  goals: CovenantGoal[]
  /** 成员的精锐、技能、模组（elite / skill / module，v0.7.0）原样保留；广场目录要带每名干员的技能、模组 id 才解得出 */
  members: LineupMember[]
  equipment: string[]
  strategyId: string | null
  /** 回合阶段（SA3，v0.7.0）；没有就不写。顶层的 goals / members / equipment 是成型阵容 */
  stages?: LineupStage[]
}

export interface Post {
  id: string
  /** 分享码（SA1-…），导入就用它 */
  code: string
  lineup: PostLineup
  /** 简介，纯文本，最多 500 字，可以有换行 */
  description: string
  author: { id: string; nickname: string }
  createdAt: string
  updatedAt: string
  featured: boolean
  likes: number
  imports: number
  /** 可见评论数（第二期） */
  comments: number
  /** 当前账号点过赞（没登录为 false） */
  liked: boolean
  /** 当前账号收藏了（没登录为 false，v0.7.0） */
  favorited: boolean
  /** 分享码的数据指纹和当期一致：false 表示旧赛季的帖子，只能看、不能导入 */
  current: boolean
}

/**
 * GET /api/posts：列表。默认只列当期（current）且可见的帖子。
 * 查询参数：sort = 'featured' | 'new' | 'hot'（默认 'hot'：点赞 × 2 + 导入数，相同按新到旧）；
 * covenant（盟约 id，可重复，要求目标盟约全部包含）；strategy（策略 id）；author（账号 id，看某人发的，含旧赛季）；
 * season = 'current'（默认）| 'all'；cursor（上一页返回的）；limit（默认 20，最多 50）。
 * q（v0.9.1，搜索）：去掉首尾空格后最多 40 字，按空格分成最多 3 个词，每个词都要命中下面任意一项（不分大小写）：
 * 阵容名、简介、作者昵称、任一阶段里的干员名、装备名、目标盟约名；空串当没传。可以和其他筛选、排序、收藏一起用。
 * favorite = '1'（v0.7.0）：只列当前账号收藏的（要登录，没登录 401），含旧赛季，按收藏时间新到旧，忽略 sort 和 season。
 *
 * GET /api/public/posts（v0.9.3）：同样的列表，但永远按没登录处理（不看凭据和 Cookie，liked、favorited 都是 false），
 * 返回 Cache-Control: public, max-age=0, s-maxage=120，让 ESA 边缘缓存。参数同上，但不支持 favorite、author（传了返回 400）。
 * 助手和网站在没登录时、看普通列表时用它；登录后照旧用 /api/posts（要显示自己点过的赞、收藏）
 */
export interface PostList {
  posts: Post[]
  /** 没有下一页为 null */
  cursor: string | null
}

/** GET /api/posts/:id：单个帖子。已删除、已下架的返回 404（作者本人能看到下架的，带 hidden: true） */
export type PostDetail = Post & { hidden?: boolean }

/**
 * POST /api/posts：发布（要登录、有昵称）。服务端解码分享码，指纹不是当期的拒绝（season_mismatch）；
 * 阵容名和简介过敏感词（blocked_words）；每个账号每天最多发 10 个（rate_limited）。
 * 同一账号发同一个分享码时不新建，返回原来的帖子并更新简介
 */
export interface CreatePostRequest {
  code: string
  description: string
}

/**
 * PATCH /api/posts/:id：作者修改帖子，返回最新的 Post。DELETE /api/posts/:id：作者删除。
 * 只带 description 就只改简介（和原来一样）。带 code（v0.7.4）就连阵容一起换：校验和发布一样
 * （分享码格式、当期指纹 season_mismatch、阵容名和简介敏感词 blocked_words）；点赞、收藏、导入数、评论都保留。
 * 换成的分享码和自己另一个帖子相同时 409 + `duplicate`「你已经发过一模一样的阵容。」。不计入每天 10 个的发布次数
 */
export interface UpdatePostRequest {
  description: string
  code?: string
}

/** POST /api/posts/:id/like、DELETE /api/posts/:id/like：点赞 / 取消（要登录、有昵称）。返回最新的 { likes, liked } */
export interface LikeResponse {
  likes: number
  liked: boolean
}

/**
 * POST /api/posts/:id/import：记一次导入（不用登录）。device 是助手或网站本地生成并长期保存的随机 id，
 * 同一 device 或同一当天来源对同一帖子只算一次。返回最新的 { imports }
 */
export interface ImportRequest {
  device: string
}

/**
 * POST /api/posts/:id/favorite、DELETE /api/posts/:id/favorite：收藏 / 取消（v0.7.0，要登录，不要求昵称）。
 * 收藏是书签：帖子之后被作者改了，看到的也是改后的；帖子删除或下架后从收藏列表里消失。每个账号最多收藏 500 个（rate_limited）。
 * 返回最新的 { favorited }
 */
export interface FavoriteResponse {
  favorited: boolean
}

// ── 评论（第二期）────────────────────────────────────
// 只有一层：回复别人的评论也排在同一个列表里，显示「回复 某某」。帖子下架或删除后评论跟着看不到。

export interface Comment {
  id: string
  postId: string
  author: { id: string; nickname: string }
  /** 纯文本，1～300 字（按字符计），可以有换行 */
  body: string
  /** 回复的哪条评论；那条后来被删了也照样显示名字 */
  replyTo: { id: string; nickname: string } | null
  createdAt: string
  /** 当前账号能删：自己写的，或者评论在自己的帖子下面 */
  canDelete: boolean
}

/** GET /api/posts/:id/comments：新到旧。cursor、limit（默认 20，最多 50）。不用登录；帖子不可见时 404 */
export interface CommentList {
  comments: Comment[]
  cursor: string | null
}

/**
 * POST /api/posts/:id/comments：发评论（要登录、有昵称），返回新建的 Comment。旧赛季的帖子也能评论。
 * 过敏感词（blocked_words）；每个账号每天（北京时间）最多 30 条、两条之间至少隔 10 秒（rate_limited）；
 * replyTo 必须是同一帖子下可见的评论，否则 400 + `bad_reply`
 */
export interface CreateCommentRequest {
  body: string
  replyTo?: string
}

/** DELETE /api/comments/:id：评论作者或帖子作者删除，无响应体（204）；别人删 403 */

// ── 站内消息（v0.9.3）────────────────────────────────
// 别人对「我」做了什么：评论我的阵容、回复我的评论、收藏、点赞，加上管理员的处理。自己对自己做的不发。
// 收藏会让作者看到收藏者的昵称（隐私说明里写明）。只留最近 90 天。

export type NoticeKind =
  /** 有人评论了我的阵容（回复我的评论时只发 reply，不再重复发 comment） */
  | 'comment'
  /** 有人回复了我的评论 */
  | 'reply'
  /** 有人收藏了我的阵容；还没读就取消收藏的，这条消失 */
  | 'favorite'
  /** 我的阵容被点赞：同一个阵容在没读之前的赞合并成一条，读过以后再有人赞另起一条；还没读就取消的从合并里去掉 */
  | 'like'
  /** 我的阵容被设为精选 */
  | 'featured'
  /** 我的阵容被管理员下架（含处理举报时下架） */
  | 'post_hidden'
  /** 我的评论被管理员下架 */
  | 'comment_hidden'

export interface Notice {
  id: string
  kind: NoticeKind
  /** 最近一次事件的时间（合并的点赞取最后一个赞的时间）；列表按它新到旧 */
  createdAt: string
  read: boolean
  /** 相关的阵容；阵容被作者删除后，除了 post_hidden 的消息都不再出现 */
  post: { id: string; name: string }
  /** 做这件事的人，新到旧；点赞最多给 3 个，其他种类 1 个；管理员的消息为空数组。收藏不要求昵称，没起昵称的人 nickname 为空串（界面显示「一位玩家」） */
  actors: { id: string; nickname: string }[]
  /** 一共几个人（点赞合并时大于 actors 的长度；其他种类和 actors 一样） */
  actorCount: number
  /** comment、reply：新评论（前 100 字）；comment_hidden：被下架的评论（前 100 字）。评论被删或下架后 comment / reply 消息不再出现 */
  comment?: { id: string; body: string }
}

/** GET /api/notices（要登录，不要求昵称）：新到旧，cursor、limit（默认 20，最多 50）。只读不改已读状态 */
export interface NoticeList {
  notices: Notice[]
  cursor: string | null
  /** 未读总数 */
  unread: number
}
/** GET /api/notices/unread（要登录）：轻量轮询用，可以每几分钟调一次 */
export interface NoticeUnread {
  unread: number
}
/** POST /api/notices/read（要登录）：把 before 及更早的全部标成已读（打开消息列表时传列表第一条的 createdAt），无响应体（204） */
export interface ReadNoticesRequest {
  before: string
}

// ── 举报（第二期）────────────────────────────────────

export type ReportReason = 'spam' | 'offensive' | 'unrelated' | 'broken' | 'other'
/** 界面上的说法；broken 只用于帖子 */
export const REPORT_REASONS: Record<ReportReason, string> = {
  spam: '广告、引流',
  offensive: '辱骂或不当内容',
  unrelated: '和卫戍协议无关',
  broken: '阵容有误、导入不了',
  other: '其他',
}

/**
 * POST /api/reports：举报帖子或评论（要登录，不要求昵称），无响应体（204）。
 * 同一账号对同一目标只算一次，再举报就更新理由和说明；每个账号每天最多 20 次（rate_limited）；
 * 目标不可见 404；评论选 broken 400 + `bad_reason`。举报不会自动下架，只进后台
 */
export interface CreateReportRequest {
  target: ReportTarget
  reason: ReportReason
  /** 选填，最多 200 字 */
  detail?: string
}
export interface ReportTarget {
  /** rating：举报评分的短评（任务 131），id 是 RatingComment.id；没写短评的评分不能举报 */
  kind: 'post' | 'comment' | 'rating'
  id: string
}

// ── 云同步（v0.7.0）──────────────────────────────────
// 设计见 docs/design.md「账号云同步与网站联动」。助手和网站「我的阵容」都用这一个接口，按整套阵容存 JSON，不拆字段。
// 冲突按每套阵容的 updatedAt：新的为准；推上来的比云端旧时不收，把云端那份放进 conflicts 还给客户端，
// 客户端自己决定（两边都改过时把本地那份另存成「（冲突副本）」）。删除是一条 deleted: true 的记录（墓碑），保留 90 天。

/** 云端的一套阵容 */
export interface CloudLineup {
  /** 和本地阵容 id 相同 */
  id: string
  /** 整套阵容（含阶段、简介、候选装备、来源）；deleted 时为 null */
  data: Lineup | null
  /** 客户端改这套阵容的时间（阵容的 updatedAt；删除时为删除时间） */
  updatedAt: string
  deleted: boolean
}

/** 同步的偏好（只这几项；其余设置是这台电脑自己的） */
export type CloudPrefsData = Pick<Settings, 'focusOperators' | 'focusCovenants' | 'gameTheme' | 'shopMarkers' | 'operatorDefaults'>
export interface CloudPrefs {
  data: CloudPrefsData
  updatedAt: string
}

/**
 * POST /api/sync（要登录，不要求昵称）：一次请求同时上传和下载。
 * - lineups：本地自上次同步以来改过、删过的；服务端逐条比 updatedAt；非删除记录若云端在 since 之后改变且 updatedAt 不同则返回冲突，since 为 null 时已有且时间不同也冲突。删除仅对已有 id 按 updatedAt 比较。
 * - prefs：本地改过才带；同样比 updatedAt。
 * - since：上次同步返回的 now；第一次同步（或网站打开「我的阵容」）为 null，返回全部（不含墓碑）。
 * 限制：每个账号最多 300 套阵容（不算墓碑，超了 400 + `too_many`），单套 JSON 最多 64 KB（400 + `too_large`），
 * 请求体最多 2 MB、lineups 最多 400 条、id 最长 64（超限 too_large），id 仅允许字母、数字、下划线和连字符。
 * 每分钟最多 30 次（rate_limited）。注销账号时云端数据一起删除
 */
export interface SyncRequest {
  since: string | null
  lineups: CloudLineup[]
  prefs?: CloudPrefs
}
export interface SyncResponse {
  /** since 之后云端变了的（别的设备推的、墓碑），不含这次被收下的 */
  lineups: CloudLineup[]
  /** 这次推上来但因云端时间更新或 since 后发生修改而没收下的：给的是云端那份 */
  conflicts: CloudLineup[]
  /** 云端的偏好比这次推上来的新、或 since 之后变过时给出，否则为 null */
  prefs: CloudPrefs | null
  /** 服务端时间，下次当 since 用 */
  now: string
}

// ── 大家的资金表（v0.7.0）────────────────────────────
// 设计见 docs/design.md「回合与资金」。助手宿主上报和下载，不带账号、不带广场凭据。

/** 一局里一个回合的起始资金 */
export interface IncomeSample {
  round: number
  /** 起始资金减去当回合已生效的、用户点上的收入 */
  startMoney: number
}

/**
 * POST /api/income（不用登录）：每局结束上报一次，无响应体（204）。
 * 服务端校验：mode 是当期数据里的模式 id，round 1～30，startMoney 0～200，samples 最多 30 条；
 * 同一 device 每天最多 20 次、同一 IP 每天最多 60 次（rate_limited）。用了改经济规则的策略的局助手不报，服务端也按 strategyId 再挡一遍
 */
export interface IncomeReport {
  /** 本机随机编号（设置里能重置），只用来数「几个人报过」 */
  device: string
  /** MatchInfo.mode.id，如 'mode_single_16'、'mode_multi_hard' */
  mode: string
  solo: boolean
  strategyId: string | null
  /** 数据指纹（和分享码同一个），换赛季的数据分开算 */
  fingerprint: number
  samples: IncomeSample[]
}

/**
 * GET /api/income-table（不用登录，可缓存 1 小时）：当期指纹的资金表。每组（mode + solo + round）
 * 取「至少 3 个不同 device 且至少 3 个不同来源报过的最小值」，不够 3 个的组不给。一天汇总一次
 */
export interface IncomeTable {
  fingerprint: number
  /** 汇总时间 */
  updatedAt: string
  rows: { mode: string; solo: boolean; round: number; base: number }[]
}

// ── 后台（/api/admin/*，只认 Cloudflare Access 放行、且邮箱在 ADMIN_EMAILS 里的请求）────

/** GET /api/admin/posts：全部帖子（含下架、删除），新到旧；参数 status = 'visible' | 'hidden' | 'deleted'，cursor，limit */
export type AdminPost = PostDetail & { status: 'visible' | 'hidden' | 'deleted'; authorEmailMasked: string }
export interface AdminPostList {
  posts: AdminPost[]
  cursor: string | null
}

/** PATCH /api/admin/posts/:id：下架 / 恢复 / 删除、设精选 */
export interface AdminUpdatePost {
  status?: 'visible' | 'hidden' | 'deleted'
  featured?: boolean
}

/** GET /api/admin/users?q=昵称或邮箱片段：查账号 */
export interface AdminUser {
  id: string
  nickname: string | null
  emailMasked: string
  createdAt: string
  banned: boolean
  posts: number
  hasPassword: boolean
}

/** PATCH /api/admin/users/:id：封禁 / 解封。封禁后不能登录、发布、点赞、评论、举报，他的帖子和评论全部下架（解封不自动恢复） */
export interface AdminUpdateUser {
  banned: boolean
}

/** 后台看到的评论：带状态、所在帖子的阵容名、作者打码邮箱 */
export type AdminComment = Omit<Comment, 'canDelete'> & {
  status: 'visible' | 'hidden' | 'deleted'
  postTitle: string
  authorEmailMasked: string
}
/** GET /api/admin/comments：全部评论（含下架、删除），新到旧；参数 status、post（帖子 id）、cursor、limit */
export interface AdminCommentList {
  comments: AdminComment[]
  cursor: string | null
}
/** PATCH /api/admin/comments/:id：下架 / 恢复 / 删除，无响应体（204） */
export interface AdminUpdateComment {
  status: 'visible' | 'hidden' | 'deleted'
}

/** 按目标归并的举报：同一个帖子或评论的所有举报算一条 */
export interface AdminReport {
  target: ReportTarget
  /** 被举报的帖子（kind 为 post 时）或评论所在的帖子；kind 为 rating 时为 null */
  post: AdminPost | null
  /** kind 为 comment 时 */
  comment: AdminComment | null
  /** kind 为 rating 时（任务 131）：短评原文、作者昵称、评的是什么；comment_hidden 为 1 表示短评已隐藏 */
  rating?: { kind: RatingKind; name: string; nickname: string; comment: string; comment_hidden: number } | null
  /** 举报人数和各理由的人数 */
  count: number
  reasons: Partial<Record<ReportReason, number>>
  /** 最近 5 条非空的说明，新到旧 */
  details: string[]
  firstAt: string
  lastAt: string
  status: 'open' | 'resolved'
}
/**
 * GET /api/admin/reports：参数 status = 'open'（默认）| 'resolved'，cursor、limit。
 * open 按最近一次举报新到旧；处理后再有人举报同一目标，重新变成 open
 */
export interface AdminReportList {
  reports: AdminReport[]
  cursor: string | null
  /** 待处理的总数，后台标签上的角标 */
  openCount: number
}
/**
 * PATCH /api/admin/reports/:kind/:id：处理一个目标的全部举报，返回 204。
 * dismiss 只标记已处理；hide、delete 同时把目标改成对应状态
 */
export interface AdminResolveReport {
  action: 'dismiss' | 'hide' | 'delete'
}

// ── 后台看板（v0.9.3）────────────────────────────────
// 「今天」和每天的边界都按北京时间。只统计可见和下架的，作者删除的帖子、注销和封禁的账号不算。

/** 一项每天的数，近 30 天，旧到新 */
export interface DailyCount {
  /** 北京时间日期，如 '2026-11-14' */
  date: string
  /** 这一项开始记时间之前的日子为 null（点赞、导入是 2026-11-14 加的时间列） */
  count: number | null
}

export interface AdminRankItem {
  post: { id: string; name: string; author: string }
  count: number
}

/** GET /api/admin/stats：看板数据 */
export interface AdminStats {
  /** 累计 */
  totals: {
    users: number
    posts: number
    imports: number
    likes: number
    comments: number
    favorites: number
    /** 打开了云同步的账号数（云端有偏好或阵容的） */
    cloudSync: number
    /** 待处理举报（目标数，和举报标签的角标一样） */
    openReports: number
  }
  /** 近 30 天每天的新增 */
  daily: {
    users: DailyCount[]
    posts: DailyCount[]
    imports: DailyCount[]
    likes: DailyCount[]
    comments: DailyCount[]
    /** 资金表上报的局数（只统计打开了「帮大家补全资金表」的人；一局算一次） */
    incomeGames: DailyCount[]
  }
  /** 近 7 天导入、点赞最多的阵容，各前 10；按记录时间算，开始记时间之前的不算 */
  topImported: AdminRankItem[]
  topLiked: AdminRankItem[]
  /** 服务器生成这份数据的时间 */
  at: string
}

// ── 后台看板：Mirror酱（v0.9.4 后）────────────────────────
// 服务端拿 Pages 机密 MIRROR_KEY 调 Mirror酱 开发者面板的接口（同 mirrorchyan.com/zh/dashboard），浏览器拿不到 key。
// 项目列表在变量 MIRROR_RIDS（逗号分隔，如 'StrongholdAssistant,NovelAIPromptStudio,Pane'）。

export interface AdminMirrorProject {
  /** Mirror酱 的资源 id */
  rid: string
  /** 这个月（不含退款）：销量（buy_count 之和）、销售额（元）、预计收益（元：扣支付平台费率后五五分成，同 Mirror酱 面板的估算） */
  orders: number
  amount: number
  income: number
  /** 这个月退款的销量和金额 */
  refundedOrders: number
  refundedAmount: number
  /** 这个月每天（北京时间）的销量、销售额，1 号到今天（看往月时到月末），旧到新，没有销售的日子为 0 */
  daily: { date: string; orders: number; amount: number }[]
  /** 按方案（月卡、年卡……）和支付平台分的销量、销售额，销售额高的在前 */
  byPlan: { name: string; orders: number; amount: number }[]
  byPlatform: { name: string; orders: number; amount: number }[]
  /** Mirror酱 统计的每天日活（按 IP 估算）和下载量，近 30 天，旧到新，没有数据为 0 */
  usage: { date: string; users: number; downloads: number }[]
  /** 这个项目查询失败时的提示（其他字段为 0 或空数组） */
  error?: string
}

/** GET /api/admin/mirror?month=YYYY-MM（默认本月，北京时间）：Mirror酱 的销售和使用统计 */
export interface AdminMirrorStats {
  month: string
  projects: AdminMirrorProject[]
  at: string
}

// ── 后台看板：流量（任务 130）────────────────────────
// 香港反代服务器（docs/internal.md「国内加速」）上的脚本每 15 分钟汇总 nginx 日志，按北京时间的天和网站（Host）上报。
// 只有经香港服务器的流量（国内入口）；走 ESA 或 Cloudflare 的那部分不在这里。精品 EIP 按出方向流量计费，日志里的
// bytes_sent 不含 TLS 和 TCP 开销，比账单略少。

/** 一个网站一天的汇总（上报和后台看板共用） */
export interface TrafficDay {
  /** 北京时间日期，如 '2026-11-14' */
  date: string
  /** 请求的 Host，如 'sa.lubeiluchen.com' */
  host: string
  requests: number
  /** 发给访客的字节数（nginx $bytes_sent 之和） */
  bytes: number
  /** 当天不同 IP 数（只上报个数，IP 不出服务器） */
  visitors: number
  /** 走了香港缓存的请求：命中（HIT、STALE、UPDATING、REVALIDATED）和没命中（MISS、EXPIRED）；不缓存的路径两个都不算 */
  cacheHits: number
  cacheMisses: number
  status4xx: number
  status5xx: number
}

/** 一个网站一天里流量最大的路径（去掉查询参数），按 bytes 前 20 */
export interface TrafficPath {
  date: string
  host: string
  path: string
  requests: number
  bytes: number
}

/** POST /api/edge/traffic：香港服务器上报（请求头 X-Plaza-Edge 必须等于 EDGE_SECRET，否则 403）。
 *  每次带今天和昨天的完整数，同一天同一网站整行覆盖，重复上报无害 */
export interface TrafficReport {
  days: TrafficDay[]
  paths: TrafficPath[]
}

export interface AdminTrafficHost {
  host: string
  /** 这个月合计 */
  requests: number
  bytes: number
  /** 这个月每天，1 号到今天（看往月时到月末），旧到新，没有数据的日子各项为 0 */
  daily: { date: string; requests: number; bytes: number; visitors: number }[]
  /** 这个月的缓存命中率 0～1；这个网站没有走缓存的请求时为 null */
  cacheHitRate: number | null
  status4xx: number
  status5xx: number
  /** 这个月按路径合计，bytes 前 10 */
  topPaths: { path: string; requests: number; bytes: number }[]
}

/** GET /api/admin/traffic?month=YYYY-MM（默认本月，北京时间）：经香港服务器的流量 */
export interface AdminTrafficStats {
  month: string
  /** bytes 多的在前 */
  hosts: AdminTrafficHost[]
  /** 所有网站这个月合计 */
  bytes: number
  /** 精品流量单价（元/GB，1 GB = 1e9 字节），现在是 3 */
  pricePerGB: number
  /** 按这个月已过天数线性估算的全月字节数；看往月时等于 bytes */
  forecastBytes: number
  /** 最近一次收到上报的时间；从没收到过为 null（看板据此提示「香港服务器 N 分钟没上报了」） */
  lastReportAt: string | null
  at: string
}

// ── 干员和装备评分（任务 131）────────────────────────
// 社区第一步：玩家给干员、装备打 1～5 星，可以附一句短评。按赛季统计（服务端变量 RATING_SEASON，环境大改时设计改它，
// 旧赛季的评分保留但不再算进当前赛季）。目标用名字标识（和阵容里一样），服务端用 src/plaza/data/catalog.json 校验。

export type RatingKind = 'operator' | 'equipment'

export interface RatingSummary {
  kind: RatingKind
  name: string
  /** 当前赛季的评分人数 */
  count: number
  /** 平均分（保留 1 位小数）；不满 5 人为 null，界面显示「评分人数不足」 */
  avg: number | null
  /** 1～5 星各多少人，下标 0 是 1 星 */
  dist: [number, number, number, number, number]
}

/** GET /api/public/ratings?kind=operator|equipment：当前赛季所有有人评过的目标，不用登录、不带个人信息，
 *  Cache-Control: public, max-age=0, s-maxage=120（香港入口会缓存）。kind 不对 400 */
export interface RatingList {
  season: string
  kind: RatingKind
  items: RatingSummary[]
  at: string
}

export interface RatingComment {
  id: string
  author: { id: string; nickname: string }
  score: 1 | 2 | 3 | 4 | 5
  /** 1～100 字纯文本，不能换行 */
  comment: string
  createdAt: string
  updatedAt: string
}

/** GET /api/ratings/item?kind=&name=：一个目标的当前赛季详情。不用登录；登录了带 mine。
 *  短评新到旧，cursor、limit（默认 20，最多 50），只列写了短评、没被隐藏的。名字不在图鉴里 404 */
export interface RatingDetail {
  season: string
  summary: RatingSummary
  comments: RatingComment[]
  nextCursor: string | null
  /** 当前账号这个赛季对它的评分；没登录或没评过为 null */
  mine: { score: 1 | 2 | 3 | 4 | 5; comment: string; updatedAt: string } | null
}

/** GET /api/ratings/mine：当前账号这个赛季的全部评分（要登录），评分页用来在列表上标出「你给了几星」 */
export interface MyRatings {
  season: string
  items: { kind: RatingKind; name: string; score: 1 | 2 | 3 | 4 | 5 }[]
}

/** PUT /api/ratings：打分或改分（要登录，要有昵称：403 `need_nickname`），返回这个目标最新的 RatingSummary。
 *  一个账号对一个目标每赛季一条，再次提交覆盖分数和短评（短评传空串就是去掉）。
 *  名字不在图鉴里 404；分数不是 1～5 的整数、短评超长或有换行 400；
 *  同一 IP 每小时最多 60 次评分写入（rate_limited） */
export interface PutRatingRequest {
  kind: RatingKind
  name: string
  score: 1 | 2 | 3 | 4 | 5
  comment?: string
}
// DELETE /api/ratings?kind=&name=：撤回自己这个赛季的评分（要登录），204；本来就没有也 204。
// 后台：举报 kind 为 rating 的进举报列表（显示短评原文、作者、目标名字），处理时「隐藏」只隐藏短评，分数照算。
