// 网站显示用的当期数据：盟约名、干员星级与盟约、装备星级、策略。取自助手界面打包的图鉴数据，
// 和助手里看到的一致。素材路径和助手相同（构建时把助手的 public/assets 一起发布）。
import raw from '../../../ui/src/lib/mock/catalog.json'

interface Covenant {
  id: string
  name: string
  kind: 'core' | 'extra'
  thresholds: number[]
}
interface Operator {
  name: string
  tier: number
  core: string[]
  extra: string[]
  obtain?: string
  /** 可选技能、模组和数据默认（v0.7.0） */
  skills?: { id: string; name: string }[]
  modules?: { id: string; name: string }[]
  defaultSkill?: string
  defaultModule?: string | null
}
interface Equipment {
  name: string
  tier: number
}
interface Strategy {
  id: string
  name: string
  initiator: string
  description: string
}

const c = raw as unknown as { covenants: Covenant[]; operators: Operator[]; equipment: Equipment[]; strategies: Strategy[] }

export const covenants = c.covenants
export const strategies = c.strategies
const covById = new Map(c.covenants.map((x) => [x.id, x]))
const opByName = new Map(c.operators.map((x) => [x.name, x]))
const eqByName = new Map(c.equipment.map((x) => [x.name, x]))
const stById = new Map(c.strategies.map((x) => [x.id, x]))

export const cov = (id: string) => covById.get(id) ?? { id, name: id, kind: 'extra' as const, thresholds: [] }
export const op = (name: string): Operator => opByName.get(name) ?? { name, tier: 1, core: [], extra: [] }
export const eq = (name: string) => eqByName.get(name) ?? { name, tier: 1 }
export const strategy = (id: string | null) => (id ? stById.get(id) : undefined)

export const asset = {
  bond: (id: string) => `/assets/game/bond/${id}.png`,
  tier: (t: number) => `/assets/game/card/img_chess_level_${t}.png`,
  operator: (name: string) => `/assets/data/operators/${encodeURIComponent(name)}.webp`,
  equipment: (name: string) => `/assets/data/equipment/${encodeURIComponent(name)}.webp`,
  strategy: (id: string) => `/assets/data/strategies/${encodeURIComponent(id)}.webp`,
  skill: (id: string) => `/assets/data/skills/${encodeURIComponent(id)}.webp`,
  module: (id: string) => `/assets/data/modules/${id}.webp`,
  card: (f: string) => `/assets/game/card/${f}.png`,
}

/** 帖子里这名干员实际用的技能、模组：没写就是数据默认（看的是作者的配置，不用谁的「我的默认」）；module '' = 不带 */
export function config(m: { name: string; skill?: string; module?: string }) {
  const o = op(m.name)
  if (!o.skills?.length) return null
  const sid = m.skill ?? o.defaultSkill
  const mid = m.module ?? o.defaultModule ?? ''
  return { skill: o.skills.find((k) => k.id === sid), module: o.modules?.find((k) => k.id === mid), hasModules: !!o.modules?.length }
}

export const ROMAN = ['', 'I', 'II', 'III', 'IV', 'V', 'VI']

/** 「3 天前」这类相对时间；超过 30 天写日期 */
export function ago(iso: string) {
  const s = (Date.now() - Date.parse(iso)) / 1000
  if (s < 60) return '刚刚'
  if (s < 3600) return `${Math.floor(s / 60)} 分钟前`
  if (s < 86400) return `${Math.floor(s / 3600)} 小时前`
  if (s < 86400 * 30) return `${Math.floor(s / 86400)} 天前`
  return new Date(iso).toLocaleDateString('zh-CN')
}
