// 广场接口调用：同域 /api，登录态用 HttpOnly Cookie（后端下发），网站不碰凭据。
import type {
  AdminPost,
  AdminPostList,
  AdminUpdatePost,
  AdminUser,
  LikeResponse,
  Me,
  Post,
  PostDetail,
  PostList,
  SendCodeResponse,
} from '../../shared/api'

export type { AdminPost, AdminUser, Me, Post, PostDetail }

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
    throw new ApiError(e?.error ?? 'http', e?.message ?? `请求失败（${r.status}），请稍后再试。`, r.status)
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
  covenant?: string[]
  strategy?: string | null
  author?: string
  season?: 'current' | 'all'
  cursor?: string | null
}

export const api = {
  posts(q: ListQuery) {
    const p = new URLSearchParams()
    if (q.sort) p.set('sort', q.sort)
    for (const c of q.covenant ?? []) p.append('covenant', c)
    if (q.strategy) p.set('strategy', q.strategy)
    if (q.author) p.set('author', q.author)
    if (q.season) p.set('season', q.season)
    if (q.cursor) p.set('cursor', q.cursor)
    return req<PostList>('GET', '/posts?' + p)
  },
  post: (id: string) => req<PostDetail>('GET', `/posts/${encodeURIComponent(id)}`),
  updatePost: (id: string, description: string) => req<Post>('PATCH', `/posts/${encodeURIComponent(id)}`, { description }),
  deletePost: (id: string) => req<void>('DELETE', `/posts/${encodeURIComponent(id)}`),
  like: (id: string, on: boolean) => req<LikeResponse>(on ? 'POST' : 'DELETE', `/posts/${encodeURIComponent(id)}/like`),
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
    users: (q: string) => req<AdminUser[]>('GET', '/admin/users?q=' + encodeURIComponent(q)),
    setBanned: (id: string, banned: boolean) => req<AdminUser>('PATCH', `/admin/users/${encodeURIComponent(id)}`, { banned }),
  },
}
