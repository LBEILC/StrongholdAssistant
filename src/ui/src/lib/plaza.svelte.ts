// 阵容广场（v0.6）：助手界面直接带 Bearer 调广场接口（后端放行 https://app.local 跨域）。
// 凭据由宿主加密保存：plazaLogin 弹登录小窗口，plazaToken 启动时取一次。
import { api, type Lineup } from './api'
import { encodeLineup } from './share'
import type { Comment, CommentList, CreateReportRequest, Me, Notice, NoticeList, Post, PostList, ReportTarget, SyncRequest, SyncResponse } from '../../../plaza/shared/api'

export type { Comment, Me, Notice, Post, ReportTarget }
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
  /** 站内消息（v0.9.3）：底部面板切成消息列表 */
  noticesOpen = $state(false)
  /** 未读数；新消息提醒关着或没登录时不问，保持 0 */
  unread = $state(0)
  /** 从消息点进帖子：详情面板底部给「返回消息」，评论区定位到这条评论 */
  fromNotices = $state(false)
  focusComment = $state<string | null>(null)

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
      throw new PlazaError(data?.error ?? 'http', data?.message ?? (r.status >= 500 ? `暂时连不上阵容广场服务器（${r.status}），过几分钟再试。` : `请求失败（${r.status}），请稍后再试。`), r.status)
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

  /** 设登录密码（v0.9.2，广场必须有密码）：还没密码的账号不用当前密码 */
  async setPassword(password: string) {
    this.me = await this.req<Me>('PUT', '/me/password', { password })
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
    // 没登录时走公开列表（v0.9.3，ESA 边缘缓存 2 分钟）；登录了要带上自己的点赞、收藏
    return this.req<PostList>('GET', (!this.token && q.sort !== 'favorite' ? '/public/posts?' : '/posts?') + p)
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
  notices = (cursor: string | null) => this.req<NoticeList>('GET', '/notices' + (cursor ? '?cursor=' + encodeURIComponent(cursor) : ''))
  readNotices = (before: string) => this.req<void>('POST', '/notices/read', { before })
  /** 问一次未读数：只在登录、开着新消息提醒时，失败就当没有 */
  async pollUnread() {
    if (!this.token) return
    try {
      this.unread = (await this.req<{ unread: number }>('GET', '/notices/unread')).unread
    } catch {
      /* 网络不通下次再问 */
    }
  }
  /** 云同步（v0.7.0），逻辑在 sync.svelte.ts */
  sync = (r: SyncRequest) => this.req<SyncResponse>('POST', '/sync', r)

  /** 发布：分享码 + 简介；同一账号发同一个码会更新原帖的简介 */
  publish(l: Lineup, description: string, catalog: Parameters<typeof encodeLineup>[1]) {
    return this.req<Post>('POST', '/posts', { code: encodeLineup(l, catalog), description })
  }
}

export const plaza = new Plaza()

/** 「3 分钟前」「2 天前」，超过 30 天写日期 */
export function ago(iso: string) {
  const m = Math.floor((Date.now() - Date.parse(iso)) / 60000)
  if (m < 1) return '刚刚'
  if (m < 60) return `${m} 分钟前`
  if (m < 1440) return `${Math.floor(m / 60)} 小时前`
  if (m < 43200) return `${Math.floor(m / 1440)} 天前`
  return iso.slice(0, 10)
}

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
