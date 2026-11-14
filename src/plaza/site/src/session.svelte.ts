// 当前登录的账号：启动时问一次 /api/me，401 就是没登录
import { api, ApiError, type Me } from './api'

class Session {
  me = $state<Me | null>(null)
  ready = $state(false)
  /** 站内消息未读数（v0.9.3）：打开页面和切页面时问一次 */
  unread = $state(0)
  async load() {
    try {
      this.me = await api.me()
    } catch (e) {
      if (!(e instanceof ApiError) || e.status !== 401) console.warn(e)
      this.me = null
    }
    this.ready = true
  }
  async pollUnread() {
    if (!this.me) return
    try {
      this.unread = (await api.unread()).unread
    } catch {
      /* 下次再问 */
    }
  }
  async logout() {
    await api.logout()
    this.me = null
  }
}

export const session = new Session()

// 轻提示：右下角一行字，3 秒后消失
class Toast {
  text = $state('')
  bad = $state(false)
  private t = 0
  show(text: string, bad = false) {
    this.text = text
    this.bad = bad
    clearTimeout(this.t)
    this.t = window.setTimeout(() => (this.text = ''), 3000)
  }
}
export const toast = new Toast()
