// 阵容里干员的精锐、技能、模组（v0.7.0，设计见 docs/design.md「精锐、技能、模组」）。
// 默认的优先级：阵容里写了的 → 我的默认（Settings.operatorDefaults）→ 数据默认（Operator.defaultSkill / defaultModule）
import type { LineupMember, Operator, Settings } from './api'

type Defaults = Settings['operatorDefaults']

/** 「绝技」：按精锐状态的干员人数激活（数据：「在场 2 名精锐状态的干员」，所有干员的精锐都算），不看干员的盟约 */
export const ELITE_COVENANT = 'sunt'

/** 这名干员不写时用的技能、模组 */
export const defaultsFor = (op: Operator, my: Defaults) => ({
  skill: my[op.name]?.skill ?? op.defaultSkill,
  module: my[op.name]?.module ?? op.defaultModule ?? '',
})

/** 实际生效的技能、模组（模组 '' = 不带） */
export function effective(m: LineupMember, op: Operator, my: Defaults) {
  const d = defaultsFor(op, my)
  return { skill: m.skill ?? d.skill, module: m.module ?? d.module }
}

/** 头像右下角要不要露技能图标：技能或模组和默认不一样时，返回要显示的技能 id */
export function customSkill(m: LineupMember, op: Operator, my: Defaults) {
  const d = defaultsFor(op, my)
  const e = effective(m, op, my)
  return e.skill !== d.skill || e.module !== d.module ? e.skill : null
}

/** 选技能或模组：总是写进阵容（默认只在加干员时预填，之后改默认不影响已有阵容） */
export const setChoice = (m: LineupMember, _op: Operator, _my: Defaults, k: 'skill' | 'module', v: string): LineupMember => ({ ...m, [k]: v })

/** 保存阵容时把没写的技能、模组按当时的默认写进去（各阶段都补）；目录里没有技能数据的干员不补 */
export function fillDefaults<T extends { members: LineupMember[]; stages?: { members: LineupMember[] }[] }>(l: T, ops: (name: string) => Operator, my: Defaults): T {
  const fill = (m: LineupMember) => {
    const op = ops(m.name)
    if (!op.skills?.length) return m
    const d = defaultsFor(op, my)
    return { ...m, skill: m.skill ?? d.skill, module: m.module ?? (op.modules?.length ? d.module : undefined) }
  }
  return { ...l, members: l.members.map(fill), ...(l.stages && { stages: l.stages.map((s) => ({ ...s, members: s.members.map(fill) })) }) }
}

/** 改这名干员的「我的默认」：和数据默认一样的项不写，两项都一样就删掉这名干员（等于恢复） */
export function setMyDefault(my: Defaults, op: Operator, patch: { skill?: string; module?: string } | null): Defaults {
  const { [op.name]: _, ...rest } = my
  if (!patch) return rest
  const next = { ...defaultsFor(op, my), ...patch }
  const entry = {
    ...(next.skill !== op.defaultSkill && { skill: next.skill }),
    ...(op.modules?.length && next.module !== (op.defaultModule ?? '') && { module: next.module }),
  }
  return Object.keys(entry).length ? { ...rest, [op.name]: entry } : rest
}

/** 用一套阵容里写了的技能、模组更新我的默认（各阶段都算，成型阵容最后算、优先）；返回新默认和改了几名 */
export function defaultsFromLineup(l: { members: LineupMember[]; stages?: { members: LineupMember[] }[] }, ops: (name: string) => Operator, my: Defaults) {
  let next = my
  const seen = new Map<string, Operator>()
  for (const m of [...(l.stages ?? []).flatMap((s) => s.members), ...l.members]) {
    const op = ops(m.name)
    if (!op.skills?.length || (m.skill === undefined && m.module === undefined)) continue
    next = setMyDefault(next, op, { ...(m.skill !== undefined && { skill: m.skill }), ...(m.module !== undefined && { module: m.module }) })
    seen.set(op.name, op)
  }
  const same = (op: Operator) => JSON.stringify(defaultsFor(op, my)) === JSON.stringify(defaultsFor(op, next))
  return { next, changed: [...seen.values()].filter((op) => !same(op)).length }
}
