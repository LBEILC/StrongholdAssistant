import minimal from '../data/catalog.json'
import type { Catalog, Lineup } from '../../ui/src/lib/api/types'
import { encodeLineup, decodeLineup } from '../../ui/src/lib/share'

// 编解码仅访问四种标识；沿用原接口，不填造完整图鉴数据。
export const catalog = minimal as unknown as Catalog
const empty: Lineup = { id: '', name: '', goals: [], members: [], equipment: [], strategyId: null, source: 'user', updatedAt: '' }
const encoded = atob(encodeLineup(empty, catalog).slice(4).replace(/-/g, '+').replace(/_/g, '/'))
export const fingerprint = (encoded.charCodeAt(1) << 8) | encoded.charCodeAt(2)
export { encodeLineup, decodeLineup }
