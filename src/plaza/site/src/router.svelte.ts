// 极简路由：History API。Pages 没有 404.html 时会把未知路径都交给 index.html。
export type Route =
  | { page: 'home' }
  | { page: 'post'; id: string }
  | { page: 'login'; next: string }
  | { page: 'me' }
  | { page: 'new'; edit: string | null }
  | { page: 'privacy' }
  | { page: 'admin' }
  | { page: 'notfound' }

function parse(): Route {
  const p = location.pathname.replace(/\/+$/, '') || '/'
  const q = new URLSearchParams(location.search)
  if (p === '/') return { page: 'home' }
  const m = p.match(/^\/p\/([^/]+)$/)
  if (m) return { page: 'post', id: decodeURIComponent(m[1]) }
  if (p === '/login') return { page: 'login', next: q.get('next') ?? '/me' }
  if (p === '/me') return { page: 'me' }
  if (p === '/new') return { page: 'new', edit: q.get('edit') }
  if (p === '/privacy') return { page: 'privacy' }
  if (p === '/admin') return { page: 'admin' }
  return { page: 'notfound' }
}

class Router {
  route = $state<Route>(parse())
  constructor() {
    addEventListener('popstate', () => (this.route = parse()))
  }
  go(path: string) {
    if (path === location.pathname + location.search) return
    history.pushState(null, '', path)
    this.route = parse()
    scrollTo(0, 0)
  }
}

export const router = new Router()

/** 站内链接：普通左键点击走前端路由，中键、Ctrl 点击照常新开标签 */
export function link(node: HTMLAnchorElement) {
  const onclick = (e: MouseEvent) => {
    if (e.button !== 0 || e.ctrlKey || e.metaKey || e.shiftKey || e.altKey || node.target) return
    e.preventDefault()
    router.go(node.getAttribute('href')!)
  }
  node.addEventListener('click', onclick)
  return { destroy: () => node.removeEventListener('click', onclick) }
}
