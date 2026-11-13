// 右键菜单：界面里所有右键都走这里，不弹 WebView2 自带的浏览器菜单；输入框也用自己的「剪切 / 复制 / 粘贴 / 全选」。
// 干员、装备、盟约、阵容各有一份菜单，在任何页面右键都一样，不用先进「修正」。
import type { IconName } from '../components/Icon.svelte'
import { api, type Lineup } from './api'
import { app } from './state.svelte'
import { plaza, postToLineup, PLAZA, type Post } from './plaza.svelte'
import { allStages, stageLabel } from './stages'

export interface MenuItem {
  label: string
  icon?: IconName
  /** 开关项：右侧显示勾选状态 */
  on?: boolean
  tone?: 'danger'
  /** 灰掉、点不了 */
  disabled?: boolean
  /** 危险操作先变成「再点一次确认」，第二次才执行 */
  confirm?: string
  run: () => void
}
export type MenuEntry = MenuItem | 'sep'

export interface MenuState {
  x: number
  y: number
  /** 不写就不画标题行（输入框菜单） */
  title?: string
  sub?: string
  items: MenuEntry[]
}

export const menu = $state<{ open: MenuState | null; at: number }>({ open: null, at: 0 })

export function openMenu(e: MouseEvent, m: Omit<MenuState, 'x' | 'y'>) {
  e.preventDefault()
  e.stopPropagation()
  menu.open = { ...m, x: e.clientX, y: e.clientY }
  menu.at = performance.now()
}

export type TextField = HTMLInputElement | HTMLTextAreaElement
const TEXT_TYPES = new Set(['text', 'search', 'email', 'url', 'tel', 'password'])
/** 右键点在可以打字的框里时返回它（勾选框、开关之类不算） */
export const textField = (t: EventTarget | null): TextField | null => {
  const el = (t as HTMLElement | null)?.closest?.('input, textarea')
  return el instanceof HTMLTextAreaElement || (el instanceof HTMLInputElement && TEXT_TYPES.has(el.type)) ? el : null
}

/** 输入框的右键菜单。点菜单时输入框已经失焦，先把焦点和选区放回去再走浏览器的编辑命令：撤销、input 事件、字数上限都照常 */
export function textMenu(e: MouseEvent, el: TextField) {
  const start = el.selectionStart ?? 0
  const end = el.selectionEnd ?? 0
  const selected = end > start && el.type !== 'password'
  const editable = !el.readOnly && !el.disabled
  const back = () => {
    el.focus()
    el.setSelectionRange(start, end)
  }
  openMenu(e, {
    items: [
      { label: '剪切', icon: 'cut', disabled: !selected || !editable, run: () => (back(), document.execCommand('cut')) },
      { label: '复制', icon: 'copy', disabled: !selected, run: () => (back(), document.execCommand('copy')) },
      {
        label: '粘贴',
        icon: 'paste',
        disabled: !editable,
        run: async () => {
          const text = await navigator.clipboard.readText()
          back()
          document.execCommand('insertText', false, text)
        },
      },
      'sep',
      { label: '全选', icon: 'selectAll', disabled: !el.value, run: () => (el.focus(), el.select()) },
    ],
  })
}

const tierName = (t: number) => `${['', 'I', 'II', 'III', 'IV', 'V', 'VI'][t]} 阶`

export function operatorMenu(name: string): Omit<MenuState, 'x' | 'y'> {
  const o = app.op(name)
  const inTarget = !!app.active?.members.some((m) => m.name === name)
  const items: MenuEntry[] = []
  // 按格位推测的禁用：先让用户确认或否认，否认后那一格变回问号、换候选
  if (app.inferred.has(name))
    items.push(
      { label: '确认是他', icon: 'check', run: () => app.setBanned(name, true) },
      { label: '不是他', icon: 'close', tone: 'danger', run: () => app.setBanned(name, false) },
      'sep',
    )
  items.push(
    { label: '查看详情', icon: 'arrowRight', run: () => (app.detail = { kind: 'operator', name }) },
    'sep',
    { label: '追踪', icon: 'star', on: app.tracked.has(name), run: () => app.toggleTracked(name) },
  )
  if (app.target) items.push({ label: '在目标阵容里', icon: 'focus', on: inTarget, run: () => app.toggleMember(name) })
  items.push('sep', { label: '本局禁用', icon: 'forbid', on: app.banned.has(name), tone: 'danger', run: () => app.toggleBanned(name) })
  items.push({ label: '特别关注', icon: 'heart', on: app.focusOps.has(name), run: () => app.toggleFocusOp(name) })
  return { title: name, sub: app.inferred.has(name) ? `${tierName(o.tier)} · 按格位推测的禁用` : tierName(o.tier), items }
}

export function equipmentMenu(name: string): Omit<MenuState, 'x' | 'y'> {
  const e = app.eq(name)
  const inTarget = !!app.active?.equipment.includes(name)
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

/** 禁用里没认出的格子：候选直接列在菜单里，点一下就填上（宿主按禁用填进这一格） */
export function slotMenu(cov: string, i: number): Omit<MenuState, 'x' | 'y'> {
  const slot = app.match.banGroups.find((g) => g.covenant === cov)?.slots[i]
  const cands = slot?.candidates ?? []
  const items: MenuEntry[] = cands.slice(0, 8).map((n) => ({ label: n, icon: 'forbid', run: () => app.setBanned(n, true) }))
  if (items.length) items.push('sep')
  items.push({ label: '打开挑选面板…', icon: 'search', run: () => (app.banPicker = { cov, slot: i }) })
  return {
    title: `${app.cov(cov).name} · 第 ${i + 1} 格`,
    sub: `${slot ? tierName(slot.tier) : ''}${cands.length ? ` · ${cands.length} 个候选，点一个填上` : ' · 没有候选，打开面板手动补'}`,
    items,
  }
}

/** 顶栏目标阵容名：换目标、切阶段、追踪 */
export function targetMenu(): Omit<MenuState, 'x' | 'y'> {
  const t = app.target
  const cur = app.session.targetLineupId
  const recent = [...app.lineups].sort((a, b) => b.updatedAt.localeCompare(a.updatedAt)).slice(0, 8)
  const items: MenuEntry[] = recent.map((l) => ({ label: l.name, icon: 'focus', on: l.id === cur, run: () => app.setTarget(l.id === cur ? null : l.id) }))
  if (t?.stages?.length)
    items.push(
      'sep',
      { label: '阶段：按回合自动', on: app.session.stage === null, run: () => app.setStage(null) },
      ...allStages(t).map((s, i) => ({ label: s.final ? '成型阵容' : stageLabel(s), on: app.session.stage === i, run: () => app.setStage(i) })),
    )
  items.push('sep')
  if (app.untrackedInTarget) items.push({ label: `全部追踪（还有 ${app.untrackedInTarget} 个没追踪）`, icon: 'refresh', run: () => app.trackTarget() })
  const total = app.session.tracked.length + app.session.trackedEquipment.length
  items.push({ label: '清空追踪', icon: 'close', tone: 'danger', disabled: !total, confirm: '再点一次清空', run: () => app.clearTracked() })
  if (cur) items.push({ label: '取消目标', icon: 'close', run: () => app.setTarget(null) })
  return { title: '目标阵容', sub: t ? t.name : '还没有目标；点一个设为目标', items }
}

/** 策略：本局策略、目标阵容的策略 */
export function strategyMenu(id: string): Omit<MenuState, 'x' | 'y'> {
  const s = app.catalog.strategies.find((x) => x.id === id)!
  const isMatch = app.match.strategyId === id
  const items: MenuEntry[] = [{ label: '本局策略', icon: 'focus', on: isMatch, run: () => app.setMatchStrategy(isMatch ? null : id) }]
  if (app.target) {
    const inT = app.target.strategyId === id
    items.push({ label: '目标阵容的策略', icon: 'star', on: inT, run: () => app.setTargetStrategy(inT ? null : id) })
  }
  return { title: s.name, sub: `策略 · ${s.initiator}`, items }
}

/** 应用内广场的阵容卡 */
export function plazaPostMenu(p: Post): Omit<MenuState, 'x' | 'y'> {
  const imp = (asTarget: boolean) => {
    plaza.imported(p.id).catch(() => {})
    app.importLineup(postToLineup(p), asTarget)
  }
  return {
    title: p.lineup.name,
    sub: `阵容广场 · ${p.author.nickname}${p.current ? '' : ' · 旧赛季'}`,
    items: [
      { label: '查看', icon: 'arrowRight', run: () => (plaza.viewing = p) },
      'sep',
      { label: '导入到我的阵容', icon: 'download', disabled: !p.current, run: () => imp(false) },
      { label: '导入并设为目标', icon: 'focus', disabled: !p.current, run: () => imp(true) },
      { label: '复制分享码', icon: 'share', disabled: !p.current, run: () => app.copyShare(postToLineup(p)) },
      'sep',
      { label: '在浏览器里打开', icon: 'arrowRight', run: () => api.openUrl(`${PLAZA}/p/${p.id}`) },
    ],
  }
}

export function lineupMenu(l: Lineup): Omit<MenuState, 'x' | 'y'> {
  const isT = app.session.targetLineupId === l.id
  return {
    title: l.name,
    sub: '阵容',
    items: [
      { label: '编辑', icon: 'pencil', run: () => app.edit(l) },
      { label: '复制一份', icon: 'copy', run: () => app.duplicateLineup(l) },
      { label: '复制分享码', icon: 'share', run: () => app.copyShare(l) },
      { label: '发布到广场', icon: 'upload', run: () => (plaza.publishing = $state.snapshot(l) as Lineup) },
      { label: '用这套阵容更新我的默认', icon: 'magic', run: () => app.defaultsFromLineup(l) },
      isT ? { label: '取消目标', icon: 'close', run: () => app.setTarget(null) } : { label: '设为目标', icon: 'focus', run: () => app.setTarget(l.id) },
      'sep',
      { label: '删除', icon: 'trash', tone: 'danger', confirm: '再点一次删除', run: () => app.deleteLineup(l.id) },
    ],
  }
}
