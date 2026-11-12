// 阵容广场（v0.6）：助手界面直接带 Bearer 调广场接口（后端放行 https://app.local 跨域）。
// 凭据由宿主加密保存：plazaLogin 弹登录小窗口，plazaToken 启动时取一次。
import { api, type Lineup } from './api'
import { encodeLineup } from './share'
import type { Me, Post, PostList } from '../../../plaza/shared/api'

export type { Me, Post }
export const PLAZA = 'https://sa.lubeiluchen.com'

export class PlazaError extends Error {
  constructor(
    public code: string,
    message: string,
    public status: number,
  ) {
    super(message)
  }
}

function deviceId() {
  try {
    let id = localStorage.getItem('sa_device')
    if (!id) localStorage.setItem('sa_device', (id = crypto.randomUUID()))
    return id
  } catch {
    return crypto.randomUUID()
  }
}

class Plaza {
  token = $state<string | null>(null)
  me = $state<Me | null>(null)
  /** 第一次打开广场分段或账号栏时才去问，不在启动时联网 */
  checked = $state(false)
  loggingIn = $state(false)
  /** 发布面板：要发布的本地阵容 */
  publishing = $state<Lineup | null>(null)
  /** 广场帖子详情面板 */
  viewing = $state<Post | null>(null)

  private async req<T>(method: string, path: string, body?: unknown): Promise<T> {
    let r: Response
    try {
      r = await fetch(PLAZA + '/api' + path, {
        method,
        headers: {
          ...(this.token && { Authorization: `Bearer ${this.token}` }),
          ...(body !== undefined && { 'content-type': 'application/json' }),
        },
        body: body === undefined ? undefined : JSON.stringify(body),
      })
    } catch {
      throw new PlazaError('network', '连不上阵容广场，检查一下网络再试。', 0)
    }
    if (r.status === 204) return undefined as T
    const data = (await r.json().catch(() => undefined)) as { error?: string; message?: string } | undefined
    if (!r.ok) {
      // 凭据过期或被注销：清掉本地的，回到没登录
      if (r.status === 401 && this.token) {
        this.token = null
        this.me = null
        api.plazaLogout()
      }
      throw new PlazaError(data?.error ?? 'http', data?.message ?? `请求失败（${r.status}），请稍后再试。`, r.status)
    }
    return data as T
  }

  /** 取本地凭据并确认还有效；只在需要时调一次 */
  async check() {
    if (this.checked) return
    this.checked = true
    this.token = await api.plazaToken()
    if (!this.token) return
    try {
      this.me = await this.req<Me>('GET', '/me')
    } catch {
      /* 网络不通时保留凭据，下次再试；过期的 req 里已经清掉 */
    }
  }

  async login() {
    this.loggingIn = true
    try {
      const t = await api.plazaLogin()
      if (!t) return false
      this.token = t
      this.me = await this.req<Me>('GET', '/me')
      return true
    } finally {
      this.loggingIn = false
    }
  }

  async logout() {
    try {
      await this.req('POST', '/auth/logout')
    } catch {
      /* 后端退出失败也照样清本地 */
    }
    await api.plazaLogout()
    this.token = null
    this.me = null
  }

  async setNickname(nickname: string) {
    this.me = await this.req<Me>('PATCH', '/me', { nickname })
  }

  list(q: { sort: 'hot' | 'new' | 'featured'; covenant?: string[]; cursor?: string | null }) {
    const p = new URLSearchParams({ sort: q.sort, limit: '50' })
    for (const c of q.covenant ?? []) p.append('covenant', c)
    if (q.cursor) p.set('cursor', q.cursor)
    return this.req<PostList>('GET', '/posts?' + p)
  }
  post = (id: string) => this.req<Post>('GET', `/posts/${encodeURIComponent(id)}`)
  like = (id: string, on: boolean) => this.req<{ likes: number; liked: boolean }>(on ? 'POST' : 'DELETE', `/posts/${encodeURIComponent(id)}/like`)
  imported = (id: string) => this.req<{ imports: number }>('POST', `/posts/${encodeURIComponent(id)}/import`, { device: deviceId() })

  /** 发布：分享码 + 简介；同一账号发同一个码会更新原帖的简介 */
  publish(l: Lineup, description: string, catalog: Parameters<typeof encodeLineup>[1]) {
    return this.req<Post>('POST', '/posts', { code: encodeLineup(l, catalog), description })
  }
}

export const plaza = new Plaza()

/** 广场帖子转成本地阵容的样子（给阵容卡显示、导入用） */
export const postToLineup = (p: Post): Lineup => ({
  id: '',
  name: p.lineup.name,
  goals: p.lineup.goals,
  members: p.lineup.members,
  equipment: p.lineup.equipment,
  strategyId: p.lineup.strategyId,
  source: 'user',
  description: p.description || undefined,
  updatedAt: p.updatedAt,
})
