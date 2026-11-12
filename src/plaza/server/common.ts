import words from '../data/blocked-words.txt'

export interface Env {
 DB: D1Database
 RESEND_API_KEY: string
 MAIL_FROM: string
 TURNSTILE_SECRET: string
 CODE_PEPPER: string
 ADMIN_EMAILS: string
 ACCESS_TEAM_DOMAIN: string
 ACCESS_AUD: string
 ALLOWED_ORIGINS: string
}
// TODO(design): 请求参数、未登录、权限和网络失败等未定文案先用简短中文提示，待统一。
export class ApiFailure extends Error {
 constructor(public status: number, public code: string, message: string) { super(message) }
}
export function fail(status: number, code: string, message: string): never { throw new ApiFailure(status, code, message) }
export const json = (data: unknown, status = 200) => Response.json(data, { status })
export const empty = () => new Response(null, { status: 204 })
export const iso = (time = Date.now()) => new Date(time).toISOString()
export const DAY = 86400000
export const dayStart = () => iso(Math.floor((Date.now() + 8 * 3600000) / DAY) * DAY - 8 * 3600000)
export const stmt = (env: Env, sql: string, ...args: (string | number | null)[]) => env.DB.prepare(sql).bind(...args)
export async function hash(text: string) {
 const bytes = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text))
 return Array.from(new Uint8Array(bytes), b => b.toString(16).padStart(2, '0')).join('')
}
export async function body(request: Request): Promise<Record<string, unknown>> {
 try {
  const result = await request.json()
  if (!result || typeof result !== 'object' || Array.isArray(result)) throw new Error()
  return result as Record<string, unknown>
 } catch { return fail(400, 'bad_request', '请求内容不正确。') }
}
export function string(value: unknown): string {
 if (typeof value !== 'string') fail(400, 'bad_request', '请求内容不正确。')
 return value
}
const normalize = (s: string) => s.toLowerCase().replace(/\s/g, '')
const blocked = words.split(/\r?\n/).map(normalize).filter(Boolean)
export const hasBlocked = (s: string) => blocked.some(w => normalize(s).includes(w))
export function description(value: unknown) {
 const result = string(value)
 if ([...result].length > 500) fail(400, 'bad_description', '简介最多 500 字。')
 if (hasBlocked(result)) fail(400, 'blocked_words', '阵容名或简介里有不允许的词。')
 return result
}
export function mask(email: string | null) {
 if (!email) return ''
 const [name, domain] = email.split('@')
 return name.slice(0, 2) + '***@' + domain
}
export interface UserRow { id: string; email: string | null; nickname: string | null; created_at: string; banned: number; deleted: number; password_hash: string | null }
export const me = (u: UserRow) => ({ id: u.id, nickname: u.nickname, emailMasked: mask(u.email), createdAt: u.created_at, hasPassword: u.password_hash !== null })
export function needNickname(u: UserRow) {
 if (!u.nickname) fail(403, 'need_nickname', '请先起一个昵称。')
}
