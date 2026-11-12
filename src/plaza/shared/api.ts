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
 * favorite = '1'（v0.7.0）：只列当前账号收藏的（要登录，没登录 401），含旧赛季，按收藏时间新到旧，忽略 sort 和 season。
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
 * 同一 device 对同一帖子只算一次。返回最新的 { imports }
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
  kind: 'post' | 'comment'
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
 * - lineups：本地自上次同步以来改过、删过的；服务端逐条比 updatedAt，不比云端旧的收下（相同也收）。
 * - prefs：本地改过才带；同样比 updatedAt。
 * - since：上次同步返回的 now；第一次同步（或网站打开「我的阵容」）为 null，返回全部（不含墓碑）。
 * 限制：每个账号最多 300 套阵容（不算墓碑，超了 400 + `too_many`），单套 JSON 最多 64 KB（400 + `too_large`），
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
  /** 这次推上来但云端更新、没收下的：给的是云端那份 */
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
 * 取「至少 3 个不同 device 报过的最小值」，不够 3 个的组不给。一天汇总一次
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
  /** 被举报的帖子（kind 为 post 时）或评论所在的帖子 */
  post: AdminPost
  /** kind 为 comment 时 */
  comment: AdminComment | null
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
