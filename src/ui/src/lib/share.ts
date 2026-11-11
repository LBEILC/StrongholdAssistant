// 阵容分享码：把一套阵容编成一串短码，复制粘贴就能导入，不经过任何服务器。
// 码里只放盟约、干员、装备在当期数据里的序号，再带一个当期数据的指纹；指纹对不上（换赛季、换数据）就不导入。
//
// 格式：「SA1-」+ base64url(字节)。字节依次是：
//   版本 1 | 指纹 2 字节 | 名字字节数 + UTF-8 名字 | 目标盟约个数 + 每个 (档位-1)<<7 | 盟约序号
//   | 干员个数 + 每个干员序号 + 关键标记位图（每 8 名 1 字节） | 装备个数 + 每个装备序号 | 策略序号（255 = 没选）
import type { Catalog, Lineup } from './api'

const PREFIX = 'SA1-'
const VERSION = 1
const MAX_NAME = 60

/** 当期数据的指纹：盟约 id、干员名、装备名、策略 id 按顺序算 FNV-1a，取低 16 位。序号就是按这个顺序数的 */
function fingerprint(c: Catalog) {
  const text = [c.covenants.map((x) => x.id), c.operators.map((x) => x.name), c.equipment.map((x) => x.name), c.strategies.map((x) => x.id)]
    .flat()
    .join('|')
  let h = 0x811c9dc5
  for (const b of new TextEncoder().encode(text)) h = Math.imul(h ^ b, 0x01000193)
  return h & 0xffff
}

const toBase64Url = (bytes: Uint8Array) => btoa(String.fromCharCode(...bytes)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
const fromBase64Url = (s: string) => Uint8Array.from(atob(s.replace(/-/g, '+').replace(/_/g, '/')), (ch) => ch.charCodeAt(0))

export function encodeLineup(l: Lineup, c: Catalog): string {
  const cov = new Map(c.covenants.map((x, i) => [x.id, i]))
  const op = new Map(c.operators.map((x, i) => [x.name, i]))
  const eq = new Map(c.equipment.map((x, i) => [x.name, i]))
  // 名字超长就按字截短（不截断半个汉字）
  let chars = [...l.name]
  let name = new TextEncoder().encode(l.name)
  while (name.length > MAX_NAME) name = new TextEncoder().encode((chars = chars.slice(0, -1)).join(''))
  const fp = fingerprint(c)
  const st = l.strategyId ? c.strategies.findIndex((s) => s.id === l.strategyId) : 255
  const keys = new Uint8Array(Math.ceil(l.members.length / 8))
  l.members.forEach((m, i) => m.key && (keys[i >> 3] |= 1 << (i & 7)))
  const bytes = [
    VERSION, fp >> 8, fp & 0xff,
    name.length, ...name,
    l.goals.length, ...l.goals.map((g) => ((g.level - 1) << 7) | cov.get(g.id)!),
    l.members.length, ...l.members.map((m) => op.get(m.name)!), ...keys,
    l.equipment.length, ...l.equipment.map((n) => eq.get(n)!),
    st,
  ]
  return PREFIX + toBase64Url(Uint8Array.from(bytes))
}

/** 复制到剪贴板的整段文字：带上名字，贴到群里也看得懂是什么 */
export const shareText = (l: Lineup, c: Catalog) => `【卫戍协议助手阵容】${l.name} ${encodeLineup(l, c)}`

export type Decoded = { ok: true; lineup: Lineup } | { ok: false; error: string }

/** 从一段文字里找出分享码并解出阵容；文字可以是整段转发的消息 */
export function decodeLineup(text: string, c: Catalog): Decoded {
  const code = text.match(/SA(\d+)-([A-Za-z0-9_-]+)/)
  if (!code) return { ok: false, error: '没找到分享码。分享码以「SA1-」开头。' }
  if (code[1] !== String(VERSION)) return { ok: false, error: '这个分享码来自更新版本的助手，先把助手更新到最新版。' }
  let b: Uint8Array
  try {
    b = fromBase64Url(code[2])
  } catch {
    return { ok: false, error: '分享码不完整，复制时可能少了几个字符。' }
  }
  let p = 0
  const next = () => {
    if (p >= b.length) throw new Error()
    return b[p++]
  }
  try {
    if (next() !== VERSION) throw new Error()
    if (((next() << 8) | next()) !== fingerprint(c)) {
      return { ok: false, error: '这个分享码来自其他赛季或其他版本的数据，当前数据里对不上。双方都更新到最新版再试。' }
    }
    const nameLen = next()
    const name = new TextDecoder().decode(b.subarray(p, (p += nameLen)))
    if (p > b.length) throw new Error()
    const goals = Array.from({ length: next() }, () => {
      const v = next()
      const cv = c.covenants[v & 0x7f]
      if (!cv) throw new Error()
      return { id: cv.id, level: (((v >> 7) + 1) as 1 | 2) }
    })
    const names = Array.from({ length: next() }, () => {
      const o = c.operators[next()]
      if (!o) throw new Error()
      return o.name
    })
    const keys = Array.from({ length: Math.ceil(names.length / 8) }, next)
    const members = names.map((n, i) => ({ name: n, ...((keys[i >> 3] >> (i & 7)) & 1 && { key: true }) }))
    const equipment = Array.from({ length: next() }, () => {
      const e = c.equipment[next()]
      if (!e) throw new Error()
      return e.name
    })
    const si = next()
    const strategyId = si === 255 ? null : c.strategies[si]?.id
    if (strategyId === undefined || p !== b.length) throw new Error()
    return { ok: true, lineup: { id: '', name, goals, members, equipment, strategyId, source: 'user', updatedAt: new Date().toISOString() } }
  } catch {
    return { ok: false, error: '分享码不完整或被改动过，解不出阵容。' }
  }
}
