// 从仓库根目录运行：node scripts/generate-operator-options.mjs <森空岛 resource.json> <activity_table.json> <素材缓存目录>
// resource 来源：https://zonai.skland.com/web/v1/game/garrison/guide/resource
import fs from 'node:fs'
import path from 'node:path'

const read = p => JSON.parse(fs.readFileSync(p, 'utf8'))
const [resourcePath, activityPath, cache] = process.argv.slice(2)
const resource = read(resourcePath)
if (resource.code !== 0) throw new Error('森空岛资源响应失败')
const season = read(activityPath).activity.AUTOCHESS.act3autochess.V3_1
const extra = read('data/codex_extra.json')
const mock = read('src/ui/src/lib/mock/catalog.json')
// 分享码按宿主的数据顺序编码，预览目录的盟约也使用同一顺序。
const covenantIds = read('data/covenant_ids.json')
mock.covenants = read('data/covenants.json').covenants.map(c => mock.covenants.find(m => m.id === covenantIds[c.name]))
const assets = new Map()
let preset = 0
let noModule = 0
for (const operator of Object.values(extra.operators)) {
  const shop = Object.values(season.charShopChessDatas).find(s => s.charId === operator.charId)
  const charId = shop?.tmplId || operator.charId
  const char = resource.data.chars.find(c => c.charId === charId)
  if (!char || !char.skills.length) throw new Error(`缺少可选技能：${charId}`)
  operator.skills = char.skills.map(s => ({ id: s.skillId, name: s.name }))
  operator.modules = char.modules.map(m => ({ id: m.moduleId, name: m.name }))
  operator.defaultSkill = shop && shop.defaultSkillIndex >= 0
    ? operator.skills[shop.defaultSkillIndex]?.id : operator.skills.at(-1).id
  operator.defaultModule = shop?.defaultUniEquipId || operator.modules[0]?.id || null
  if (!operator.skills.some(s => s.id === operator.defaultSkill)
      || (operator.defaultModule && !operator.modules.some(m => m.id === operator.defaultModule)))
    throw new Error(`默认项不在可选列表：${charId}`)
  if (shop && shop.defaultSkillIndex >= 0) preset++
  if (!operator.modules.length) noModule++
  for (const s of char.skills) assets.set(`skills/${s.skillId}`, s.iconUrl)
  for (const m of char.modules) assets.set(`modules/${m.moduleId}`, m.icon.type)
}
for (const op of mock.operators)
  for (const key of ['skills', 'modules', 'defaultSkill', 'defaultModule']) op[key] = extra.operators[op.name][key]
fs.writeFileSync('data/codex_extra.json', JSON.stringify(extra, null, 2) + '\n')
fs.writeFileSync('src/ui/src/lib/mock/catalog.json', JSON.stringify(mock, null, 1) + '\n')
// 原始文件仅放调用方指定的调研缓存；用 convert-operator-icons.gd 转为发布用 WebP。
fs.mkdirSync(cache, { recursive: true })
const queue = [...assets]
const missing = []
await Promise.all(Array.from({ length: 6 }, async () => {
  while (queue.length) {
    const [id, url] = queue.shift()
    if (!/^(skills|modules)\/[\w[\]-]+$/.test(id) || new URL(url).hostname !== 'web.hycdn.cn')
      throw new Error(`素材路径无效：${id}`)
    const dest = path.join(cache, id + '.png')
    fs.mkdirSync(path.dirname(dest), { recursive: true })
    if (!fs.existsSync(dest)) {
      const response = await fetch(url)
      if (!response.ok) { missing.push(`${id}: HTTP ${response.status} (${url})`); continue }
      fs.writeFileSync(dest, Buffer.from(await response.arrayBuffer()))
    }
  }
}))
fs.writeFileSync(path.join(cache, 'icons.json'), JSON.stringify([...assets.keys()]))
console.log(`干员 ${Object.keys(extra.operators).length}；棋子技能默认 ${preset}；末项默认 ${Object.keys(extra.operators).length - preset}；无模组 ${noModule}；图标 ${assets.size}`)
if (missing.length) { console.error(missing.join('\n')); process.exitCode = 1 }
