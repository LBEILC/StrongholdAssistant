// 账号云同步（v0.7.0，设计见 docs/design.md「账号云同步与网站联动」）。
// 同步逻辑全在界面里：直接调广场的 POST /api/sync，本机状态存在 Settings.cloudSync，阵容经 putLineups 原样写回（保留 updatedAt）。
// 规则：按每套阵容的 updatedAt，新的为准；推上去被拒（云端更新）而本地也改过的，本地那份另存成「（冲突副本）」。
// 同步状态属于一个账号（cloudSync.account）：换了账号就从头来，像第一次打开同步一样合并。
// 第一次同步不推偏好：先拿云端的，和本机的合并（关注取并集、我的默认按干员合并、其余取云端），合并后有变化再推。
// 启动时同步一次，改动后几秒自动上传；离线照常用，下次再传。
import { api, type Lineup } from './api'
import { plaza } from './plaza.svelte'
import type { CloudLineup, CloudPrefsData, SyncRequest } from '../../../plaza/shared/api'
import type { AppState as App } from './state.svelte'

const PREF_KEYS = ['focusOperators', 'focusCovenants', 'gameTheme', 'shopMarkers', 'operatorDefaults'] as const
const DELAY = 4000

class CloudSync {
  status = $state<'idle' | 'syncing' | 'error'>('idle')
  error = $state('')
  /** 本次运行里最近一次同步成功的时间 */
  lastAt = $state<string | null>(null)
  private app!: App
  private timer = 0
  private running = false
  private again = false
  private lastPrefs = ''

  get enabled() {
    return this.app.settings.cloudSync.enabled
  }

  /** 启动时调一次：记下偏好的样子，盯着阵容变化，打开了就同步 */
  init(app: App) {
    this.app = app
    this.lastPrefs = this.prefsJson()
    $effect.root(() => {
      let first = true
      $effect(() => {
        void app.lineups
        if (first) return void (first = false)
        this.schedule()
      })
    })
    if (this.enabled) plaza.check().then(() => this.run())
  }

  private prefsData = (): CloudPrefsData => Object.fromEntries(PREF_KEYS.map((k) => [k, $state.snapshot(this.app.settings[k])])) as CloudPrefsData
  private prefsJson = () => JSON.stringify(this.prefsData())
  /** 云端偏好只认约定的这几项 */
  private pick = (d: CloudPrefsData) => Object.fromEntries(PREF_KEYS.filter((k) => k in d).map((k) => [k, d[k]])) as CloudPrefsData
  /** 第一次同步时合并：关注取并集，我的默认按干员合并（同一名干员取云端），主题和商店标记取云端 */
  private merge = (cloud: CloudPrefsData, local: CloudPrefsData): CloudPrefsData => ({
    ...cloud,
    focusOperators: [...new Set([...(cloud.focusOperators ?? []), ...local.focusOperators])],
    focusCovenants: [...new Set([...(cloud.focusCovenants ?? []), ...local.focusCovenants])],
    operatorDefaults: { ...local.operatorDefaults, ...cloud.operatorDefaults },
  })

  /** 每次保存设置前调：同步的那几项变了就记下时间，稍后上传 */
  beforeSaveSettings() {
    const now = this.prefsJson()
    if (now === this.lastPrefs) return
    this.lastPrefs = now
    this.app.settings.cloudSync.prefsAt = new Date().toISOString()
    this.schedule()
  }

  /** 删阵容时调：记一条删除，下次上传 */
  deleted(id: string) {
    if (!this.enabled) return
    this.app.settings.cloudSync.deleted.push({ id, at: new Date().toISOString() })
    this.app.saveSettings()
  }

  schedule() {
    if (!this.enabled || !plaza.token) return
    clearTimeout(this.timer)
    this.timer = window.setTimeout(() => this.run(), DELAY)
  }

  async setEnabled(on: boolean) {
    this.app.settings.cloudSync.enabled = on
    this.app.saveSettings()
    if (on) await this.run()
  }

  /** 同步一次；正在同步时等这次完了再来一次 */
  async run() {
    if (!this.enabled || !plaza.token || !plaza.me) return
    if (this.running) return void (this.again = true)
    this.running = true
    this.status = 'syncing'
    this.error = ''
    clearTimeout(this.timer)
    try {
      await this.once()
      this.status = 'idle'
      this.lastAt = new Date().toISOString()
    } catch (e) {
      this.status = 'error'
      this.error = (e as Error).message
    } finally {
      this.running = false
      if (this.again) {
        this.again = false
        this.schedule()
      }
    }
  }

  private async once() {
    const app = this.app
    const st = app.settings.cloudSync
    // 换了账号（或旧版本没记账号）：游标、上传时间、删除队列都是上个账号的，丢掉从头合并
    if (st.account !== plaza.me!.id) Object.assign(st, { account: plaza.me!.id, since: null, pushedAt: null, deleted: [] })
    const first = !st.pushedAt
    const startedAt = new Date().toISOString()
    const changed = app.lineups.filter((l) => !st.pushedAt || l.updatedAt > st.pushedAt)
    const tombs = [...st.deleted]
    const req: SyncRequest = {
      since: st.since,
      lineups: [
        ...changed.map((l): CloudLineup => ({ id: l.id, data: $state.snapshot(l) as Lineup, updatedAt: l.updatedAt, deleted: false })),
        ...tombs.map((t): CloudLineup => ({ id: t.id, data: null, updatedAt: t.at, deleted: true })),
      ],
      ...(!first && st.prefsAt && st.prefsAt > st.pushedAt! && { prefs: { data: this.prefsData(), updatedAt: st.prefsAt } }),
    }
    const res = await plaza.sync(req)

    // 写回本地：别的设备改的、被拒的（取云端那份；本地改过的先另存冲突副本）
    const local = new Map(app.lineups.map((l) => [l.id, l]))
    const pushedIds = new Set(changed.map((l) => l.id))
    const put: Lineup[] = []
    const removed: string[] = []
    const take = (c: CloudLineup) => (c.deleted || !c.data ? local.has(c.id) && removed.push(c.id) : put.push(c.data))
    // 请求在路上时本地删掉的，不让云端那份写回来
    const goneSince = new Set(st.deleted.filter((t) => !tombs.includes(t)).map((t) => t.id))
    for (const c of res.lineups) {
      if (goneSince.has(c.id)) continue
      const l = local.get(c.id)
      // 本地在这之后又改过的保留本地，下次推上去
      if (l && l.updatedAt > c.updatedAt) continue
      take(c)
    }
    for (const c of res.conflicts) {
      if (goneSince.has(c.id)) continue
      const l = local.get(c.id)
      if (l && pushedIds.has(c.id)) put.push({ ...($state.snapshot(l) as Lineup), id: crypto.randomUUID().replace(/-/g, ''), name: `${l.name}（冲突副本）`, updatedAt: new Date().toISOString() })
      take(c)
    }
    if (put.length || removed.length) {
      if (removed.includes(app.session.targetLineupId ?? '')) app.setTarget(null)
      app.lineups = await api.putLineups(put, removed)
    }
    // 请求在路上时本地改过偏好的，保留本地，下次推上去
    const prefsTouched = !!st.prefsAt && st.prefsAt > startedAt
    if (first && !prefsTouched) {
      const cloudPrefs = res.prefs && this.pick(res.prefs.data)
      const merged = cloudPrefs ? this.merge(cloudPrefs, this.prefsData()) : this.prefsData()
      Object.assign(app.settings, merged)
      this.lastPrefs = this.prefsJson()
      // 合并出了云端没有的东西（或云端还没有偏好）：记个时间，下一轮推上去
      if (!cloudPrefs || JSON.stringify(merged) !== JSON.stringify(cloudPrefs)) {
        st.prefsAt = new Date(Date.parse(startedAt) + 1).toISOString()
        this.again = true
      }
    } else if (res.prefs && !prefsTouched) {
      Object.assign(app.settings, this.pick(res.prefs.data))
      this.lastPrefs = this.prefsJson()
    }
    st.since = res.now
    st.pushedAt = startedAt
    st.deleted = st.deleted.filter((t) => !tombs.some((x) => x.id === t.id && x.at === t.at))
    app.saveSettings()
  }
}

export const cloud = new CloudSync()
