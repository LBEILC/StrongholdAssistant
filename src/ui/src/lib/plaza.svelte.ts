// 阵容广场（v0.6）：助手界面直接带 Bearer 调广场接口（后端放行 https://app.local 跨域）。
// 凭据由宿主加密保存：plazaLogin 弹登录小窗口，plazaToken 启动时取一次。
import { api, type Lineup } from './api'
import { encodeLineup } from './share'
import type { Comment, CommentList, CreateReportRequest, Me, Post, PostList, ReportTarget, SyncRequest, SyncResponse } from '../../../plaza/shared/api'

export type { Comment, Me, Post, ReportTarget }
export { REPORT_REASONS, type ReportReason } from '../../../plaza/shared/api'
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
  /** 详情面板里正在举报的帖子或评论（面板切成举报页） */
  reporting = $state<{ target: ReportTarget; what: string } | null>(null)

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

  /** 广场的搜索和筛选（v0.9.1）：切页面、切排序都保留，重启清空 */
  filter = $state<{ q: string; covenants: string[]; strategy: string | null }>({ q: '', covenants: [], strategy: null })

  /** sort 为 'favorite' 时只列我收藏的（要登录）；q 搜阵容名、简介、作者、干员、装备 */
  list(q: { sort: 'hot' | 'new' | 'featured' | 'favorite'; q?: string; covenant?: string[]; strategy?: string | null; cursor?: string | null }) {
    const p = new URLSearchParams(q.sort === 'favorite' ? { favorite: '1', limit: '50' } : { sort: q.sort, limit: '50' })
    if (q.q?.trim()) p.set('q', q.q.trim())
    for (const c of q.covenant ?? []) p.append('covenant', c)
    if (q.strategy) p.set('strategy', q.strategy)
    if (q.cursor) p.set('cursor', q.cursor)
    return this.req<PostList>('GET', '/posts?' + p)
  }
  post = (id: string) => this.req<Post>('GET', `/posts/${encodeURIComponent(id)}`)
  like = (id: string, on: boolean) => this.req<{ likes: number; liked: boolean }>(on ? 'POST' : 'DELETE', `/posts/${encodeURIComponent(id)}/like`)
  favorite = (id: string, on: boolean) => this.req<{ favorited: boolean }>(on ? 'POST' : 'DELETE', `/posts/${encodeURIComponent(id)}/favorite`)
  imported = (id: string) => this.req<{ imports: number }>('POST', `/posts/${encodeURIComponent(id)}/import`, { device: deviceId() })

  comments = (id: string, cursor: string | null) =>
    this.req<CommentList>('GET', `/posts/${encodeURIComponent(id)}/comments` + (cursor ? '?cursor=' + encodeURIComponent(cursor) : ''))
  comment = (id: string, body: string, replyTo?: string) =>
    this.req<Comment>('POST', `/posts/${encodeURIComponent(id)}/comments`, { body, ...(replyTo && { replyTo }) })
  deleteComment = (id: string) => this.req<void>('DELETE', `/comments/${encodeURIComponent(id)}`)
  report = (r: CreateReportRequest) => this.req<void>('POST', '/reports', r)
  /** 云同步（v0.7.0），逻辑在 sync.svelte.ts */
  sync = (r: SyncRequest) => this.req<SyncResponse>('POST', '/sync', r)

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
  ...(p.lineup.stages?.length && { stages: p.lineup.stages }),
  source: 'user',
  description: p.description || undefined,
  updatedAt: p.updatedAt,
})
