// 阵容阶段（v0.7.0）：成型阵容在 Lineup 顶层，回合阶段在 Lineup.stages。设计见 docs/design.md「阵容阶段」
import type { Lineup, LineupStage } from './api/types'

/** 回合选择的上限（森空岛攻略编辑页列到 13，游戏最多 15 回合，取 14：第 15 回合一般已经是成型） */
export const MAX_ROUND = 14

export const stageLabel = (s: LineupStage) => s.name.trim() || (s.fromRound === s.toRound ? `第 ${s.fromRound} 回合` : `${s.fromRound}～${s.toRound} 回合`)

/** 阵容的全部阶段，回合阶段在前、成型阵容在最后；成型阵容的 fromRound/toRound 为 0 */
export function allStages(l: Lineup): (LineupStage & { final?: true })[] {
  return [...(l.stages ?? []), { name: '成型阵容', fromRound: 0, toRound: 0, goals: l.goals, members: l.members, equipment: l.equipment, final: true }]
}

/** 当前阶段的下标（allStages 里）：固定了就用固定的；自动时按回合找第一个包含它的回合阶段，都不包含或不知道回合就是成型阵容 */
export function currentStage(l: Lineup, fixed: number | null | undefined, round: number | null) {
  const n = l.stages?.length ?? 0
  if (fixed != null && fixed <= n) return fixed
  if (round != null) {
    const i = (l.stages ?? []).findIndex((s) => round >= s.fromRound && round <= s.toRound)
    if (i >= 0) return i
  }
  return n
}

/**
 * 目标阵容在第 i 个阶段（allStages 下标）的样子：本阶段的盟约、干员、装备，再加上后面阶段的关键干员（标成关键）。
 * 追踪、商店标记、顶栏、目标页都按它；id、名字、策略等和整套一样
 */
export function stageView(l: Lineup, i: number): Lineup {
  const all = allStages(l)
  const s = all[Math.min(i, all.length - 1)]
  const have = new Set(s.members.map((m) => m.name))
  const laterKeys = all
    .slice(i + 1)
    .flatMap((x) => x.members.filter((m) => m.key))
    .filter((m) => !have.has(m.name) && (have.add(m.name), true))
  return { ...l, goals: s.goals, members: [...s.members, ...laterKeys.map((m) => ({ name: m.name, key: true as const, transform: m.transform }))], equipment: s.equipment }
}

/** 改阵容第 i 个阶段（allStages 下标）的干员或装备，返回改好的整套阵容 */
export function editStage(l: Lineup, i: number, f: (s: { members: Lineup['members']; equipment: string[] }) => { members: Lineup['members']; equipment: string[] }): Lineup {
  const n = l.stages?.length ?? 0
  if (i >= n) return { ...l, ...f({ members: l.members, equipment: l.equipment }) }
  return { ...l, stages: l.stages!.map((s, j) => (j === i ? { ...s, ...f({ members: s.members, equipment: s.equipment }) } : s)) }
}
