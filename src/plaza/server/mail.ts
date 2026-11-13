import { fail, type Env } from './common'

const encode = (value: string) => encodeURIComponent(value).replace(/[!'()*]/g,
 c => '%' + c.charCodeAt(0).toString(16).toUpperCase())
const query = (params: Record<string, string>) => Object.keys(params).sort()
 .map(key => `${encode(key)}=${encode(params[key])}`).join('&')

export async function signMail(params: Record<string, string>, secret: string) {
 const encoder = new TextEncoder()
 const key = await crypto.subtle.importKey('raw', encoder.encode(secret + '&'),
  { name: 'HMAC', hash: 'SHA-1' }, false, ['sign'])
 const signature = await crypto.subtle.sign('HMAC', key, encoder.encode('POST&%2F&' + encode(query(params))))
 return btoa(String.fromCharCode(...new Uint8Array(signature)))
}

export async function sendCodeMail(env: Env, email: string, code: string) {
 const text = `你的验证码是 ${code}，10 分钟内有效。\n如果不是你本人在登录卫戍协议助手的阵容广场，请忽略这封邮件。`
 let sent: Response, errorCode: unknown
 try {
  if (env.ALIYUN_DM_ACCESS_KEY_ID && env.ALIYUN_DM_ACCESS_KEY_SECRET && env.ALIYUN_DM_ACCOUNT) {
   const params = {
    AccessKeyId: env.ALIYUN_DM_ACCESS_KEY_ID, AccountName: env.ALIYUN_DM_ACCOUNT,
    Action: 'SingleSendMail', AddressType: '1', ReplyToAddress: 'false', ToAddress: email,
    Subject: `卫戍协议助手验证码：${code}`, TextBody: text, FromAlias: '卫戍协议助手', ClickTrace: '0',
    Format: 'JSON', Version: '2015-11-23', RegionId: 'cn-hangzhou',
    SignatureMethod: 'HMAC-SHA1', SignatureVersion: '1.0',
    SignatureNonce: crypto.randomUUID(), Timestamp: new Date().toISOString().replace(/\.\d{3}Z$/, 'Z'),
   }
   const signature = await signMail(params, env.ALIYUN_DM_ACCESS_KEY_SECRET)
   sent = await fetch('https://dm.aliyuncs.com/', {
    method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: query({ ...params, Signature: signature }),
   })
   if (sent.status !== 429) {
    const body = await sent.json().catch(() => null) as { Code?: string; Message?: string } | null
    errorCode = body?.Code
    // 阿里云拒绝时把错误码和说明写进日志（wrangler pages deployment tail 能看到），不含凭据
    if (!sent.ok || errorCode) console.log(`[mail] aliyun status=${sent.status} code=${errorCode} message=${body?.Message}`)
   }
  } else {
   sent = await fetch('https://api.resend.com/emails', {
    method: 'POST', headers: { Authorization: `Bearer ${env.RESEND_API_KEY}`, 'Content-Type': 'application/json' },
    body: JSON.stringify({ from: env.MAIL_FROM, to: [email], subject: `卫戍协议助手登录验证码：${code}`, text }),
   })
  }
 } catch {
  console.log('[mail] fetch failed')
  return fail(424, 'mail_failed', '验证码发送失败，请稍后再试。')
 }
 // TODO(design): InvalidSendMail.Spam 兼有额度、限频和反垃圾含义，暂按一般失败提示，待确认。
 if (sent.status === 429 || ['InvalidQuota', 'Throttling', 'Throttling.User', 'Throttling.Api'].includes(String(errorCode)))
  fail(429, 'mail_quota', '今天能发的验证码邮件用完了。设过密码的请用「密码登录」，没设过的明天再试。')
 // 发送失败用 424（不用 502）：5xx 会被中间层换成自己的错误页，用户看不到这句提示
 if (!sent.ok || errorCode) fail(424, 'mail_failed', '验证码发送失败，请稍后再试。')
}
