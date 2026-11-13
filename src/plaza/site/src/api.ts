// 广场接口调用：同域 /api，登录态用 HttpOnly Cookie（后端下发），网站不碰凭据。
import type {
  AdminComment,
  AdminCommentList,
  AdminPost,
  AdminPostList,
  AdminReport,
  AdminReportList,
  AdminResolveReport,
  Comment,
  CommentList,
  CreateReportRequest,
  AdminUpdatePost,
  AdminUser,
  FavoriteResponse,
  SyncRequest,
  SyncResponse,
  CloudLineup,
  LikeResponse,
  Me,
  Post,
  PostDetail,
  PostList,
  SendCodeResponse,
} from '../../shared/api'

export type { AdminComment, AdminPost, AdminReport, AdminUser, CloudLineup, Comment, Me, Post, PostDetail }
export { REPORT_REASONS, type ReportReason, type ReportTarget } from '../../shared/api'

export class ApiError extends Error {
  constructor(
    public code: string,
    message: string,
    public status: number,
  ) {
    super(message)
  }
}

async function req<T>(method: string, path: string, body?: unknown): Promise<T> {
  let r: Response
  try {
    r = await fetch('/api' + path, {
      method,
      headers: body === undefined ? undefined : { 'content-type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  } catch {
    throw new ApiError('network', '连不上广场服务器，检查一下网络再试。', 0)
  }
  if (r.status === 204) return undefined as T
  const text = await r.text()
  let data: unknown
  try {
    data = text ? JSON.parse(text) : undefined
  } catch {
    data = undefined
  }
  if (!r.ok) {
    const e = data as { error?: string; message?: string } | undefined
    throw new ApiError(e?.error ?? 'http', e?.message ?? (r.status >= 500 ? `暂时连不上阵容广场服务器（${r.status}），过几分钟再试。` : `请求失败（${r.status}），请稍后再试。`), r.status)
  }
  return data as T
}

// 导入计数用的设备 id：本地随机生成一次，长期保留
function deviceId() {
  try {
    let id = localStorage.getItem('sa_device')
    if (!id) localStorage.setItem('sa_device', (id = crypto.randomUUID()))
    return id
  } catch {
    return crypto.randomUUID()
  }
}

export interface ListQuery {
  sort?: 'hot' | 'new' | 'featured'
  /** 搜索（v0.9.1）：阵容名、简介、作者、干员、装备，空格分开的几个词都要命中 */
  q?: string
  /** 只列我收藏的（v0.7.0，要登录） */
  favorite?: boolean
  covenant?: string[]
  strategy?: string | null
  author?: string
  season?: 'current' | 'all'
  cursor?: string | null
}

export const api = {
  posts(q: ListQuery) {
    const p = new URLSearchParams()
    if (q.favorite) p.set('favorite', '1')
    else if (q.sort) p.set('sort', q.sort)
    if (q.q?.trim()) p.set('q', q.q.trim())
    for (const c of q.covenant ?? []) p.append('covenant', c)
    if (q.strategy) p.set('strategy', q.strategy)
    if (q.author) p.set('author', q.author)
    if (q.season) p.set('season', q.season)
    if (q.cursor) p.set('cursor', q.cursor)
    return req<PostList>('GET', '/posts?' + p)
  },
  post: (id: string) => req<PostDetail>('GET', `/posts/${encodeURIComponent(id)}`),
  /** 带 code 时连阵容一起换（v0.7.4），点赞、收藏、评论保留 */
  updatePost: (id: string, description: string, code?: string) =>
    req<Post>('PATCH', `/posts/${encodeURIComponent(id)}`, { description, ...(code && { code }) }),
  deletePost: (id: string) => req<void>('DELETE', `/posts/${encodeURIComponent(id)}`),
  /** 云同步（v0.7.0）：网站「我的阵容」读写都走它 */
  sync: (r: SyncRequest) => req<SyncResponse>('POST', '/sync', r),
  /** 我的阵容：全部（不含已删除的） */
  myLineups: () => req<SyncResponse>('POST', '/sync', { since: null, lineups: [] }).then((r) => r.lineups.filter((c) => !c.deleted && c.data)),
  favorite: (id: string, on: boolean) => req<FavoriteResponse>(on ? 'POST' : 'DELETE', `/posts/${encodeURIComponent(id)}/favorite`),
  like: (id: string, on: boolean) => req<LikeResponse>(on ? 'POST' : 'DELETE', `/posts/${encodeURIComponent(id)}/like`),
  comments: (id: string, cursor: string | null) =>
    req<CommentList>('GET', `/posts/${encodeURIComponent(id)}/comments` + (cursor ? '?cursor=' + encodeURIComponent(cursor) : '')),
  comment: (id: string, body: string, replyTo?: string) => req<Comment>('POST', `/posts/${encodeURIComponent(id)}/comments`, { body, ...(replyTo && { replyTo }) }),
  deleteComment: (id: string) => req<void>('DELETE', `/comments/${encodeURIComponent(id)}`),
  report: (r: CreateReportRequest) => req<void>('POST', '/reports', r),
  imported: (id: string) => req<{ imports: number }>('POST', `/posts/${encodeURIComponent(id)}/import`, { device: deviceId() }),

  sendCode: (email: string, turnstileToken: string) => req<SendCodeResponse>('POST', '/auth/code', { email, turnstileToken }),
  verify: (email: string, code: string) => req<{ token: string; me: Me }>('POST', '/auth/verify', { email, code }),
  passwordLogin: (email: string, password: string, turnstileToken: string) => req<{ token: string; me: Me }>('POST', '/auth/password', { email, password, turnstileToken }),
  publish: (code: string, description: string) => req<Post>('POST', '/posts', { code, description }),
  setPassword: (password: string, currentPassword?: string) => req<Me>('PUT', '/me/password', { password, ...(currentPassword && { currentPassword }) }),
  deletePassword: (currentPassword?: string) => req<void>('DELETE', '/me/password', currentPassword ? { currentPassword } : {}),
  logout: () => req<void>('POST', '/auth/logout'),
  me: () => req<Me>('GET', '/me'),
  setNickname: (nickname: string) => req<Me>('PATCH', '/me', { nickname }),
  deleteMe: () => req<void>('DELETE', '/me'),

  admin: {
    posts: (status: AdminPost['status'] | null, cursor: string | null) => {
      const p = new URLSearchParams()
      if (status) p.set('status', status)
      if (cursor) p.set('cursor', cursor)
      return req<AdminPostList>('GET', '/admin/posts?' + p)
    },
    updatePost: (id: string, patch: AdminUpdatePost) => req<AdminPost>('PATCH', `/admin/posts/${encodeURIComponent(id)}`, patch),
    comments: (status: AdminComment['status'] | null, cursor: string | null) => {
      const p = new URLSearchParams()
      if (status) p.set('status', status)
      if (cursor) p.set('cursor', cursor)
      return req<AdminCommentList>('GET', '/admin/comments?' + p)
    },
    updateComment: (id: string, status: AdminComment['status']) => req<void>('PATCH', `/admin/comments/${encodeURIComponent(id)}`, { status }),
    reports: (status: AdminReport['status'], cursor: string | null) => {
      const p = new URLSearchParams({ status })
      if (cursor) p.set('cursor', cursor)
      return req<AdminReportList>('GET', '/admin/reports?' + p)
    },
    resolveReport: (r: AdminReport['target'], action: AdminResolveReport['action']) =>
      req<void>('PATCH', `/admin/reports/${r.kind}/${encodeURIComponent(r.id)}`, { action }),
    users: (q: string) => req<AdminUser[]>('GET', '/admin/users?q=' + encodeURIComponent(q)),
    setBanned: (id: string, banned: boolean) => req<AdminUser>('PATCH', `/admin/users/${encodeURIComponent(id)}`, { banned }),
  },
}
