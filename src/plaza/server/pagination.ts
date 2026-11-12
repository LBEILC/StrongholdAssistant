import { fail } from './common'

export function pagination(q: URLSearchParams, key: string) {
 const limit = Math.min(50, Number(q.get('limit') ?? 20))
 if (!Number.isInteger(limit) || limit < 1) fail(400, 'bad_query', '分页参数不正确。')
 let after: { time: string; id: string } | null = null
 if (q.has('cursor')) {
  try {
   const cursor = JSON.parse(atob(q.get('cursor')!))
   if (cursor.key !== key || typeof cursor.time !== 'string' || typeof cursor.id !== 'string') throw new Error()
   after = cursor
  } catch { fail(400, 'bad_cursor', '分页参数不正确。') }
 }
 return { limit, after, cursor: (time: string, id: string) => btoa(JSON.stringify({ key, time, id })) }
}
