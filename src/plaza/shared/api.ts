// 阵容广场的接口定义（设计定义，工程照着在 Pages Functions 里实现）。
// 网站（src/plaza/site）和助手界面（src/ui）都按这里的类型调用。
//
// 约定：
// - 路径都在同一个域名下的 /api/ 里，JSON 进出，时间是 ISO 字符串。
// - 登录凭据：助手放在请求头 `Authorization: Bearer <token>`；网站用 HttpOnly Cookie（`sa_session`），两种都认。
// - 出错时 HTTP 状态码非 2xx，响应体是 ApiError；message 是直接给用户看的中文。
// - 需要登录的接口没登录返回 401，被封禁返回 403。

import type { CovenantGoal, LineupMember } from '../../ui/src/lib/api/types'

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

/** DELETE /api/me：注销。删除邮箱、昵称和这个账号发的全部帖子、点赞，凭据全部失效。无响应体 */

// ── 帖子 ─────────────────────────────────────────────

/** 帖子里的阵容：发布时服务端用当期数据解码分享码得到，存下来，换赛季后也能照常显示 */
export interface PostLineup {
  name: string
  goals: CovenantGoal[]
  members: LineupMember[]
  equipment: string[]
  strategyId: string | null
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
  /** 当前账号点过赞（没登录为 false） */
  liked: boolean
  /** 分享码的数据指纹和当期一致：false 表示旧赛季的帖子，只能看、不能导入 */
  current: boolean
}

/**
 * GET /api/posts：列表。默认只列当期（current）且可见的帖子。
 * 查询参数：sort = 'featured' | 'new' | 'hot'（默认 'hot'：点赞 × 2 + 导入数，相同按新到旧）；
 * covenant（盟约 id，可重复，要求目标盟约全部包含）；strategy（策略 id）；author（账号 id，看某人发的，含旧赛季）；
 * season = 'current'（默认）| 'all'；cursor（上一页返回的）；limit（默认 20，最多 50）。
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

/** PATCH /api/posts/:id：作者改简介。DELETE /api/posts/:id：作者删除 */
export interface UpdatePostRequest {
  description: string
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

/** PATCH /api/admin/users/:id：封禁 / 解封。封禁后不能登录、发布、点赞，他的帖子全部下架（解封不自动恢复） */
export interface AdminUpdateUser {
  banned: boolean
}
