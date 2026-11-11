// 界面只从这里取数据：在宿主的 WebView2 里走消息桥，在浏览器里（npm run dev）用假数据
import type { Api } from './types'

export const api: Api = window.chrome?.webview
  ? (await import('./bridge')).bridgeApi
  : (await import('./mock')).mockApi
export type * from './types'
