// 只在本地开发时用：地址带 ?demo 时首页、详情页显示这几条示例阵容，方便看排版（构建产物里会被摇树掉）
import type { Comment, Post } from './api'
import { covenants, strategies, op } from './data'
import raw from '../../../ui/src/lib/mock/catalog.json'

const ops = (raw as unknown as { operators: { name: string; tier: number; core: string[]; extra: string[]; obtain?: string; skills?: { id: string }[] }[] }).operators
const eqs = (raw as unknown as { equipment: { name: string }[] }).equipment.map((e) => e.name)

function build(i: number, name: string, goals: { id: string; level: 1 | 2 }[], desc: string): Post {
  const members = ops
    .filter((o) => !o.obtain && goals.some((g) => o.core.includes(g.id) || o.extra.includes(g.id)))
    .sort((a, b) => b.tier - a.tier)
    .slice(0, 9 + (i % 4))
    .map((o, k) => ({
      name: o.name,
      ...(k < 2 && { key: true }),
      ...(k % 3 === 0 && { elite: true }),
      ...(k === 1 && o.skills?.[0] && { skill: o.skills[0].id, module: '' }),
      ...(k < 3 && { equipment: [{ name: eqs[(i * 7 + k * 3) % eqs.length], priority: true }, { name: eqs[(i * 5 + k * 11 + 4) % eqs.length], priority: false }] }),
    }))
  const t = new Date(Date.now() - i * 5.3 * 3600_000).toISOString()
  return {
    id: `demo-${i}`,
    code: 'SA2-演示数据不能导入',
    lineup: {
      name,
      goals,
      members,
      equipment: i % 2 ? [eqs[i + 20]] : [],
      strategyId: strategies[(i * 9) % strategies.length].id,
      // 第一条带两个回合阶段，看阶段标签
      ...(i === 0 && {
        stages: [
          { name: '', fromRound: 1, toRound: 3, goals: goals.slice(0, 1).map((g) => ({ ...g, level: 1 as const })), members: members.slice(2, 6).map(({ name }) => ({ name })), equipment: [] },
          { name: '过渡', fromRound: 4, toRound: 8, goals, members: members.slice(0, 7), equipment: [] },
        ],
      }),
    },
    description: desc,
    author: { id: 'demo', nickname: ['罗德岛干员', '叙拉古家族成员', '炎客', '萨尔贡流沙', '雪境来客', '玩家114514'][i % 6] },
    createdAt: t,
    updatedAt: t,
    featured: i === 1 || i === 4,
    likes: [128, 64, 33, 12, 7, 2][i % 6],
    imports: [420, 233, 91, 40, 18, 5][i % 6],
    comments: [12, 5, 0, 3, 1, 0][i % 6],
    liked: i === 2,
    favorited: i === 1,
    current: i !== 5,
  }
}

const c = (name: string) => covenants.find((x) => x.name === name)!.id
export const demoPosts: Post[] = [
  build(0, '叙拉古 · 家族叠层', [{ id: c('叙拉古'), level: 2 }, { id: c('坚守'), level: 1 }], '前期先抓德克萨斯和拉普兰德，四本左右凑齐叙拉古 6 人。\n关键是不要卖掉家族成员，叠层吃满再转精锐。'),
  build(1, '炎 · 元素爆发', [{ id: c('炎'), level: 2 }], '炎 6 人后元素损伤很高，配合远见打首领隐秘核心很稳。'),
  build(2, '莱塔尼亚 · 法术叠层', [{ id: c('莱塔尼亚'), level: 1 }, { id: c('远见'), level: 1 }], ''),
  build(3, '萨尔贡 · 流沙经济', [{ id: c('萨尔贡'), level: 1 }, { id: c('投资人'), level: 1 }], '经济流，前期拉利息，中期一口气升本。'),
  build(4, '谢拉格 · 不融冰', [{ id: c('谢拉格'), level: 2 }, { id: c('协防干员'), level: 1 }], '冰冻控场，适合打假想敌：茧。'),
  build(5, '上赛季的维多利亚', [{ id: c('维多利亚'), level: 1 }], '旧赛季留下的，看看就好。'),
]
void op

/** 示例评论：每个示例帖子都用这一组 */
const ago = (h: number) => new Date(Date.now() - h * 3600_000).toISOString()
export const demoComments: Comment[] = [
  { id: 'dc-4', postId: '', author: { id: 'demo-a', nickname: '叙拉古家族成员' }, body: '德克萨斯一定要留到最后吗？我经常中期被迫卖掉。', replyTo: { id: 'dc-2', nickname: '罗德岛干员' }, createdAt: ago(0.4), canDelete: false },
  { id: 'dc-3', postId: '', author: { id: 'demo-b', nickname: '雪境来客' }, body: '照抄打到了规程 16，首领隐秘核心那波差点漏。\n建议后期补一个远见。', replyTo: null, createdAt: ago(3), canDelete: true },
  { id: 'dc-2', postId: '', author: { id: 'demo', nickname: '罗德岛干员' }, body: '关键是叠层别断，装备优先给拉普兰德。', replyTo: null, createdAt: ago(7), canDelete: false },
  { id: 'dc-1', postId: '', author: { id: 'demo-c', nickname: '玩家114514' }, body: '收藏了', replyTo: null, createdAt: ago(30), canDelete: false },
]
