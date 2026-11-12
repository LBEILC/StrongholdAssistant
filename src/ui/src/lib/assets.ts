// 素材路径。产品里由宿主按同样的相对路径提供。
// 用绝对地址：这些路径会放进 CSS 变量里的 url()，相对路径会按样式表所在目录解析而错位
const base = new URL('./assets', document.baseURI).href

export const asset = {
  bond: (id: string) => `${base}/game/bond/${id}.png`,
  tier: (t: number) => `${base}/game/card/img_chess_level_${t}.png`,
  card: (f: string) => `${base}/game/card/${f}.png`,
  hud: (f: string) => `${base}/game/hud/${f}.png`,
  band: (f: string) => `${base}/game/band/${f}.png`,
  stage: (f: string) => `${base}/game/stageinfo/${f}.png`,
  operator: (name: string) => `${base}/data/operators/${name}.webp`,
  equipment: (name: string) => `${base}/data/equipment/${name}.webp`,
  boss: (id: string) => `${base}/data/bosses/${id}.webp`,
  enemyType: (code: string) => `${base}/data/enemy_types/${code}.webp`,
  strategy: (id: string) => `${base}/data/strategies/${id}.webp`,
  skill: (id: string) => `${base}/data/skills/${encodeURIComponent(id)}.webp`,
  module: (id: string) => `${base}/data/modules/${id}.webp`,
}

export const ROMAN = ['', 'I', 'II', 'III', 'IV', 'V', 'VI']

/** 首领血量：一百万以上取整到万（142万），一万以上保留一位（28.4万） */
export const fmtHp = (n: number) => (n >= 1e6 ? `${Math.round(n / 1e4)}万` : n >= 1e4 ? `${(n / 1e4).toFixed(1)}万` : String(n))
