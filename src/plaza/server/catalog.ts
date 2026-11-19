import minimal from '../data/catalog.json'
import covenantIds from '../../../data/covenant_ids.json'
import type { Catalog } from '../../ui/src/lib/api/types'
import { encodeLineup, decodeLineup, fingerprint as catalogFingerprint, compatibleFingerprints } from '../../ui/src/lib/share'

// 编解码仅访问四种标识；沿用原接口，不填造完整图鉴数据。
export const catalog = minimal as unknown as Catalog
export const covenantNames = Object.entries(covenantIds).filter(([, id]) => minimal.covenants.some(c => c.id === id))
export const fingerprint = catalogFingerprint(catalog)
export const currentFingerprints = compatibleFingerprints(catalog)
export { encodeLineup, decodeLineup }
