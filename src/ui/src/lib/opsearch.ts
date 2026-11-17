// 群里的叫法：暴躁铁皮的技能叫「大丑的愤怒」，玩家都叫它大丑
const NICKNAMES: Record<string, string[]> = { 暴躁铁皮: ['大丑'] }

/** 搜干员：名字或外号包含关键字就算（空关键字全算） */
export const opMatches = (name: string, q: string) => {
  const k = q.trim().toLowerCase()
  return !k || name.toLowerCase().includes(k) || (NICKNAMES[name] ?? []).some((n) => n.includes(k))
}
