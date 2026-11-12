// 历史记录与统计（v0.7.0，设计见 docs/design.md「历史记录与统计」）。数据在宿主，统计在这里算
import { api } from './api'
import type { MatchRecord, SklandRecord } from './api/types'

export const MODE: Record<string, string> = { MULTI: '联机', SINGLE: '单人' }
// 和游戏数据的模式编号一致：mode_multi_funny 标准模拟、normal 险境、hard 绝境、abyss 终极
export const DIFFICULTY: Record<string, string> = { FUNNY: '标准', NORMAL: '险境', HARD: '绝境', ABYSS: '终极' }

/** 结果文字：通关 / 8 回合中断 / 开局中断（游戏里显示「[8] 模拟中断」） */
export const resultText = (s: SklandRecord) => (s.success ? '通关' : s.passRound ? `${s.passRound} 回合中断` : '开局中断')

export type RangeId = '7' | '30' | 'all'
export type ModeId = 'all' | 'MULTI' | 'SINGLE'

class History {
  records = $state<MatchRecord[] | null>(null)
  /** 正在从森空岛更新 */
  syncing = $state(false)
  /** 本次启动已经自动更新过（每次启动最多一次） */
  private autoSynced = false

  async load() {
    this.records = await api.history()
    if (this.autoSynced) return
    this.autoSynced = true
    this.sync(true)
  }
  /** 从森空岛更新。silent：历史页打开时自动更新，没登录就算了，不弹窗、不提示 */
  async sync(silent = false) {
    if (this.syncing) return
    this.syncing = true
    try {
      const got = await api.sklandHistory({ silent })
      if (got) this.records = await api.saveSklandRecords(got)
      return got
    } finally {
      this.syncing = false
    }
  }
  async remove(id: string) {
    await api.deleteRecord(id)
    this.records = this.records!.filter((r) => r.id !== id)
  }
}
export const history = new History()

/** 一局的模式文字，如「联机 · 绝境」；只有本地记录时用开局页读到的模式名 */
export function modeText(r: MatchRecord) {
  if (r.skland) return [MODE[r.skland.mode] ?? r.skland.mode, DIFFICULTY[r.skland.difficulty] ?? r.skland.difficulty].join(' · ')
  return [r.local!.solo ? '单人' : '联机', r.local!.mode?.name].filter(Boolean).join(' · ')
}

export interface Stats {
  total: number
  wins: number
  avgRound: number
  avgSpent: number
  strategies: { id: string; n: number; wins: number }[]
  operators: { id: string; n: number; wins: number }[]
  /** 最近 20 局，旧到新 */
  trend: SklandRecord[]
}

/** 只算有结果（对上了森空岛战绩）的局 */
export function stats(records: MatchRecord[], mode: ModeId, range: RangeId): Stats {
  const since = range === 'all' ? 0 : Date.now() - Number(range) * 86400_000
  const rs = records
    .flatMap((r) => (r.skland ? [r.skland] : []))
    .filter((s) => (mode === 'all' || s.mode === mode) && Date.parse(s.time) >= since)
    .sort((a, b) => a.time.localeCompare(b.time))
  const group = (keys: (s: SklandRecord) => string[]) => {
    const m = new Map<string, { id: string; n: number; wins: number }>()
    for (const s of rs)
      for (const id of new Set(keys(s))) {
        const g = m.get(id) ?? m.set(id, { id, n: 0, wins: 0 }).get(id)!
        g.n++
        if (s.success) g.wins++
      }
    return [...m.values()].sort((a, b) => b.n - a.n || b.wins - a.wins)
  }
  const avg = (f: (s: SklandRecord) => number) => (rs.length ? rs.reduce((n, s) => n + f(s), 0) / rs.length : 0)
  return {
    total: rs.length,
    wins: rs.filter((s) => s.success).length,
    avgRound: avg((s) => s.passRound),
    avgSpent: avg((s) => s.coinSpent),
    strategies: group((s) => (s.strategyId ? [s.strategyId] : [])).slice(0, 8),
    operators: group((s) => s.operators.map((o) => o.id)).slice(0, 20),
    trend: rs.slice(-20),
  }
}

/**
 * 我的默认（v0.7.0）：按森空岛战绩里每名干员用得最多的技能、模组生成（只看有技能记录的干员）。
 * 返回干员名 → { skill, module }，模组没带记成 ''
 */
export function defaultsFromHistory(records: MatchRecord[], byChar: Map<string, { name: string }>) {
  const count = new Map<string, Map<string, number>>()
  for (const r of records)
    for (const o of r.skland?.operators ?? []) {
      const name = byChar.get(o.id)?.name
      if (!name || !o.skill) continue
      const k = `${o.skill}|${o.module ?? ''}`
      const m = count.get(name) ?? count.set(name, new Map()).get(name)!
      m.set(k, (m.get(k) ?? 0) + 1)
    }
  return Object.fromEntries(
    [...count].map(([name, m]) => {
      const [skill, module] = [...m].sort((a, b) => b[1] - a[1])[0][0].split('|')
      return [name, { skill, module }]
    }),
  )
}

export const pct = (wins: number, n: number) => (n ? `${Math.round((wins / n) * 100)}%` : '—')
