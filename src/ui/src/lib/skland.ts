// 从森空岛导入（v0.7.0）：认出攻略链接，把攻略的一个阶段或历史战绩里的一局换成本地阵容。读取由宿主做
import { ELITE_COVENANT } from './opconfig'
import type { Catalog, CovenantGoal, Lineup, LineupMember, SklandGuide, SklandRecord, SklandStage } from './api/types'

const LINK = /https:\/\/game\.skland\.com\/arknights\/stronghold-guide\/detail\?[^\s]*?guideId=\d+[^\s]*/

/** 粘贴的文字里的森空岛攻略链接（整句分享消息也行）；没有就是 null */
export const sklandLink = (text: string) => text.match(LINK)?.[0] ?? null

const cut = (s: string, n: number) => ([...s].length > n ? [...s].slice(0, n - 1).join('') + '…' : s)

/** 阶段名：已知类型用中文，其他按顺序叫「阶段 N」，有回合范围时带上 */
export function stageName(s: SklandStage, i: number) {
  const name = s.type === 'FORMATION' ? '成型阵容' : `阶段 ${i + 1}`
  return s.startRound || s.endRound ? `${name}（${s.startRound || '?'}～${s.endRound || '?'} 回合）` : name
}

export interface SklandImport {
  lineup: Lineup
  /** 目录里找不到的干员、装备个数（新赛季或助手数据旧了） */
  unknown: { operators: number; equipment: number }
}

/** 森空岛编号 → 目录：干员 charId → 干员，装备道具编号（含别名）→ 装备名 */
export const charMap = (catalog: Catalog) => new Map(catalog.operators.map((o) => [o.charId, o]))
export const itemMap = (catalog: Catalog) => new Map(catalog.equipment.flatMap((e) => [e.itemId, ...(e.itemAliases ?? [])].map((id) => [id, e.name] as const)))

type SkOp = { id: string; elite: boolean; skill?: string; module?: string }

/**
 * 干员、装备、策略编号换成本地阵容；目标盟约按干员人数和各档门槛自己算，到哪档设哪档。
 * 精锐、技能、模组原样带上（v0.7.0）；和数据默认一样的技能、模组不写
 */
function build(catalog: Catalog, opList: SkOp[], itemIds: string[], strategyId: string | null, name: string, description: string): SklandImport {
  const byChar = charMap(catalog)
  const byItem = itemMap(catalog)
  const found = opList.flatMap((x) => {
    const o = byChar.get(x.id)
    return o ? [{ o, x }] : []
  })
  const ops = found.map((f) => f.o)
  const seen = new Set<string>()
  const members: LineupMember[] = found
    .filter(({ o }) => !seen.has(o.name) && (seen.add(o.name), true))
    .map(({ o, x }) => ({
      name: o.name,
      ...(x.elite && { elite: true as const }),
      ...(x.skill && x.skill !== o.defaultSkill && o.skills?.some((k) => k.id === x.skill) && { skill: x.skill }),
      ...(x.module && x.module !== o.defaultModule && o.modules?.some((k) => k.id === x.module) && { module: x.module }),
    }))
  const equipment = itemIds.map((id) => byItem.get(id)).filter((n): n is string => !!n)

  const goals: CovenantGoal[] = []
  for (const c of catalog.covenants) {
    const have = c.id === ELITE_COVENANT ? members.filter((m) => m.elite).length : ops.filter((o) => o.core.includes(c.id) || o.extra.includes(c.id)).length
    const level = c.thresholds.filter((t) => have >= t).length
    if (level) goals.push({ id: c.id, level: Math.min(level, 2) as 1 | 2 })
  }
  return {
    lineup: {
      id: '',
      name: cut(name, 30),
      goals,
      members,
      equipment: [...new Set(equipment)],
      strategyId: catalog.strategies.some((s) => s.id === strategyId) ? strategyId : null,
      source: 'user',
      description: cut(description, 500),
      updatedAt: new Date().toISOString(),
    },
    unknown: { operators: opList.length - ops.length, equipment: itemIds.length - equipment.length },
  }
}

/**
 * 整篇攻略换成一套阵容（v0.7.0）：成型阵容（FORMATION，没有就用最后一页）放顶层，其余带回合的页签变成回合阶段，
 * 按回合先后
 */
export function guideToLineup(guide: SklandGuide, catalog: Catalog): SklandImport {
  const f = guide.stages.findIndex((s) => s.type === 'FORMATION')
  const fi = f >= 0 ? f : guide.stages.length - 1
  const final = stageToLineup(guide, fi, catalog)
  const rounds = guide.stages
    .map((s, i) => ({ s, i }))
    .filter(({ s, i }) => i !== fi && s.startRound > 0)
    .sort((a, b) => a.s.startRound - b.s.startRound)
    .map(({ s, i }) => ({ s, r: stageToLineup(guide, i, catalog) }))
  if (!rounds.length) return final
  return {
    lineup: {
      ...final.lineup,
      stages: rounds.map(({ s, r }) => ({ name: '', fromRound: s.startRound, toRound: Math.max(s.endRound, s.startRound), goals: r.lineup.goals, members: r.lineup.members, equipment: r.lineup.equipment })),
    },
    unknown: {
      operators: final.unknown.operators + rounds.reduce((n, x) => n + x.r.unknown.operators, 0),
      equipment: final.unknown.equipment + rounds.reduce((n, x) => n + x.r.unknown.equipment, 0),
    },
  }
}

export function stageToLineup(guide: SklandGuide, i: number, catalog: Catalog): SklandImport {
  const stage = guide.stages[i]
  const strategies = guide.strategies.map((id) => catalog.strategies.find((s) => s.id === id)).filter((s) => !!s)
  const description = [
    stage.notes.trim(),
    strategies.length > 1 && `备选策略：${strategies.map((s) => s.name).join('、')}`,
    `来自森空岛 · ${guide.author}${guide.stages.length > 1 ? ` · ${stageName(stage, i)}` : ''}`,
  ]
    .filter(Boolean)
    .join('\n\n')
  return build(catalog, stage.operators, stage.equipment, guide.strategies[0] ?? null, guide.title, description)
}

const MODE: Record<string, string> = { MULTI: '联机', SINGLE: '单人' }
/** 历史战绩一局的标题，如「联机 · 8 回合中断」 */
export const recordTitle = (r: SklandRecord) =>
  `${MODE[r.mode] ?? r.mode} · ${r.success ? '通关' : r.passRound ? `${r.passRound} 回合中断` : '开局中断'}`

export function recordToLineup(r: SklandRecord, catalog: Catalog): SklandImport {
  const day = new Date(r.time).toLocaleDateString('zh-CN')
  return build(catalog, r.operators, r.equipment, r.strategyId, `${day} ${recordTitle(r)}`, `来自森空岛历史战绩 · ${day} · ${recordTitle(r)}`)
}
