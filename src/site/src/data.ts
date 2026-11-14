// 官网用到的数据：外链、更新日志（助手打包的同一份 release-notes）、构建时算好的图鉴数字
type Note = { kind: 'new' | 'improved' | 'fixed'; title: string; detail: string; highlight?: boolean }
export type Release = { version: string; date: string; items: Note[] }

declare const __STATS__: { operators: number; equipment: number; covenants: number; strategies: number; bonds: { id: string; name: string; kind: string }[] }
export const STATS = __STATS__

const cmp = (a: string, b: string) => {
  const x = a.split('.').map(Number)
  const y = b.split('.').map(Number)
  for (let i = 0; i < 3; i++) if (x[i] !== y[i]) return x[i] - y[i]
  return 0
}

/** 新的在前 */
export const RELEASES = (Object.values(import.meta.glob('../../ui/release-notes/*.json', { eager: true, import: 'default' })) as Release[]).sort((a, b) => cmp(b.version, a.version))
export const LATEST = RELEASES[0]
export const FIRST = RELEASES[RELEASES.length - 1]

export const KIND = { new: '新增', improved: '改进', fixed: '修复' } as const

const REPO = 'https://github.com/LBEILC/StrongholdAssistant'
export const LINKS = {
  repo: REPO,
  releases: REPO + '/releases/latest',
  issues: REPO + '/issues',
  license: REPO + '/blob/main/LICENSE',
  notice: REPO + '/blob/main/NOTICE',
  assets: REPO + '/blob/main/ASSETS.md',
  // source 只给 Mirror酱 做来源统计，不影响分成归属
  mirror: 'https://mirrorchyan.com/zh/projects?rid=StrongholdAssistant&source=sa-website',
  qq: 'https://qm.qq.com/q/AuRBCSxWEg',
  qqGroup: '1132134217',
  plaza: '/plaza',
  webview2: 'https://developer.microsoft.com/microsoft-edge/webview2/',
}

/** 站内页面：导航和页脚共用。社区先占位 */
export const NAV = [
  { id: 'home', label: '首页', href: '/' },
  { id: 'download', label: '下载', href: '/#download' },
  { id: 'changelog', label: '更新日志', href: '/changelog/' },
  { id: 'faq', label: '常见问题', href: '/faq/' },
  { id: 'about', label: '开源与素材', href: '/about/' },
] as const
export type PageId = 'home' | 'changelog' | 'faq' | 'about'

/** 日期 2026-11-14 → 2026.11.14 */
export const dot = (d: string) => d.replaceAll('-', '.')
