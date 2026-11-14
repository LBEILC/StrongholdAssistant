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

// 验证码邮件的 HTML 版（设计）：照 GitHub 通知邮件的样子，白底、灰框、等宽大号验证码。
// 邮件客户端只认表格布局和行内样式；logo 用地址固定的 /mail/logo.png（构建产物带哈希，不能用）
const SITE = 'https://sa.lubeiluchen.com'
const FONT = "-apple-system,BlinkMacSystemFont,'Segoe UI','PingFang SC','Microsoft YaHei',Helvetica,Arial,sans-serif"
const MONO = "ui-monospace,SFMono-Regular,'SF Mono',Menlo,Consolas,'Liberation Mono',monospace"
export function codeMailHtml(code: string) {
 const link = (href: string, label: string) => `<a href="${href}" style="color:#0969da;text-decoration:none">${label}</a>`
 return `<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="color-scheme" content="light"><title>卫戍协议助手验证码</title></head>
<body style="margin:0;padding:0;background:#f6f8fa;font-family:${FONT};color:#1f2328">
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f6f8fa"><tr><td align="center" style="padding:32px 16px">
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:520px">
 <tr><td align="center" style="padding-bottom:20px">
  <img src="${SITE}/mail/logo.png" width="48" height="48" alt="卫戍协议助手" style="display:block;border:0;border-radius:10px">
 </td></tr>
 <tr><td style="background:#ffffff;border:1px solid #d1d9e0;border-radius:6px;padding:28px 32px">
  <p style="margin:0 0 4px;font-size:13px;color:#59636e">卫戍协议助手 · 阵容广场</p>
  <h1 style="margin:0 0 20px;padding-bottom:12px;border-bottom:1px solid #d1d9e0;font-size:22px;font-weight:600;line-height:1.3">登录验证码</h1>
  <p style="margin:0 0 16px;font-size:15px;line-height:1.6">在登录页输入下面的验证码就能继续：</p>
  <div style="margin:0 0 16px;padding:16px;background:#f6f8fa;border:1px solid #d1d9e0;border-radius:6px;text-align:center;font-family:${MONO};font-size:32px;font-weight:600;letter-spacing:8px;color:#1f2328">${code}</div>
  <p style="margin:0 0 20px;font-size:14px;line-height:1.6;color:#59636e">验证码 <b style="color:#1f2328">10 分钟</b>内有效，只能用一次。</p>
  <div style="padding:10px 14px;border-left:4px solid #d1d9e0;color:#59636e;font-size:13px;line-height:1.6">如果不是你本人在登录，忽略这封邮件就好，你的账号不会有任何变化。不要把验证码告诉任何人。</div>
 </td></tr>
 <tr><td align="center" style="padding:20px 8px 0;font-size:12px;line-height:1.8;color:#59636e">
  ${link(SITE, '阵容广场')} &nbsp;·&nbsp; ${link('https://github.com/LBEILC/StrongholdAssistant/releases/latest', '下载助手')} &nbsp;·&nbsp; ${link('https://github.com/LBEILC/StrongholdAssistant', 'GitHub')}<br>
  这封邮件由系统自动发送，请不要直接回复。
 </td></tr>
</table></td></tr></table></body></html>`
}

export async function sendCodeMail(env: Env, email: string, code: string) {
 const text = `你的验证码是 ${code}，10 分钟内有效。\n如果不是你本人在登录卫戍协议助手的阵容广场，请忽略这封邮件。`
 const html = codeMailHtml(code)
 let sent: Response, errorCode: unknown
 try {
  if (env.ALIYUN_DM_ACCESS_KEY_ID && env.ALIYUN_DM_ACCESS_KEY_SECRET && env.ALIYUN_DM_ACCOUNT) {
   const params = {
    AccessKeyId: env.ALIYUN_DM_ACCESS_KEY_ID, AccountName: env.ALIYUN_DM_ACCOUNT,
    Action: 'SingleSendMail', AddressType: '1', ReplyToAddress: 'false', ToAddress: email,
    Subject: `卫戍协议助手验证码：${code}`, HtmlBody: html, FromAlias: '卫戍协议助手', ClickTrace: '0',
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
    body: JSON.stringify({ from: env.MAIL_FROM, to: [email], subject: `卫戍协议助手登录验证码：${code}`, html, text }),
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
