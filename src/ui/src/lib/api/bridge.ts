// WebView2 消息桥（界面一侧）。协议由设计定义，宿主（C#）照此实现：
//   界面 → 宿主  { id: number, method: keyof Api, params: unknown[] }   postMessage 发对象
//   宿主 → 界面  { id, result } 或 { id, error: string }                 回应同 id 的请求
//   宿主 → 界面  { event: 'match' | 'connection' | 'scan' | 'update', data } 主动推送，data 依次是 MatchInfo / Connection / ScanStatus / UpdateStatus
// method 名与参数顺序与 types.ts 的 Api 一致。
import type { Api } from './types'

interface WebView {
  postMessage(msg: unknown): void
  addEventListener(type: 'message', cb: (e: { data: any }) => void): void
}
declare global {
  interface Window {
    chrome?: { webview?: WebView }
  }
}

const webview = window.chrome!.webview!
let seq = 0
const pending = new Map<number, { resolve: (v: any) => void; reject: (e: Error) => void }>()
const listeners: Record<string, Set<(d: any) => void>> = { match: new Set(), connection: new Set(), scan: new Set(), update: new Set(), window: new Set() }
const listen = (event: string) => (cb: (d: any) => void) => {
  listeners[event].add(cb)
  return () => void listeners[event].delete(cb)
}

webview.addEventListener('message', ({ data }) => {
  if ('event' in data) {
    listeners[data.event].forEach((cb) => cb(data.data))
    return
  }
  const p = pending.get(data.id)!
  pending.delete(data.id)
  'error' in data ? p.reject(new Error(data.error)) : p.resolve(data.result)
})

const call =
  (method: keyof Api) =>
  (...params: unknown[]) =>
    new Promise<any>((resolve, reject) => {
      const id = ++seq
      pending.set(id, { resolve, reject })
      webview.postMessage({ id, method, params })
    })

export const bridgeApi: Api = {
  catalog: call('catalog'),
  connection: call('connection'),
  match: call('match'),
  rescan: call('rescan'),
  scanContinuous: call('scanContinuous'),
  setCovenantIncomplete: call('setCovenantIncomplete'),
  setOperatorBanned: call('setOperatorBanned'),
  resetMatch: call('resetMatch'),
  lineups: call('lineups'),
  saveLineup: call('saveLineup'),
  deleteLineup: call('deleteLineup'),
  session: call('session'),
  saveSession: call('saveSession'),
  windowCommand: call('windowCommand'),
  collapseUp: call('collapseUp'),
  settings: call('settings'),
  saveSettings: call('saveSettings'),
  appInfo: call('appInfo'),
  checkUpdate: call('checkUpdate'),
  applyUpdate: call('applyUpdate'),
  openUrl: call('openUrl'),
  copyText: call('copyText'),
  plazaLogin: call('plazaLogin'),
  plazaToken: call('plazaToken'),
  plazaLogout: call('plazaLogout'),
  openDataFolder: call('openDataFolder'),
  mumuInstances: call('mumuInstances'),
  exportData: call('exportData'),
  pickBackup: call('pickBackup'),
  importData: call('importData'),
  onUpdate: listen('update'),
  onMatch: listen('match'),
  onConnection: listen('connection'),
  onScan: listen('scan'),
  onWindow: listen('window'),
  onDeepLink: listen('deepLink'),
}
