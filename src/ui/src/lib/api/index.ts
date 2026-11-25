// 界面只从这里取数据：在宿主的 WebView2 里走消息桥，在浏览器里（npm run dev）用假数据
import type { Api } from './types'

export const api: Api = window.chrome?.webview
  ? (await import('./bridge')).bridgeApi
  : (await import('./mock')).mockApi
export type * from './types'

/** 界面报错送进宿主日志，诊断包里能看到；送不出去就算了，不能让报错再报错 */
export function reportUiError(where: string, error: unknown) {
  const text = error instanceof Error ? `${error.message}\n${error.stack ?? ''}` : String(error)
  console.error(where, error)
  api.logUiError(`${where}: ${text}`.slice(0, 4000)).catch(() => {})
}
