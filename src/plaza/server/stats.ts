import type { AdminStats, AdminRankItem, DailyCount } from '../shared/api'
import type { Context } from './auth'
import { DAY, iso, json, stmt } from './common'

export async function stats({ env }: Context) {
 const now = Date.now(), at = iso(now), offset = 8 * 3600000
 const today = Math.floor((now + offset) / DAY) * DAY - offset
 const start = iso(today - 29 * DAY), end = iso(today + DAY), week = iso(today - 6 * DAY)
 const dates = Array.from({ length: 30 }, (_, i) => iso(today + offset - (29 - i) * DAY).slice(0, 10))
 const cte = `WITH active_users AS (SELECT * FROM users WHERE deleted=0 AND banned=0),
  counted_posts AS (SELECT p.* FROM posts p JOIN active_users u ON u.id=p.user_id WHERE p.status<>'deleted')`
 const sources = {
  users: 'SELECT created_at FROM active_users',
  posts: 'SELECT created_at FROM counted_posts',
  imports: 'SELECT i.created_at FROM imports i JOIN counted_posts p ON p.id=i.post_id',
  likes: 'SELECT l.created_at FROM likes l JOIN counted_posts p ON p.id=l.post_id JOIN active_users u ON u.id=l.user_id',
  comments: `SELECT c.created_at FROM comments c JOIN counted_posts p ON p.id=c.post_id
   JOIN active_users u ON u.id=c.user_id WHERE c.status<>'deleted'`,
  incomeGames: 'SELECT created_at FROM income_samples GROUP BY device,created_at',
 }
 // TODO(design): 待处理举报遵循接口注明的现有角标口径，不因目标或举报人状态再过滤。
 const totals = `${cte} SELECT
  (SELECT COUNT(*) FROM active_users) AS users,
  (SELECT COUNT(*) FROM counted_posts) AS posts,
  (SELECT COUNT(*) FROM (${sources.imports})) AS imports,
  (SELECT COUNT(*) FROM (${sources.likes})) AS likes,
  (SELECT COUNT(*) FROM (${sources.comments})) AS comments,
  (SELECT COUNT(*) FROM favorites f JOIN counted_posts p ON p.id=f.post_id JOIN active_users u ON u.id=f.user_id) AS favorites,
  (SELECT COUNT(*) FROM active_users u WHERE EXISTS(SELECT 1 FROM cloud_prefs WHERE user_id=u.id)
   OR EXISTS(SELECT 1 FROM cloud_lineups WHERE user_id=u.id)) AS cloudSync,
  (SELECT COUNT(*) FROM (SELECT target_kind,target_id FROM reports GROUP BY target_kind,target_id
   HAVING MAX(resolved_at IS NULL)=1)) AS openReports`
 const dailyKeys = Object.keys(sources) as (keyof typeof sources)[]
 const rank = (table: 'likes' | 'imports') => stmt(env, `${cte}
  SELECT p.id,json_extract(p.lineup,'$.name') AS name,u.nickname AS author,COUNT(*) AS count
  FROM ${table} e JOIN counted_posts p ON p.id=e.post_id JOIN active_users u ON u.id=p.user_id
  ${table === 'likes' ? 'JOIN active_users actor ON actor.id=e.user_id' : ''}
  WHERE p.status='visible' AND e.created_at>=? AND e.created_at<?
  GROUP BY p.id ORDER BY count DESC,p.id LIMIT 10`, week, end)
 const results = await env.DB.batch([
  stmt(env, totals),
  ...dailyKeys.map(key => stmt(env, `${cte} SELECT date(created_at,'+8 hours') AS date,COUNT(*) AS count
   FROM (${sources[key]}) WHERE created_at>=? AND created_at<? GROUP BY date(created_at,'+8 hours')`, start, end)),
  stmt(env, `SELECT (SELECT date(MIN(created_at),'+8 hours') FROM likes) AS likes,
   (SELECT date(MIN(created_at),'+8 hours') FROM imports) AS imports`),
  rank('imports'), rank('likes'),
 ])
 const first = results[7].results[0] as { likes: string | null; imports: string | null }
 const daily = {} as AdminStats['daily']
 dailyKeys.forEach((key, i) => {
  const counts = new Map((results[i + 1].results as unknown as DailyCount[]).map(r => [r.date, r.count]))
  daily[key] = dates.map(date => ({ date, count: (key === 'likes' || key === 'imports') &&
   (first[key] === null || date < first[key]!) ? null : counts.get(date) ?? 0 }))
 })
 const ranking = (index: number): AdminRankItem[] => (results[index].results as
  { id: string; name: string; author: string; count: number }[]).map(row => ({
  post: { id: row.id, name: row.name, author: row.author }, count: row.count,
 }))
 const data: AdminStats = { totals: results[0].results[0] as unknown as AdminStats['totals'], daily,
  topImported: ranking(8), topLiked: ranking(9), at }
 return json(data)
}
