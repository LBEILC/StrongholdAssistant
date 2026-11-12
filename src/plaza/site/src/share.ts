// 网页编辑器用的分享码编解码：直接用助手的 share.ts，数据用广场后端同一份当期数据（顺序决定序号和指纹），
// 保证网页上编出来的码和助手、后端完全一致
import { encodeLineup, decodeLineup } from '../../../ui/src/lib/share'
import type { Catalog, Lineup } from '../../../ui/src/lib/api/types'
import raw from '../../data/catalog.json'

const catalog = raw as unknown as Catalog

export type { Lineup }
export const encode = (l: Lineup) => encodeLineup(l, catalog)
export const decode = (text: string) => decodeLineup(text, catalog)
/** 后端当期数据里有的名字（网页编辑器只让选这些） */
export const known = {
  operators: new Set(catalog.operators.map((o) => o.name)),
  equipment: new Set(catalog.equipment.map((e) => e.name)),
}
