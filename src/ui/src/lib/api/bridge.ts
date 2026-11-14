// WebView2 消息桥（界面一侧）。协议由设计定义，宿主（C#）照此实现：
//   界面 → 宿主  { id: number, method: keyof Api, params: unknown[] }   postMessage 发对象
//   宿主 → 界面  { id, result } 或 { id, error: string }                 回应同 id 的请求
//                error 第一行是给用户看的中文原因；第二行起（可选，v0.7.1）是技术细节（异常类型、出错的字段或状态码等），
//                界面折叠显示，用户可以复制下来反馈。没有细节就只写一行
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
// 事件名不用预先登记：哪个事件有人订阅就建哪个（加 hotkey 时漏登记过，界面启动就崩）
const listeners: Record<string, Set<(d: any) => void>> = {}
const listen = (event: string) => (cb: (d: any) => void) => {
  ;(listeners[event] ??= new Set()).add(cb)
  return () => void listeners[event].delete(cb)
}

webview.addEventListener('message', ({ data }) => {
  if ('event' in data) {
    listeners[data.event]?.forEach((cb) => cb(data.data))
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
  field: call('field'),
  setFieldOperator: call('setFieldOperator'),
  rescan: call('rescan'),
  scanContinuous: call('scanContinuous'),
  setMatchStrategy: call('setMatchStrategy'),
  setCovenantIncomplete: call('setCovenantIncomplete'),
  setOperatorBanned: call('setOperatorBanned'),
  resetMatch: call('resetMatch'),
  lineups: call('lineups'),
  saveLineup: call('saveLineup'),
  deleteLineup: call('deleteLineup'),
  putLineups: call('putLineups'),
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
  listFonts: call('listFonts'),
  copyText: call('copyText'),
  plazaLogin: call('plazaLogin'),
  plazaToken: call('plazaToken'),
  plazaLogout: call('plazaLogout'),
  sklandGuide: call('sklandGuide'),
  sklandGuides: call('sklandGuides'),
  sklandHistory: call('sklandHistory'),
  sklandAccount: call('sklandAccount'),
  sklandLogout: call('sklandLogout'),
  sklandLogin: call('sklandLogin'),
  setHotkey: call('setHotkey'),
  setRunAsAdmin: call('setRunAsAdmin'),
  exportDiagnostics: call('exportDiagnostics'),
  addIncome: call('addIncome'),
  removeIncome: call('removeIncome'),
  history: call('history'),
  deleteRecord: call('deleteRecord'),
  saveSklandRecords: call('saveSklandRecords'),
  resetShareId: call('resetShareId'),
  openDataFolder: call('openDataFolder'),
  emulatorInstances: call('emulatorInstances'),
  exportData: call('exportData'),
  pickBackup: call('pickBackup'),
  importData: call('importData'),
  onUpdate: listen('update'),
  onMatch: listen('match'),
  onConnection: listen('connection'),
  onScan: listen('scan'),
  onField: listen('field'),
  onWindow: listen('window'),
  onDeepLink: listen('deepLink'),
  onHotkey: listen('hotkey'),
}
