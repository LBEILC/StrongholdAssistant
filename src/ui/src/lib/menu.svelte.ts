// 右键菜单：界面里所有右键都走这里，不弹 WebView2 自带的浏览器菜单（输入框除外，保留剪切、复制、粘贴）。
// 干员、装备、盟约、阵容各有一份菜单，在任何页面右键都一样，不用先进「修正」。
import type { IconName } from '../components/Icon.svelte'
import type { Lineup } from './api'
import { app } from './state.svelte'

export interface MenuItem {
  label: string
  icon?: IconName
  /** 开关项：右侧显示勾选状态 */
  on?: boolean
  tone?: 'danger'
  /** 危险操作先变成「再点一次确认」，第二次才执行 */
  confirm?: string
  run: () => void
}
export type MenuEntry = MenuItem | 'sep'

export interface MenuState {
  x: number
  y: number
  title: string
  sub?: string
  items: MenuEntry[]
}

export const menu = $state<{ open: MenuState | null }>({ open: null })

export function openMenu(e: MouseEvent, m: Omit<MenuState, 'x' | 'y'>) {
  e.preventDefault()
  e.stopPropagation()
  menu.open = { ...m, x: e.clientX, y: e.clientY }
}

const tierName = (t: number) => `${['', 'I', 'II', 'III', 'IV', 'V', 'VI'][t]} 阶`

export function operatorMenu(name: string): Omit<MenuState, 'x' | 'y'> {
  const o = app.op(name)
  const inTarget = !!app.target?.members.some((m) => m.name === name)
  const items: MenuEntry[] = [
    { label: '查看详情', icon: 'arrowRight', run: () => (app.detail = { kind: 'operator', name }) },
    'sep',
    { label: '追踪', icon: 'star', on: app.tracked.has(name), run: () => app.toggleTracked(name) },
  ]
  if (app.target) items.push({ label: '在目标阵容里', icon: 'focus', on: inTarget, run: () => app.toggleMember(name) })
  items.push('sep', { label: '本局禁用', icon: 'forbid', on: app.banned.has(name), tone: 'danger', run: () => app.toggleBanned(name) })
  items.push({ label: '特别关注', icon: 'heart', on: app.focusOps.has(name), run: () => app.toggleFocusOp(name) })
  return { title: name, sub: tierName(o.tier), items }
}

export function equipmentMenu(name: string): Omit<MenuState, 'x' | 'y'> {
  const e = app.eq(name)
  const inTarget = !!app.target?.equipment.includes(name)
  const items: MenuEntry[] = [{ label: '查看详情', icon: 'arrowRight', run: () => (app.detail = { kind: 'equipment', name }) }, 'sep']
  items.push({ label: '追踪', icon: 'star', on: app.trackedEq.has(name), run: () => app.toggleTrackedEq(name) })
  if (app.target) items.push({ label: '在目标阵容里', icon: 'focus', on: inTarget, run: () => app.toggleTargetEq(name) })
  return { title: name, sub: `装备 · ${tierName(e.tier)}`, items }
}

export function covenantMenu(id: string): Omit<MenuState, 'x' | 'y'> {
  const c = app.cov(id)
  const mc = app.matchCovenant(id)
  const items: MenuEntry[] = [{ label: '查看详情', icon: 'arrowRight', run: () => (app.detail = { kind: 'covenant', id }) }, 'sep']
  if (mc) items.push({ label: '本局阵容不完整', icon: 'forbid', on: mc.incomplete, tone: 'danger', run: () => app.toggleCovenantIncomplete(id) })
  items.push({ label: '特别关注', icon: 'heart', on: app.focusCovs.has(id), run: () => app.toggleFocusCov(id) })
  return { title: c.name, sub: c.kind === 'core' ? '核心盟约' : '附加盟约', items }
}

export function lineupMenu(l: Lineup): Omit<MenuState, 'x' | 'y'> {
  const isT = app.session.targetLineupId === l.id
  return {
    title: l.name,
    sub: '阵容',
    items: [
      { label: '编辑', icon: 'pencil', run: () => app.edit(l) },
      { label: '复制分享码', icon: 'share', run: () => app.copyShare(l) },
      isT ? { label: '取消目标', icon: 'close', run: () => app.setTarget(null) } : { label: '设为目标', icon: 'focus', run: () => app.setTarget(l.id) },
      'sep',
      { label: '删除', icon: 'trash', tone: 'danger', confirm: '再点一次删除', run: () => app.deleteLineup(l.id) },
    ],
  }
}
