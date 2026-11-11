// 打包进来的更新说明（src/ui/release-notes/*.json）：设置里的更新日志、更新后第一次打开的「这一版有什么新东西」共用
import type { ReleaseNotes } from './api'

export const cmpVersion = (a: string, b: string) => {
  const x = a.split('.').map(Number)
  const y = b.split('.').map(Number)
  for (let i = 0; i < 3; i++) if ((x[i] || 0) !== (y[i] || 0)) return (x[i] || 0) - (y[i] || 0)
  return 0
}

/** 新的在前 */
export const CHANGELOG = (Object.values(import.meta.glob('../../release-notes/*.json', { eager: true, import: 'default' })) as ReleaseNotes[]).sort(
  (a, b) => cmpVersion(b.version, a.version),
)

export const KIND_LABEL = { new: '新增', improved: '改进', fixed: '修复' } as const

/** 上次看过的版本记在本机；换了版本（含第一次装）就弹一次，开发构建（0.0.0）不弹 */
const SEEN_KEY = 'stronghold.seenVersion'
export function unseenNotes(current: string): ReleaseNotes | null {
  if (current === '0.0.0') return null
  let seen: string | null = null
  try {
    seen = localStorage.getItem(SEEN_KEY)
  } catch {}
  if (seen === current) return null
  return CHANGELOG.find((n) => n.version === current) ?? null
}
export function markSeen(current: string) {
  try {
    localStorage.setItem(SEEN_KEY, current)
  } catch {}
}
