// 阵容分享码：把一套阵容编成一串短码，复制粘贴就能导入，不经过任何服务器。
// 码里存目录序号与当期数据指纹；历史目录的序号全部保留时也能导入。
//
// 格式：「SA<版本>-」+ base64url(字节)。字节依次是：
//   版本 | 指纹 2 字节 | 名字字节数 + UTF-8 名字 | 一段阵容（成型阵容）| 策略序号（255 = 没选）
//   一段阵容 = 目标盟约个数 + 每个 (档位-1)<<7 | 盟约序号 | 干员个数 + 每个干员序号 + 关键标记位图（每 8 名 1 字节）
//             | 通用装备个数 + 每个装备序号
//   SA2（v0.5.0）在后面接着，按干员顺序每名一段：候选装备个数 + 每个装备序号 + 优先标记位图（每 8 件 1 字节）
//   SA3（v0.7.0）里「一段阵容」在通用装备后面多一段：每名干员 1 字节 = 精锐 1 位 | 技能 2 位 | 模组 3 位
//             （技能 0 = 默认、k = skills[k-1]；模组 0 = 默认、1 = 不带、k = modules[k-2]）
//   SA3（v0.7.0，阵容阶段）在 SA2 那段后面接着：回合阶段个数，每个阶段 = 起始回合 + 结束回合 + 名字字节数 + 名字
//             + 一段阵容 + 这段阵容的候选装备（同 SA2）
//   SA4（v1.0.1，「一组」干员和升本计划）在 SA3 那段后面接着：成型阵容的组 + 每个阶段的组（按阶段顺序）+ 升本计划。
//             组 = 组个数 + 每组（need + 干员个数 + 每个干员序号 + 关键标记位图 + 每名 1 字节精锐技能模组 + 候选装备（同 SA2））；
//             升本计划 = 步数 + 每步（回合 + 等级）
// 有回合阶段才编 SA3，有组或升本计划才编 SA4；没有阶段、没有候选装备时仍编 SA1，旧版助手也能导入。
//
// 指纹只算普通干员：自选干员（Operator.diy，v0.7.0）排在干员表最后，不进指纹，所以加了自选干员后指纹不变，
// 新旧版本的码互相能导；码里用到自选干员时，旧版助手会提示码不完整。
import type { Catalog, Lineup, LineupGroup, LineupMember, LineupStage, CovenantGoal } from './api'
import history from './share-history.json'

const VERSION = 4
const MAX_NAME = 60

/** 当期数据的指纹：盟约 id、普通干员名、装备名、策略 id 按顺序算 FNV-1a，取低 16 位。序号就是按这个顺序数的 */
export function fingerprint(c: Catalog) {
  const text = [c.covenants.map((x) => x.id), c.operators.filter((x) => !x.diy).map((x) => x.name), c.equipment.map((x) => x.name), c.strategies.map((x) => x.id)]
    .flat()
    .join('|')
  let h = 0x811c9dc5
  for (const b of new TextEncoder().encode(text)) h = Math.imul(h ^ b, 0x01000193)
  return h & 0xffff
}

/** 历史目录按完整的编码顺序比较，包含不参与指纹计算的自选干员。 */
export function compatibleFingerprints(c: Catalog) {
  const current = new Set([fingerprint(c)])
  for (const [hex, old] of Object.entries(history)) {
    const fp = parseInt(hex, 16)
    if (old.covenants.every((id, i) => c.covenants[i]?.id === id)
      && old.operators.every((name, i) => c.operators[i]?.name === name)
      && old.equipment.every((name, i) => c.equipment[i]?.name === name)
      && old.strategies.every((id, i) => c.strategies[i]?.id === id)) current.add(fp)
  }
  return current
}

const toBase64Url = (bytes: Uint8Array) => btoa(String.fromCharCode(...bytes)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
const fromBase64Url = (s: string) => Uint8Array.from(atob(s.replace(/-/g, '+').replace(/_/g, '/')), (ch) => ch.charCodeAt(0))

/** 名字转 UTF-8，超长就按字截短（不截断半个汉字） */
function utf8(text: string, max: number) {
  let chars = [...text]
  let out = new TextEncoder().encode(text)
  while (out.length > max) out = new TextEncoder().encode((chars = chars.slice(0, -1)).join(''))
  return out
}

type Part = { goals: CovenantGoal[]; members: LineupMember[]; equipment: string[] }
type OperatorConfigs = Record<string, { skills: { id: string }[]; modules: { id: string }[] }>
const historyByFingerprint: Record<string, { operatorConfigs?: OperatorConfigs }> = history

export function encodeLineup(l: Lineup, c: Catalog): string {
  const cov = new Map(c.covenants.map((x, i) => [x.id, i]))
  const op = new Map(c.operators.map((x, i) => [x.name, i]))
  const eq = new Map(c.equipment.map((x, i) => [x.name, i]))
  const bits = (flags: boolean[]) => {
    const out = new Uint8Array(Math.ceil(flags.length / 8))
    flags.forEach((f, i) => f && (out[i >> 3] |= 1 << (i & 7)))
    return out
  }
  const cfg = (m: LineupMember) => {
    const o = c.operators[op.get(m.name)!]
    const sk = m.skill ? (o.skills?.findIndex((x) => x.id === m.skill) ?? -1) + 1 : 0
    const mi = o.modules?.findIndex((x) => x.id === m.module) ?? -1
    const md = m.module === undefined ? 0 : m.module === '' ? 1 : mi < 0 ? 0 : mi + 2
    return (m.elite ? 1 : 0) | (sk << 1) | (md << 3)
  }
  const stages = l.stages ?? []
  const v4 = !!(l.groups?.length || l.upgrades?.length || stages.some((s) => s.groups?.length))
  const v = v4 ? 4 : stages.length || l.members.some((m) => m.elite || m.skill || m.module !== undefined) ? 3 : l.members.some((m) => m.equipment?.length) ? 2 : 1
  const part = (p: Part) => [
    p.goals.length, ...p.goals.map((g) => ((g.level - 1) << 7) | cov.get(g.id)!),
    p.members.length, ...p.members.map((m) => op.get(m.name)!), ...bits(p.members.map((m) => !!m.key)),
    p.equipment.length, ...p.equipment.map((n) => eq.get(n)!),
    ...(v >= 3 ? p.members.map(cfg) : []),
  ]
  const memberEq = (p: { members: LineupMember[] }) =>
    p.members.flatMap((m) => {
      const list = m.equipment ?? []
      return [list.length, ...list.map((x) => eq.get(x.name)!), ...bits(list.map((x) => x.priority))]
    })
  const groups = (list: LineupGroup[] = []) => [
    list.length,
    ...list.flatMap((g) => [g.need, g.members.length, ...g.members.map((m) => op.get(m.name)!), ...bits(g.members.map((m) => !!m.key)), ...g.members.map(cfg), ...memberEq(g)]),
  ]
  const name = utf8(l.name, MAX_NAME)
  const fp = fingerprint(c)
  const st = l.strategyId ? c.strategies.findIndex((s) => s.id === l.strategyId) : 255
  const bytes = [
    v, fp >> 8, fp & 0xff,
    name.length, ...name,
    ...part(l),
    st,
    ...(v >= 2 ? memberEq(l) : []),
    ...(v >= 3
      ? [stages.length, ...stages.flatMap((s) => {
          const sn = utf8(s.name, 30)
          return [s.fromRound, s.toRound, sn.length, ...sn, ...part(s), ...memberEq(s)]
        })]
      : []),
    ...(v >= 4
      ? [...groups(l.groups), ...stages.flatMap((s) => groups(s.groups)), (l.upgrades ?? []).length, ...(l.upgrades ?? []).flatMap((u) => [u.round, u.level])]
      : []),
  ]
  return `SA${v}-` + toBase64Url(Uint8Array.from(bytes))
}

/** 复制到剪贴板的整段文字：带上名字，贴到群里也看得懂是什么 */
export const shareText = (l: Lineup, c: Catalog) => `【高效雅雅阵容】${l.name} ${encodeLineup(l, c)}`

export type Decoded = { ok: true; lineup: Lineup; fingerprint: number } | { ok: false; error: string }

/** 从一段文字里找出分享码并解出阵容；文字可以是整段转发的消息 */
export function decodeLineup(text: string, c: Catalog): Decoded {
  const code = text.match(/SA(\d+)-([A-Za-z0-9_-]+)/)
  if (!code) return { ok: false, error: '没找到分享码。分享码以「SA」加数字开头，如「SA3-」。' }
  const v = Number(code[1])
  if (v < 1 || v > VERSION) return { ok: false, error: '这个分享码来自更新版本的助手，先把助手更新到最新版。' }
  let b: Uint8Array
  try {
    b = fromBase64Url(code[2])
  } catch {
    return { ok: false, error: '分享码不完整，复制时可能少了几个字符。' }
  }
  let p = 0
  let oldConfigs: OperatorConfigs | undefined
  const next = () => {
    if (p >= b.length) throw new Error()
    return b[p++]
  }
  const text8 = (n: number) => {
    const s = new TextDecoder().decode(b.subarray(p, (p += n)))
    if (p > b.length) throw new Error()
    return s
  }
  const pick = <T,>(list: T[], i: number) => {
    const x = list[i]
    if (!x) throw new Error()
    return x
  }
  const part = (): Part => {
    const goals = Array.from({ length: next() }, () => {
      const v = next()
      return { id: pick(c.covenants, v & 0x7f).id, level: (((v >> 7) + 1) as 1 | 2) }
    })
    const members = memberList()
    const equipment = Array.from({ length: next() }, () => pick(c.equipment, next()).name)
    if (v >= 3) config(members)
    return { goals, members, equipment }
  }
  const memberList = (): LineupMember[] => {
    const names = Array.from({ length: next() }, () => pick(c.operators, next()).name)
    const keys = Array.from({ length: Math.ceil(names.length / 8) }, next)
    return names.map((n, i) => ({ name: n, ...((keys[i >> 3] >> (i & 7)) & 1 && { key: true }) }))
  }
  // 精锐、技能、模组：广场目录不带技能模组时只留精锐
  const config = (members: LineupMember[]) => {
      for (const m of members) {
        const x = next()
        // 同一名干员的技能或模组序号修订过时，按发码时的目录还原。
        const o = oldConfigs?.[m.name] ?? c.operators.find((o) => o.name === m.name)!
        if (x & 1) m.elite = true
        const sk = (x >> 1) & 3
        const md = x >> 3
        if (sk && o.skills) m.skill = pick(o.skills, sk - 1).id
        if (md === 1) m.module = ''
        else if (md && o.modules) m.module = pick(o.modules, md - 2).id
      }
  }
  const memberEq = (members: LineupMember[]) => {
    for (const m of members) {
      const list = Array.from({ length: next() }, () => pick(c.equipment, next()).name)
      const pri = Array.from({ length: Math.ceil(list.length / 8) }, next)
      if (list.length) m.equipment = list.map((name, i) => ({ name, priority: !!((pri[i >> 3] >> (i & 7)) & 1) }))
    }
  }
  try {
    if (next() !== v) throw new Error()
    const fp = (next() << 8) | next()
    if (!compatibleFingerprints(c).has(fp)) {
      return { ok: false, error: '这个分享码来自其他赛季或其他版本的数据，当前数据里对不上。双方都更新到最新版再试。' }
    }
    oldConfigs = historyByFingerprint[fp.toString(16).padStart(4, '0')]?.operatorConfigs
    const name = text8(next())
    const final = part()
    const si = next()
    const strategyId = si === 255 ? null : c.strategies[si]?.id
    if (strategyId === undefined) throw new Error()
    if (v >= 2) memberEq(final.members)
    let stages: LineupStage[] | undefined
    if (v >= 3) {
      stages = Array.from({ length: next() }, () => {
        const fromRound = next()
        const toRound = next()
        const sn = text8(next())
        const s = part()
        memberEq(s.members)
        return { name: sn, fromRound, toRound, ...s }
      })
    }
    let groups: LineupGroup[] = []
    let upgrades: Lineup['upgrades'] = []
    if (v >= 4) {
      const readGroups = (): LineupGroup[] =>
        Array.from({ length: next() }, () => {
          const need = next()
          const members = memberList()
          config(members)
          memberEq(members)
          return { need, members }
        })
      groups = readGroups()
      for (const s of stages!) {
        const g = readGroups()
        if (g.length) s.groups = g
      }
      upgrades = Array.from({ length: next() }, () => ({ round: next(), level: next() }))
    }
    if (p !== b.length) throw new Error()
    return {
      ok: true,
      fingerprint: fp,
      lineup: {
        id: '', name, ...final, strategyId, source: 'user',
        ...(stages?.length && { stages }), ...(groups.length && { groups }), ...(upgrades.length && { upgrades }),
        updatedAt: new Date().toISOString(),
      },
    }
  } catch {
    return { ok: false, error: '分享码不完整或被改动过，解不出阵容。' }
  }
}
