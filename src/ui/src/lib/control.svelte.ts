// 自动操作（任务 390～393，设计见 docs/自动操作.md）：设置、风险同意、当前这批操作的进度
import { api, type ControlJob, type ControlMode, type ControlSettings } from './api'
import { app } from './state.svelte'

/** 风险说明的版本：说明有实质改动时加一，已同意旧版的人要重新看一遍 */
export const CONTROL_CONSENT_VERSION = 1

export const CONTROL_DEFAULTS: ControlSettings = { enabled: false, consent: 0, external: false, mode: 'dryRun', stopHotkey: 'Ctrl+Alt+Q', api: false }

export const MODES: { id: ControlMode; label: string; tip: string }[] = [
  { id: 'dryRun', label: '试运行', tip: '只在截图上标出会点哪里、拖到哪里，不碰游戏' },
  { id: 'confirm', label: '逐步确认', tip: '每一步做之前停下，你点「执行」才做' },
  { id: 'auto', label: '自动', tip: '一口气做完；每一步做完都核对，画面不对就停' },
]

class ControlStore {
  job = $state<ControlJob | null>(null)
  /** 风险说明页开着 */
  riskOpen = $state(false)
  /** 浮条上点开的大图 */
  zoom = $state<string | null>(null)
  /** 浮条收掉了（这批结束后点「关闭」）；新的一批开始时重新显示 */
  dismissed = $state<string | null>(null)
  /** 本机接口地址（api 开着时才有） */
  address = $state<string | null>(null)
  profile = $state('default')

  get settings(): ControlSettings {
    return { ...CONTROL_DEFAULTS, ...app.settings.control }
  }
  get consented() {
    return this.settings.consent >= CONTROL_CONSENT_VERSION
  }
  get running() {
    return this.job?.state === 'running' || this.job?.state === 'waitingConfirm'
  }

  init() {
    api.controlJob().then((j) => (this.job = j)).catch(() => {})
    api.onControlJob((j) => (this.job = j))
    this.refreshApi()
  }

  refreshApi() {
    api.controlApi().then((s) => ((this.address = s.address), (this.profile = s.profile))).catch(() => {})
  }

  set(patch: Partial<ControlSettings>) {
    app.settings.control = { ...this.settings, ...patch }
    return app.saveSettings().then(() => this.refreshApi())
  }

  /** 设置里的总开关：没同意过当前版本的风险说明就先开说明页 */
  toggle(on: boolean) {
    if (!on) return this.set({ enabled: false, external: false })
    if (!this.consented) return void (this.riskOpen = true)
    return this.set({ enabled: true })
  }

  /** 风险页勾选后点「开启」 */
  agree() {
    this.riskOpen = false
    return this.set({ enabled: true, consent: CONTROL_CONSENT_VERSION })
  }

  async run(start: () => Promise<ControlJob>) {
    try {
      this.job = await start()
      this.dismissed = null
    } catch (e) {
      app.showToast((e as Error).message)
    }
  }
}

export const control = new ControlStore()
