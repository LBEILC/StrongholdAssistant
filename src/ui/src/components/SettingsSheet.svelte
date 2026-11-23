<script lang="ts">
  import { sheet } from '../lib/sheet'
  import { fade } from 'svelte/transition'
  import { app, type SettingsTab } from '../lib/state.svelte'
  import Seg from './Seg.svelte'
  import { fontStack } from '../lib/fonts'
  import { ACCENTS } from '../lib/accent'
  import { api, type BackupSummary, type CollectionMatch, type GameTheme, type ImportMode, type EmulatorInstance, type Settings, type UiSkin, type MarkerStyle, type GitHubTokenCheck } from '../lib/api'
  import Icon from './Icon.svelte'
  import NotesList from './NotesList.svelte'
  import { plaza, PLAZA } from '../lib/plaza.svelte'
  import { cloud } from '../lib/sync.svelte'
  import { control, MODES } from '../lib/control.svelte'

  // 设置：游戏主题（商店识别要用）、商店标记、版本与更新、关于
  const themes: { id: GameTheme; name: string; note: string }[] = [
    { id: 'terminal', name: '卫戍协议终端', note: '默认主题' },
    { id: 'ursus', name: '乌萨斯齐心团', note: '' },
    { id: 'closure', name: '「全能的」可露希尔', note: '' },
  ]
  // 界面主题（v0.8.0）：和游戏主题分开设置，默认跟着游戏主题走
  const skins: { id: UiSkin; name: string; note: string }[] = [
    { id: 'follow', name: '跟随游戏主题', note: '默认' },
    ...themes.map((t) => ({ ...t, note: '' })),
    { id: 'classic', name: '经典', note: '助手原来的样子' },
    // 不照游戏做的通用风格（v0.9.8），各有深色、浅色
    { id: 'glass', name: '毛玻璃', note: '通用软件风格，深色 / 浅色' },
    { id: 'round', name: '圆润', note: '圆角胶囊按钮，深色 / 浅色' },
  ]
  // 自选颜色：拖动取色器时连续触发，最后一次才保存
  let accentTimer: ReturnType<typeof setTimeout> | undefined
  const pickAccent = (v: string) => {
    app.settings.uiAccent = v.toLowerCase()
    clearTimeout(accentTimer)
    accentTimer = setTimeout(() => app.saveSettings(), 300)
  }
  const customAccent = $derived(!!app.settings.uiAccent && !ACCENTS.some((a) => a.v === app.settings.uiAccent))
  const schemes: { v: Settings['uiScheme']; label: string }[] = [
    { v: 'system', label: '跟随系统' },
    { v: 'dark', label: '深色' },
    { v: 'light', label: '浅色' },
  ]
  const scales: { v: 'auto' | number; label: string }[] = [
    { v: 'auto', label: '自动' },
    { v: 0.5, label: '50%' },
    { v: 0.75, label: '75%' },
    { v: 1, label: '100%' },
    { v: 1.25, label: '125%' },
    { v: 1.5, label: '150%' },
    { v: 1.75, label: '175%' },
    { v: 2, label: '200%' },
  ]
  const REPO = 'https://github.com/LBEILC/StrongholdAssistant'
  const QQ = 'https://qm.qq.com/q/F5F2akHgQ0' // 一群 1132134217 已满，2026-11-20 起指向二群
  const close = () => (app.settingsOpen = false)
  const set = <K extends keyof typeof app.settings>(k: K, v: (typeof app.settings)[K]) => {
    app.settings[k] = v
    app.saveSettings()
  }
  // 敌人路线（v0.9.0）：一组设置整体存
  const rt = $derived(app.settings.routes)
  // 覆盖层新加的两项（任务 169）；宿主还没补这一项时按默认（都开）显示
  const ol = $derived(app.settings.overlayLayers ?? { placement: true, pending: true, upgrade: true })
  const setRoutes = (patch: Partial<typeof app.settings.routes>) => set('routes', { ...app.settings.routes, ...patch })
  const u = $derived(app.update)

  // Mirror酱：新 CDK 第一个带它检查的 app 拿分成，所以填完（失焦或回车）立刻检查一次，顺便校验 CDK、显示到期日
  const MIRROR = 'https://mirrorchyan.com/zh/projects?rid=StrongholdAssistant&source=strongholdassistant-settings'
  const mirror = $derived(app.settings.updateSource === 'mirrorchyan')
  let cdkDraft = $state('')
  let showCdk = $state(false)
  // 设置是启动后异步读进来的，导入备份也可能换掉：跟着设置走
  $effect(() => {
    cdkDraft = app.settings.mirrorChyanCdk
  })
  // 先等宿主存好设置再检查，检查才会用上新的更新源和 CDK
  const setUpdateSource = async (v: Settings['updateSource']) => {
    if (app.settings.updateSource === v) return
    app.settings.updateSource = v
    await app.saveSettings()
    app.checkUpdate()
  }
  const commitCdk = async () => {
    const v = cdkDraft.trim()
    cdkDraft = v
    if (v === app.settings.mirrorChyanCdk) return
    app.settings.mirrorChyanCdk = v
    await app.saveSettings()
    app.checkUpdate()
  }
  const toGithub = () => setUpdateSource('github')

  // GitHub Token（v1.0.2，参考 MAA）：GitHub 对匿名请求按 IP 每小时只给 60 次 API 额度，反复检查更新容易用完。
  // 填完（失焦或回车）先校验一次拿到到期时间，再重新检查更新——和 MAA 的交互一样
  const TOKEN_URL = 'https://github.com/settings/tokens/new?description=StrongholdAssistant'
  let tokenDraft = $state('')
  let showToken = $state(false)
  let tokenChecking = $state(false)
  let tokenCheck = $state<GitHubTokenCheck | null>(null)
  // 设置是启动后异步读进来的，导入备份也可能换掉：跟着设置走
  $effect(() => {
    tokenDraft = app.settings.githubToken
  })
  const commitToken = async () => {
    const v = tokenDraft.trim()
    tokenDraft = v
    if (v !== app.settings.githubToken) {
      app.settings.githubToken = v
      app.settings.githubTokenExpiresAt = ''
      await app.saveSettings()
    }
    tokenCheck = null
    if (v === '') {
      app.checkUpdate()
      return
    }
    tokenChecking = true
    try {
      tokenCheck = await api.validateGithubToken(v)
      if (tokenCheck.valid) app.settings.githubTokenExpiresAt = tokenCheck.expiresAt ?? ''
    } finally {
      tokenChecking = false
    }
    app.checkUpdate()
  }
  const tokenBad = $derived(
    !!tokenCheck && !tokenCheck.valid || (u.state === 'failed' && !!u.githubTokenProblem) || (u.state === 'failed' && !!u.githubRateLimited),
  )
  // 到期日优先用刚校验的结果，其次用宿主上次记下的
  const tokenExpiry = $derived.by(() => {
    const iso = (tokenCheck?.valid ? tokenCheck.expiresAt : null) ?? app.settings.githubTokenExpiresAt ?? u.githubTokenExpiresAt ?? ''
    if (!iso) return null
    const d = new Date(iso)
    if (Number.isNaN(d.getTime())) return null
    const days = Math.ceil((d.getTime() - Date.now()) / 86400_000)
    return {
      date: d.toLocaleDateString('zh-CN', { year: 'numeric', month: '2-digit', day: '2-digit' }).replaceAll('/', '-'),
      soon: days <= 7,
      days,
    }
  })

  // 更新代理：'' 跟随系统；手动填 http:// 或 socks5:// 地址，填好（失焦或回车）立刻检查一次，顺便试通代理
  const validProxy = (v: string) => {
    const m = /^(http|socks5):\/\/[^\s/:@]+:(\d{1,5})$/i.exec(v)
    return !!m && +m[2] >= 1 && +m[2] <= 65535
  }
  let proxyManual = $state(false)
  let proxyDraft = $state('')
  $effect(() => {
    proxyDraft = app.settings.updateProxy
    proxyManual = app.settings.updateProxy !== ''
  })
  const proxyBad = $derived(proxyDraft.trim() !== '' && !validProxy(proxyDraft.trim()))
  const saveProxy = async (v: string) => {
    if (v === app.settings.updateProxy) return
    app.settings.updateProxy = v
    await app.saveSettings()
    app.checkUpdate()
  }
  const setProxyManual = (on: boolean) => {
    proxyManual = on
    if (!on) {
      proxyDraft = ''
      saveProxy('')
    }
  }
  const commitProxy = () => {
    const v = proxyDraft.trim()
    proxyDraft = v
    if (v === '' || validProxy(v)) saveProxy(v)
  }
  // 自定义 ADB 地址（v1.2.8）：'' = 自动找；填了就只连这个地址。和代理一样失焦或回车才保存
  const validAdb = (v: string) => /^emulator-\d+$/.test(v) || (/^[\w.-]+:(\d{1,5})$/.test(v) && +v.split(':')[1] >= 1 && +v.split(':')[1] <= 65535)
  let adbManual = $state(false)
  // 多开（365）：默认配置里开关；任何配置里都能看到配置列表、打开或新建
  let profile = $state<string | null>(null)
  let profiles = $state<string[]>([])
  let newProfile = $state('')
  let profileError = $state('')
  const loadProfiles = async () => {
    profile = await api.currentProfile()
    profiles = await api.listProfiles()
  }
  loadProfiles()
  const openProfile = async (name: string | null) => {
    profileError = ''
    try { await api.launchProfile(name) } catch (e) { profileError = (e as Error).message }
  }
  const addProfile = async () => {
    const name = newProfile.trim().toLowerCase()
    if (!name) return
    profileError = ''
    try {
      await api.createProfile(name)
      newProfile = ''
      await loadProfiles()
      await api.launchProfile(name)
    } catch (e) {
      profileError = (e as Error).message
    }
  }
  let adbDraft = $state('')
  $effect(() => {
    adbDraft = app.settings.adbAddress
    adbManual = app.settings.adbAddress !== ''
  })
  const adbBad = $derived(adbDraft.trim() !== '' && !validAdb(adbDraft.trim()))
  const saveAdb = (v: string) => v !== app.settings.adbAddress && set('adbAddress', v)
  const setAdbManual = (on: boolean) => {
    adbManual = on
    if (!on) {
      adbDraft = ''
      saveAdb('')
    }
  }
  const commitAdb = () => {
    const v = adbDraft.trim()
    adbDraft = v
    if (v === '' || validAdb(v)) saveAdb(v)
  }
  const expiry = $derived.by(() => {
    if (!u.cdkExpiresAt) return null
    const d = new Date(u.cdkExpiresAt)
    const days = Math.ceil((d.getTime() - Date.now()) / 86400_000)
    return { date: d.toLocaleDateString('zh-CN', { year: 'numeric', month: '2-digit', day: '2-digit' }).replaceAll('/', '-'), soon: days <= 7, days }
  })

  // 游戏来源：多开模拟器时才列出实例让用户选（v0.9.4 加雷电，MuMu 和雷电共用一个实例列表）
  const sources: { v: Settings['gameSource']; label: string; note: string }[] = [
    { v: 'auto', label: '自动', note: '先找 PC 客户端，没开再找 MuMu、雷电模拟器里正在运行的明日方舟。' },
    { v: 'pc', label: 'PC 客户端', note: '只连明日方舟 PC 版。' },
    { v: 'mumu', label: 'MuMu', note: 'MuMu 画面要横屏 16:9（如 1920×1080）。窗口被挡住也能识别。' },
    { v: 'ldplayer', label: '雷电', note: '雷电画面要横屏 16:9（如 1920×1080），并在雷电设置里打开 ADB 调试。窗口被挡住也能识别。' },
  ]
  const kindName = { mumu: 'MuMu', ldplayer: '雷电' }
  let allInstances = $state<EmulatorInstance[]>([])
  const instances = $derived(
    app.settings.gameSource === 'pc' ? [] : allInstances.filter((i) => app.settings.gameSource === 'auto' || i.kind === app.settings.gameSource),
  )
  const loadInstances = async () => (allInstances = app.settings.gameSource === 'pc' ? [] : await api.emulatorInstances())
  // 贴边选项：点哪个（包括当前选中的）都顺便清掉拖动记下的位置，侧栏贴回去（v0.8.2）
  const dock = <K extends 'dockSide' | 'overlaySide'>(k: K, v: (typeof app.settings)[K]) => {
    if (app.settings[k] !== v) set(k, v)
    api.windowCommand('redock')
  }

  // 森空岛账号（v0.8.0）：宿主记下的上次读到的角色；换账号就退出登录。v0.9.0 加「登录」入口，不用先去导入
  let skland = $state<{ nickname: string } | null>(null)
  let sklandBusy = $state(false)
  const sklandLogin = async () => {
    sklandBusy = true
    try {
      const a = await api.sklandLogin()
      if (a) {
        skland = a
        app.showToast(`已登录森空岛：${a.nickname}`)
      }
    } catch (e) {
      app.showToast((e as Error).message, true)
    } finally {
      sklandBusy = false
    }
  }
  const sklandLogout = async () => {
    await api.sklandLogout()
    skland = null
    app.showToast('已退出森空岛登录')
  }
  $effect(() => {
    if (app.settingsOpen) {
      loadInstances()
      plaza.check()
      api.sklandAccount().then((a) => (skland = a))
    }
  })
  const setSource = (v: Settings['gameSource']) => {
    set('gameSource', v)
    // 选了另一种模拟器时，原来选的实例不再适用，回到自动
    const picked = allInstances.find((i) => i.serial === app.settings.emulatorSerial)
    if (picked && v !== 'auto' && picked.kind !== v) set('emulatorSerial', '')
    loadInstances()
  }

  // 界面字体：第一次展开列表时向宿主要本机字体；每个名字用它自己的字体预览
  let fontOpen = $state(false)
  let fontQ = $state('')
  let fonts = $state<string[] | null>(null)
  const shownFonts = $derived((fonts ?? []).filter((f) => !fontQ || f.toLowerCase().includes(fontQ.trim().toLowerCase())))
  function toggleFonts() {
    fontOpen = !fontOpen
    fontQ = ''
    if (fontOpen && !fonts) api.listFonts().then((l) => (fonts = l))
  }
  function pickFont(f: string) {
    set('uiFont', f)
    fontOpen = false
  }

  // 商店标记样式（v0.9.5）：默认关键金、追踪薄荷绿、可抓淡紫；色块之外可以自选
  const MARKER_KINDS = [
    { id: 'key', label: '关键', note: '目标里标了关键的干员、优先装备' },
    { id: 'tracked', label: '追踪', note: '追踪列表里的其他干员和装备' },
    { id: 'pickable', label: '可抓', note: '目标盟约里还能抓的，虚线框' },
  ] as const
  const MARKER_COLORS = ['#3CF0C2', '#F3B322', '#A98BFF', '#4FB8FF', '#FF6FB1', '#FF8A3D', '#FF5A5A', '#F2F2F2']
  const ms = $derived(app.settings.markerStyles)
  const setMarker = (k: 'key' | 'tracked' | 'pickable', patch: Partial<MarkerStyle>) => set('markerStyles', { ...ms, [k]: { ...ms[k], ...patch } })

  const tab = $derived(app.settingsTab)
  let scroller = $state<HTMLDivElement>()
  const tabs = $derived<{ id: SettingsTab; label: string; dot?: boolean }[]>([
    { id: 'game', label: '游戏' },
    { id: 'window', label: '界面' },
    { id: 'keys', label: '按键' },
    // 自动操作没实机测过（393），正式版先不显示这一页（2026-11-19 用户定），开发预览里照常显示
    ...(import.meta.env.DEV ? [{ id: 'control' as const, label: '自动' }] : []),
    { id: 'account', label: '账号' },
    { id: 'about', label: '关于', dot: app.update.state === 'available' },
  ])

  // 按键录制：修饰键按 Ctrl、Alt、Shift、Win 的顺序，再接主键（写法见 Settings.toggleHotkey）。
  // v0.7.2 起允许单键：单键只在游戏或助手在前台时生效、不拦截按键（宿主实现）
  let recording = $state<'toggle' | 'refresh' | 'route' | 'field' | 'stop' | null>(null)
  let hkMsg = $state('')
  /** hkMsg 是哪一个按键给的，提示显示在那一行下面 */
  let hkFor = $state<'toggle' | 'refresh' | 'route' | 'field' | 'stop' | null>(null)
  const keyName = (e: KeyboardEvent) =>
    e.key === ' ' ? 'Space' : /^Key[A-Z]$/.test(e.code) ? e.code.slice(3) : /^Digit\d$/.test(e.code) ? e.code.slice(5) : e.key.length === 1 ? e.key.toUpperCase() : e.key
  // 单键要收得到（v0.9.1）：游戏是管理员身份、助手不是时，单独按的键收不到，只收组合键
  const SINGLE_BLOCKED = '游戏是以管理员身份运行的，单独一个键收不到：换成组合键（如 Alt+R），或者打开下面的「以管理员身份运行」。'
  const MOUSE_BLOCKED = '游戏是以管理员身份运行的，鼠标侧键收不到：换成组合键（如 Alt+R），或者打开下面的「以管理员身份运行」。'
  const MODS = ['Control', 'Alt', 'Shift', 'Meta']
  // 路线快捷键可以单独按 Alt、Ctrl、Shift：按下修饰键先记着，松开时中间没按别的键才算单按
  let loneMod = $state<string | null>(null)
  function recordKey(e: KeyboardEvent) {
    if (!recording) return
    e.preventDefault()
    e.stopPropagation()
    if (e.key === 'Escape') return void (recording = null)
    if (MODS.includes(e.key)) return void (loneMod = e.key)
    loneMod = null
    const mods = [e.ctrlKey && 'Ctrl', e.altKey && 'Alt', e.shiftKey && 'Shift', e.metaKey && 'Win'].filter(Boolean)
    finishRecord([...mods, keyName(e)].join('+'))
  }
  function finishRecord(combo: string) {
    if (app.keysBlocked && rawOnly(combo)) return void ((hkMsg = mouse(combo) ? MOUSE_BLOCKED : SINGLE_BLOCKED), (hkFor = recording), (recording = null))
    const target = recording
    recording = null
    if (target === 'toggle') applyHotkey(combo)
    else if (target === 'route') setRoutes({ hotkey: combo })
    else if (target === 'field') set('fieldHotkey', combo)
    else if (target === 'stop') control.set({ stopHotkey: combo })
    else set('refreshKey', combo)
  }
  function recordKeyUp(e: KeyboardEvent) {
    if (recording !== 'route' || e.key !== loneMod) return
    e.preventDefault()
    loneMod = null
    recording = null
    if (app.keysBlocked) return void ((hkMsg = SINGLE_BLOCKED), (hkFor = 'route'))
    setRoutes({ hotkey: e.key === 'Control' ? 'Ctrl' : e.key === 'Meta' ? 'Win' : e.key })
  }
  const single = (combo: string) => !!combo && !/(Ctrl|Alt|Shift|Win)\+/.test(combo)
  // 鼠标侧键（v0.9.5）：写作 Mouse4（后退）/ Mouse5（前进），可以带修饰键；和单键一样靠原始输入，管理员身份的游戏里收不到
  const mouse = (combo: string) => /Mouse[45]$/.test(combo)
  const rawOnly = (combo: string) => single(combo) || mouse(combo)
  const keyLabel = (combo: string) => combo.replace('Mouse4', '鼠标后退键').replace('Mouse5', '鼠标前进键')
  const blockedWhat = (combo: string) => (mouse(combo) ? '是鼠标侧键' : '是单独一个键')
  // 录制时按鼠标侧键（e.button 3 后退、4 前进）；按下就算，松开时拦掉浏览器的后退 / 前进
  function recordMouse(e: MouseEvent) {
    if (!recording || (e.button !== 3 && e.button !== 4)) return
    e.preventDefault()
    loneMod = null
    const mods = [e.ctrlKey && 'Ctrl', e.altKey && 'Alt', e.shiftKey && 'Shift', e.metaKey && 'Win'].filter(Boolean)
    finishRecord([...mods, e.button === 3 ? 'Mouse4' : 'Mouse5'].join('+'))
  }
  const blockSideNav = (e: MouseEvent) => (e.button === 3 || e.button === 4) && e.preventDefault()

  // 以管理员身份运行（v0.9.1）：打开会以管理员身份重启，用户不同意时提示原因
  let adminBusy = $state(false)
  let adminMsg = $state('')
  async function setAdmin(on: boolean) {
    adminBusy = true
    adminMsg = ''
    try {
      await api.setRunAsAdmin(on)
      app.settings.runAsAdmin = on
    } catch (e) {
      adminMsg = (e as Error).message
    }
    adminBusy = false
  }
  async function applyHotkey(combo: string) {
    try {
      await api.setHotkey(combo)
      app.settings.toggleHotkey = combo
      app.appInfo.hotkeyFailed = false
      hkMsg = ''
    } catch (e) {
      hkMsg = (e as Error).message
      hkFor = 'toggle'
    }
  }

  // 诊断包（v0.7.2）：识别不了时导出，发群里反馈
  let diagBusy = $state(false)
  let diagMsg = $state<{ text: string; bad?: boolean } | null>(null)
  async function exportDiag() {
    diagBusy = true
    diagMsg = null
    try {
      const path = await api.exportDiagnostics()
      if (path) diagMsg = { text: `已保存到 ${path}` }
    } catch (e) {
      diagMsg = { text: (e as Error).message, bad: true }
    } finally {
      diagBusy = false
    }
  }

  // 帮助改进识别（v1.0.5，任务 235）：开着时宿主存下对局截图和记录，最近 3 局；在这里一局一局导出 zip 发给我们
  let colls = $state<CollectionMatch[]>([])
  let collBusy = $state('')
  let collMsg = $state<{ text: string; bad?: boolean } | null>(null)
  let clearAsk = $state(false)
  $effect(() => {
    if (tab === 'about') api.collections().then((c) => (colls = c))
  })
  async function exportColl(id: string) {
    collBusy = id
    collMsg = null
    try {
      const path = await api.exportCollection(id)
      if (path) collMsg = { text: `已保存到 ${path}，发到 QQ 群就行` }
    } catch (e) {
      collMsg = { text: (e as Error).message, bad: true }
    } finally {
      collBusy = ''
    }
  }
  async function clearColls() {
    if (!clearAsk) return void (clearAsk = true)
    clearAsk = false
    await api.clearCollections()
    colls = await api.collections()
    collMsg = { text: '已清空' }
  }
  const mb = (b: number) => `${(b / 1e6).toFixed(b < 1e7 ? 1 : 0)} MB`
  const collWhen = (iso: string) => {
    const d = new Date(iso)
    return `${d.getMonth() + 1}-${d.getDate()} ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
  }

  // 数据：导出备份、选文件预览后按合并 / 替换导入
  const modes: { id: ImportMode; name: string; note: string }[] = [
    { id: 'merge', name: '合并', note: '加入备份里的阵容和关注，同一套阵容用备份里的；偏好设置不变' },
    { id: 'replace', name: '替换', note: '阵容、关注和偏好设置都换成备份里的' },
  ]
  let picked = $state<{ token: string; summary: BackupSummary } | null>(null)
  let mode = $state<ImportMode>('merge')
  let busy = $state(false)
  let dataMsg = $state<{ text: string; bad?: boolean } | null>(null)
  async function run(f: () => Promise<void>) {
    busy = true
    dataMsg = null
    try {
      await f()
    } catch (e) {
      dataMsg = { text: (e as Error).message, bad: true }
    } finally {
      busy = false
    }
  }
  const doExport = () =>
    run(async () => {
      const path = await api.exportData()
      if (path) dataMsg = { text: `已导出到 ${path}` }
    })
  const doPick = () =>
    run(async () => {
      picked = await api.pickBackup()
      mode = 'merge'
    })
  const doImport = () =>
    run(async () => {
      await api.importData(picked!.token, mode)
      const n = picked!.summary.lineups
      picked = null
      await app.reloadData()
      dataMsg = { text: `已导入 ${n} 套阵容` }
    })
</script>

<svelte:window onkeydown={(e) => e.key === 'Escape' && close()} onmousedown={recordMouse} onmouseup={blockSideNav} />

{#if app.settingsOpen}
  <div class="scrim" transition:fade={{ duration: 160 }} onclick={close} role="presentation"></div>
  <aside class="sheet win" transition:sheet={{ y: 360, duration: 240 }}>
    <header>
      <h3>设置</h3>
      <span class="term">Settings</span>
      <button class="x" onclick={close} aria-label="关闭"><Icon name="close" size={16} /></button>
    </header>
    <!-- 分页（v0.9.5）：原来一长条要翻很久，按用途分五页；切页时回到顶部 -->
    <div class="tabs"><Seg items={tabs} value={tab} onchange={(v) => ((app.settingsTab = v), scroller && (scroller.scrollTop = 0))} height={26} /></div>
    <div class="scroll" bind:this={scroller}>

    {#if tab === 'game'}
    <section>
      <h4>游戏来源 <small>PC 客户端或安卓模拟器</small></h4>
      <div class="scales four">
        {#each sources as s (s.v)}
          <button class:on={app.settings.gameSource === s.v} onclick={() => setSource(s.v)}>{s.label}</button>
        {/each}
      </div>
      <p class="note">{sources.find((s) => s.v === app.settings.gameSource)!.note}</p>
      {#if app.settings.gameSource !== 'pc' && instances.length > 1}
        <div class="themes inst">
          <button class:on={!app.settings.emulatorSerial} onclick={() => set('emulatorSerial', '')}>
            <span class="radio"></span><b>自动</b><em>只有一个实例在运行明日方舟时用它</em>
          </button>
          {#each instances as i (i.serial)}
            <button class:on={app.settings.emulatorSerial === i.serial} onclick={() => set('emulatorSerial', i.serial)}>
              <span class="radio"></span><b>{i.name}</b><em>{kindName[i.kind]} · {i.serial}</em>
            </button>
          {/each}
        </div>
      {/if}
      {#if app.settings.gameSource !== 'pc'}
        <!-- 自动找不到或连错地址时（如 MuMu 桥接网络、远程设备），手动填 ADB 地址 -->
        <div class="usrc">
          <span class="hkl">ADB 地址</span>
          <div class="scales two">
            <button class:on={!adbManual} onclick={() => setAdbManual(false)}>自动</button>
            <button class:on={adbManual} onclick={() => setAdbManual(true)}>手动填</button>
          </div>
        </div>
        {#if adbManual}
          <div class="cdk">
            <input
              class:bad={adbBad}
              bind:value={adbDraft}
              placeholder="127.0.0.1:16384"
              spellcheck="false"
              autocomplete="off"
              onblur={commitAdb}
              onkeydown={(e) => e.key === 'Enter' && e.currentTarget.blur()}
            />
          </div>
          <p class="note" class:bad={adbBad}>
            {adbBad ? '格式是 地址:端口，如 127.0.0.1:16384、192.168.1.5:5555，或 emulator-5554' : '只连这个地址，不再自动找。在模拟器的 ADB 设置里能看到地址，用 adb devices 能连上的就行。'}
          </p>
        {/if}
      {/if}
      {#if app.settings.gameSource === 'pc' || app.settings.gameSource === 'auto'}
        <label class="row">
          <span class="lbl"><b>去掉黄框</b><small>Windows 10 上游戏窗口外有一圈黄框时打开</small></span>
          <input type="checkbox" class="sw" checked={app.settings.hideCaptureBorder} onchange={(e) => set('hideCaptureBorder', e.currentTarget.checked)} />
        </label>
        {#if app.settings.hideCaptureBorder}
          <p class="note">改用老的截图方式：游戏窗口要在最前面、别被其他窗口挡住，不然识别不了。</p>
        {/if}
      {/if}
    </section>

    <section>
      <h4>多开助手 <small>同时连不同的游戏窗口，比如自己玩电脑版、另一个助手连模拟器</small></h4>
      {#if profile === null}
        <label class="row">
          <span class="lbl"><b>允许多开</b><small>打开后再双击助手会新开一个；第二个起默认不占全局快捷键</small></span>
          <input type="checkbox" class="sw" checked={app.settings.multiInstance} onchange={(e) => set('multiInstance', e.currentTarget.checked)} />
        </label>
      {:else}
        <p class="note">这是配置「{profile}」，用自己的一套设置和数据。允许多开的开关在默认配置的设置里。</p>
      {/if}
      {#if profile !== null || app.settings.multiInstance}
        <div class="profs">
          {#each [null, ...profiles] as p (p ?? '')}
            <div class="pf" class:cur={p === profile}>
              <b>{p ?? '默认'}</b>
              {#if p === profile}<em>当前</em>{:else}<button class="btn" onclick={() => openProfile(p)}>打开</button>{/if}
            </div>
          {/each}
        </div>
        <div class="cdk">
          <input bind:value={newProfile} placeholder="新配置名，如 agent（英文字母、数字、短横线）" maxlength="24" spellcheck="false" autocomplete="off"
            onkeydown={(e) => e.key === 'Enter' && addProfile()} />
          <button class="btn" disabled={!newProfile.trim()} onclick={addProfile}>新建并打开</button>
        </div>
        {#if profileError}<p class="note bad">{profileError}</p>{/if}
      {/if}
    </section>

    <section>
      <h4>游戏主题 <small>游戏里正在用的主题，商店识别要靠它</small></h4>
      <!-- 自动识别（v0.9.2，默认）：宿主从游戏画面认；手动选了某个就固定住 -->
      <div class="themes">
        <button class:on={app.settings.gameThemeAuto} onclick={() => set('gameThemeAuto', true)}>
          <span class="radio"></span>
          <b>自动识别</b>
          <em>{app.connection.theme ? `现在认到：${themes.find((t) => t.id === app.connection.theme)?.name}` : app.connection.state === 'connected' ? '进休整期后自动认' : '连上游戏后自动认'}</em>
        </button>
        {#each themes as t (t.id)}
          <button class:on={!app.settings.gameThemeAuto && app.settings.gameTheme === t.id} onclick={() => (set('gameThemeAuto', false), set('gameTheme', t.id))}>
            <span class="radio"></span>
            <b>{t.name}</b>
            {#if t.note}<em>{t.note}</em>{/if}
          </button>
        {/each}
      </div>
    </section>

    <section>
      <label class="row">
        <span class="lbl"><b>商店标记</b><small>在游戏商店里给追踪的干员打标记</small></span>
        <input type="checkbox" class="sw" checked={app.settings.shopMarkers} onchange={(e) => set('shopMarkers', e.currentTarget.checked)} />
      </label>
      <div class="hk">
        <span class="hkl">识别速度</span>
        <div class="scales two grow">
          <button class:on={app.settings.shopScanSpeed === 'normal'} onclick={() => set('shopScanSpeed', 'normal')}>标准</button>
          <button class:on={app.settings.shopScanSpeed === 'fast'} onclick={() => set('shopScanSpeed', 'fast')}>快速</button>
        </div>
      </div>
      <p class="note">{app.settings.shopScanSpeed === 'fast' ? '识别得更勤，换商店后标记跟得更快，会多占一些 CPU；电脑吃力时换回标准。' : '占用低，换商店后标记稍慢一点跟上。'}</p>
      <!-- 标记样式（v0.9.5）：三种标记各自开关、换颜色；左边小卡是缩小的标记预览，跟着设置变 -->
      {#if app.settings.shopMarkers}
        <div class="mks">
          {#each MARKER_KINDS as k (k.id)}
            {@const st = ms[k.id]}
            <div class="mk" class:off={!st.show}>
              <span class="mprev" class:dash={k.id === 'pickable'} style:--mc={st.color}>
                {#if ms.labels}<i class="mtag">{k.id === 'pickable' ? '' : '★ '}{k.label}</i>{/if}
              </span>
              <span class="lbl"><b>{k.label}</b><small>{k.note}</small></span>
              <input type="checkbox" class="sw" checked={st.show} onchange={(e) => setMarker(k.id, { show: e.currentTarget.checked })} aria-label="显示{k.label}标记" />
            </div>
            {#if st.show}
              <div class="swatches">
                {#each MARKER_COLORS as c (c)}
                  <button class:on={st.color.toLowerCase() === c.toLowerCase()} style:--c={c} onclick={() => setMarker(k.id, { color: c })} aria-label="换成 {c}"></button>
                {/each}
                <!-- 自选颜色：系统取色器 -->
                <label class="custom" class:on={!MARKER_COLORS.some((c) => c.toLowerCase() === st.color.toLowerCase())} style:--c={st.color} title="自选颜色">
                  <input type="color" value={st.color} onchange={(e) => setMarker(k.id, { color: e.currentTarget.value })} />
                  <Icon name="pencil" size={11} />
                </label>
              </div>
            {/if}
          {/each}
          <label class="row">
            <span class="lbl"><b>文字标签</b><small>框上方的「★ 追踪」这类字；嫌挡就关掉</small></span>
            <input type="checkbox" class="sw" checked={ms.labels} onchange={(e) => set('markerStyles', { ...ms, labels: e.currentTarget.checked })} />
          </label>
        </div>
      {/if}
    </section>

    <!-- 敌人路线（v0.9.0）：休整期在棋盘上画本回合路线；本局页「敌方情报」里的快速开关就是这里的总开关 -->
    <section>
      <label class="row">
        <span class="lbl"><b>敌人路线</b><small>休整期在游戏棋盘上画本回合敌人会走的路线；本局页「敌方情报」也能快速开关</small></span>
        <input type="checkbox" class="sw" checked={rt.enabled} onchange={(e) => setRoutes({ enabled: e.currentTarget.checked })} />
      </label>
      <div class="hk">
        <span class="hkl">显示方式</span>
        <div class="scales three grow">
          <button class:on={rt.display === 'always'} onclick={() => setRoutes({ display: 'always' })}>一直显示</button>
          <button class:on={rt.display === 'hold'} disabled={app.keysBlocked} title={app.keysBlocked ? '要打开「以管理员身份运行」才能用' : undefined} onclick={() => setRoutes({ display: 'hold' })}>按住才显示</button>
          <button class:on={rt.display === 'peek'} onclick={() => setRoutes({ display: 'peek' })}>新回合先看</button>
        </div>
      </div>
      {#if rt.display === 'peek'}
        <div class="hk">
          <span class="hkl">先显示</span>
          <input class="rng" type="range" min="3" max="15" step="1" value={rt.peekSeconds} oninput={(e) => setRoutes({ peekSeconds: +e.currentTarget.value })} />
          <span class="rngv">{rt.peekSeconds} 秒</span>
        </div>
      {/if}
      <div class="hk">
        <span class="hkl">快捷键</span>
        <button class="hkb" class:rec={recording === 'route'} onclick={() => ((recording = recording === 'route' ? null : 'route'), (hkMsg = ''))} onkeydown={recordKey} onkeyup={recordKeyUp}>
          {recording === 'route' ? '按下想用的键…' : keyLabel(rt.hotkey)}
        </button>
      </div>
      {#if hkMsg && recording === null && hkFor === 'route'}
        <p class="note bad">{hkMsg}</p>
      {:else if recording === 'route'}
        <p class="note">{app.keysBlocked ? '按一个组合键，如 Alt+R；Esc 取消。' : '单按一个键（Alt、Ctrl、Shift 单独按也算）、组合键（如 Alt+R）或鼠标侧键；Esc 取消。'}</p>
      {:else if app.keysBlocked && rawOnly(rt.hotkey)}
        <p class="note bad">「{keyLabel(rt.hotkey)}」{blockedWhat(rt.hotkey)}，游戏是以管理员身份运行的，现在收不到。换成组合键，或者打开下面的「以管理员身份运行」。</p>
      {:else}
        <p class="note">
          {#if app.keysBlocked && rt.display === 'hold'}
            现在收不到「按住」和「松开」，先按一直显示处理；按 {keyLabel(rt.hotkey)} 临时隐藏，再按一次显示。
          {:else if rt.display === 'always'}
            休整期一直显示，进入战斗、离开棋盘时自动隐藏；按 {keyLabel(rt.hotkey)} 临时隐藏，再按一次显示。
          {:else if rt.display === 'hold'}
            平时不显示，按住 {keyLabel(rt.hotkey)} 时显示，松开就隐藏。
          {:else}
            每个新回合进休整期时显示 {rt.peekSeconds} 秒再淡出，之后{app.keysBlocked ? '按一下' : '按住'} {keyLabel(rt.hotkey)} 还能再看。
          {/if}
          {app.keysBlocked ? '这个组合键由助手占用，游戏里收不到。' : '按键照样会传给游戏，别选游戏里有用的键。'}
        </p>
      {/if}
      <div class="hk">
        <span class="hkl">路线多时</span>
        <div class="scales two grow">
          <button class:on={rt.overlap === 'cycle'} onclick={() => setRoutes({ overlap: 'cycle' })}>依次循环</button>
          <button class:on={rt.overlap === 'all'} onclick={() => setRoutes({ overlap: 'all' })}>全部一起</button>
        </div>
      </div>
      {#if rt.overlap === 'cycle'}
        <div class="hk">
          <span class="hkl">每条停留</span>
          <input class="rng" type="range" min="1" max="5" step="0.1" value={rt.cycleSeconds} oninput={(e) => setRoutes({ cycleSeconds: +e.currentTarget.value })} />
          <span class="rngv">{rt.cycleSeconds.toFixed(1)} 秒</span>
        </div>
      {/if}
      <div class="hk">
        <span class="hkl">飞行路线</span>
        <div class="scales three grow">
          <button class:on={rt.fly === 'main'} onclick={() => setRoutes({ fly: 'main' })}>最多的一条</button>
          <button class:on={rt.fly === 'all'} onclick={() => setRoutes({ fly: 'all' })}>全部</button>
          <button class:on={rt.fly === 'off'} onclick={() => setRoutes({ fly: 'off' })}>不显示</button>
        </div>
      </div>
      <div class="hk">
        <span class="hkl">联机首领</span>
        <div class="scales three grow">
          <button class:on={rt.side === 'auto'} onclick={() => setRoutes({ side: 'auto' })}>自动</button>
          <button class:on={rt.side === 'left'} onclick={() => setRoutes({ side: 'left' })}>我在左侧</button>
          <button class:on={rt.side === 'right'} onclick={() => setRoutes({ side: 'right' })}>我在右侧</button>
        </div>
      </div>
      <p class="note">联机的首领回合，会移动的首领左右各一只、路线不一样，只画你这一侧；自动认不出时两边都画。</p>
    </section>

    <!-- 场上自动识别（v1.0.1）的开关 v1.2.4 起暂时去掉（用户 2026-11-16）：一拖人就重认、推荐位一直变；
         现在只在本局页「场上」点「识别」或按快捷键时认一次、算一次推荐 -->

    <!-- 覆盖层新加的两项（v1.0.0，任务 169）：和商店标记、敌人路线一样各自开关；都只在休整期棋盘主画面画 -->
    <section>
      <label class="row">
        <span class="lbl"><b>推荐站位</b><small>在游戏棋盘的格子上标出推荐放谁；有站位推荐时才画</small></span>
        <input type="checkbox" class="sw" checked={ol.placement} onchange={(e) => set('overlayLayers', { ...ol, placement: e.currentTarget.checked })} />
      </label>
      <label class="row">
        <span class="lbl"><b>待确认提示</b><small>认不准是谁的小人，头上标一个问号；到本局页「场上」小棋盘里点选</small></span>
        <input type="checkbox" class="sw" checked={ol.pending} onchange={(e) => set('overlayLayers', { ...ol, pending: e.currentTarget.checked })} />
      </label>
      <p class="note">这两项和上面的商店标记、敌人路线都画在游戏画面上，各自开关；只在休整期、没有面板盖住棋盘时画。</p>
    </section>
    {/if}
    {#if tab === 'window'}
    <section>
      <h4>界面主题 <small>助手界面的样子，和游戏里用哪套无关</small></h4>
      <div class="themes">
        {#each skins as t (t.id)}
          <button class:on={app.settings.uiSkin === t.id} onclick={() => set('uiSkin', t.id)}>
            <span class="radio"></span>
            <b>{t.name}</b>
            {#if t.note}<em>{t.note}</em>{/if}
          </button>
        {/each}
      </div>
      {#if app.settings.uiSkin === 'glass' || app.settings.uiSkin === 'round'}
        <div class="scheme">
          <span>配色</span>
          <div class="scales">
            {#each schemes as c (c.v)}
              <button class:on={app.settings.uiScheme === c.v} onclick={() => set('uiScheme', c.v)}>{c.label}</button>
            {/each}
          </div>
        </div>
        <div class="scheme">
          <span>主题色</span>
          <div class="accents">
            {#each ACCENTS as a (a.v)}
              <button
                class="acc"
                class:def={!a.v}
                class:on={app.settings.uiAccent === a.v}
                style:--sw={a.v || undefined}
                title={a.name}
                aria-label={a.name}
                onclick={() => set('uiAccent', a.v)}
              ></button>
            {/each}
            <!-- 自选：系统取色器 -->
            <label class="acc pick" class:on={customAccent} style:--sw={customAccent ? app.settings.uiAccent : undefined} title="自选颜色">
              <input type="color" value={app.settings.uiAccent || '#3b82f6'} oninput={(e) => pickAccent(e.currentTarget.value)} />
            </label>
          </div>
        </div>
      {/if}
    </section>

    <section>
      <h4>界面大小 <small>自动：和游戏界面一样大</small></h4>
      <div class="scales four">
        {#each scales as s (s.v)}
          <button class:on={app.settings.uiScale === s.v} onclick={() => set('uiScale', s.v)}>{s.label}</button>
        {/each}
      </div>
      {#if typeof app.settings.uiScale === 'number' && app.settings.uiScale < 1}
        <p class="note">比 100% 小的档适合系统缩放开到 150% 以上的屏幕；系统缩放是 100% 时字会很小。</p>
      {/if}
    </section>

    <!-- 界面字体（v0.9.5）：本机字体列表由宿主给，每个名字用它自己的字体显示；选了马上生效 -->
    <section>
      <h4>界面字体 <small>选了马上生效；缺字时用默认字体补上</small></h4>
      <button class="fpick" class:open={fontOpen} onclick={toggleFonts} style:font-family={fontStack(app.settings.uiFont)}>
        <span>{app.settings.uiFont || '默认（MiSans）'}</span>
        <Icon name="arrowDown" size={14} />
      </button>
      {#if fontOpen}
        <div class="flist">
          <div class="cdk">
            <input bind:value={fontQ} placeholder="搜索字体" spellcheck="false" autocomplete="off" />
          </div>
          <div class="fscroll">
            {#if !fontQ}
              <button class:on={!app.settings.uiFont} style:font-family={fontStack('')} onclick={() => pickFont('')}>默认（MiSans）</button>
            {/if}
            {#each shownFonts as f (f)}
              <button class:on={app.settings.uiFont === f} style:font-family={fontStack(f)} onclick={() => pickFont(f)}>{f}</button>
            {:else}
              {#if fonts}<p class="note">没有叫这个的字体。</p>{:else}<p class="note">正在读取本机字体…</p>{/if}
            {/each}
          </div>
        </div>
      {/if}
      <div class="hk">
        <span class="hkl">粗细</span>
        <div class="scales three grow">
          {#each [['normal', '标准'], ['medium', '稍粗'], ['bold', '加粗']] as const as [v, label] (v)}
            <button class:on={app.settings.uiFontWeight === v} onclick={() => set('uiFontWeight', v)}>{label}</button>
          {/each}
        </div>
      </div>
      <p class="note">有的字体只有常规和粗体两档（如微软雅黑），「稍粗」可能看不出变化。</p>
    </section>

    <section>
      <label class="row">
        <span class="lbl"
          ><b>跟随游戏窗口</b><small
            >{app.workbench
              ? '工作台里暂停跟随，收回侧栏后照这里的设置恢复'
              : app.settings.followGame
              ? '侧栏贴着游戏窗口，全屏或旁边放不下时盖在游戏画面上'
              : '侧栏停在你拖到的位置，不再跟着游戏；要盖在全屏游戏上请打开置顶'}</small
          ></span
        >
        <input type="checkbox" class="sw" checked={app.settings.followGame} onchange={(e) => set('followGame', e.currentTarget.checked)} />
      </label>
      {#if app.settings.followGame}
        <!-- 贴哪边、覆盖时盖哪边；收起竖条跟着贴在游戏窗口那条边 -->
        <div class="hk">
          <span class="hkl">贴在游戏</span>
          <div class="scales three grow">
            <button class:on={app.settings.dockSide === 'auto'} onclick={() => dock('dockSide', 'auto')}>自动</button>
            <button class:on={app.settings.dockSide === 'right'} onclick={() => dock('dockSide', 'right')}>右边</button>
            <button class:on={app.settings.dockSide === 'left'} onclick={() => dock('dockSide', 'left')}>左边</button>
          </div>
        </div>
        <div class="hk">
          <span class="hkl">盖在画面</span>
          <div class="scales two grow">
            <button class:on={app.settings.overlaySide === 'left'} onclick={() => dock('overlaySide', 'left')}>左侧</button>
            <button class:on={app.settings.overlaySide === 'right'} onclick={() => dock('overlaySide', 'right')}>右侧</button>
          </div>
        </div>
        <p class="note">
          {app.settings.dockSide === 'auto' ? '右边放得下贴右边，不够就贴左边，两边都放不下（或全屏）时盖在游戏画面上。' : `只贴${app.settings.dockSide === 'right' ? '右' : '左'}边，放不下（或全屏）时盖在游戏画面上。`}
          {app.settings.overlaySide === 'right' ? '盖在右侧会挡住准备就绪、刷新这些按钮附近。' : ''}收成竖条时，竖条贴着游戏窗口那条边。
          拖动侧栏会记住位置；拖回边上附近，或者再点一下上面的选项，就贴回去。
        </p>
      {/if}
    </section>

    <section>
      <h4>收起方式 <small>标题栏的收起按钮</small></h4>
      <div class="scales two">
        <button class:on={app.settings.collapseMode === 'strip'} onclick={() => set('collapseMode', 'strip')}>收成竖条</button>
        <button class:on={app.settings.collapseMode === 'up'} onclick={() => set('collapseMode', 'up')}>向上收起</button>
      </div>
      <p class="note">{app.settings.collapseMode === 'strip' ? '收成左边一根竖条，追踪的头像竖着排。' : '宽度不变，只留标题栏和追踪列表，打商店时还能点掉追踪。'}</p>
    </section>

    <section>
      <h4>启动时打开 <small>侧栏贴着游戏，工作台是宽窗口</small></h4>
      <div class="scales three">
        <button class:on={app.settings.startupMode === 'remember'} onclick={() => set('startupMode', 'remember')}>记住上次</button>
        <button class:on={app.settings.startupMode === 'sidebar'} onclick={() => set('startupMode', 'sidebar')}>侧栏</button>
        <button class:on={app.settings.startupMode === 'workbench'} onclick={() => set('startupMode', 'workbench')}>工作台</button>
      </div>
    </section>
    {/if}
    {#if tab === 'keys'}
    <section class="keyrows">
      <h4>快捷键 <small>游戏在前台时也能按</small></h4>
      <!-- 全局快捷键：游戏在前台也能按；点按钮后按下新组合 -->
      <div class="hk">
        <span class="hkl">快捷键收起 / 展开</span>
        <button class="hkb" class:rec={recording === 'toggle'} onclick={() => ((recording = recording === 'toggle' ? null : 'toggle'), (hkMsg = ''))} onkeydown={recordKey}>
          {recording === 'toggle' ? '按下新的按键…' : keyLabel(app.settings.toggleHotkey) || '未设置'}
        </button>
        {#if app.settings.toggleHotkey && recording !== 'toggle'}<button class="link" onclick={() => applyHotkey('')}>关闭</button>{/if}
      </div>
      {#if hkMsg && hkFor === 'toggle'}
        <p class="note bad">{hkMsg}</p>
      {:else if recording === 'toggle'}
        <p class="note">{app.keysBlocked ? 'Ctrl / Alt / Shift 加一个键；Esc 取消。' : '单按一个键（如 Tab、`）、Ctrl / Alt / Shift 加一个键，或按鼠标侧键；Esc 取消。'}</p>
      {:else if app.keysBlocked && rawOnly(app.settings.toggleHotkey)}
        <p class="note bad">「{keyLabel(app.settings.toggleHotkey)}」{blockedWhat(app.settings.toggleHotkey)}，游戏是以管理员身份运行的，在游戏里按收不到。换成组合键，或者打开下面的「以管理员身份运行」。</p>
      {:else if app.appInfo.hotkeyFailed && app.settings.toggleHotkey}
        <p class="note bad">「{keyLabel(app.settings.toggleHotkey)}」被别的软件占用了，没生效，换一个试试。</p>
      {:else if rawOnly(app.settings.toggleHotkey)}
        <p class="note">{mouse(app.settings.toggleHotkey) ? '鼠标侧键' : '单键'}只在游戏或助手在前台时生效，按键照样会传给游戏，别选游戏里有用的键。</p>
      {:else}
        <p class="note">游戏在前台时也能按：侧栏里收起、展开；工作台里最小化、恢复。</p>
      {/if}
      <!-- 商店刷新键：游戏里刷新商店时先把旧标记藏起来 -->
      <div class="hk">
        <span class="hkl">商店刷新键</span>
        <button class="hkb" class:rec={recording === 'refresh'} onclick={() => ((recording = recording === 'refresh' ? null : 'refresh'), (hkMsg = ''))} onkeydown={recordKey}>
          {recording === 'refresh' ? '按下游戏里的刷新键…' : keyLabel(app.settings.refreshKey) || '未设置'}
        </button>
        {#if app.settings.refreshKey && recording !== 'refresh'}<button class="link" onclick={() => set('refreshKey', '')}>关闭</button>{/if}
      </div>
      {#if hkMsg && hkFor === 'refresh'}
        <p class="note bad">{hkMsg}</p>
      {:else if app.keysBlocked && app.settings.refreshKey}
        <p class="note bad">游戏是以管理员身份运行的，在游戏里按 {keyLabel(app.settings.refreshKey)} 收不到。要用的话打开下面的「以管理员身份运行」；不用也行，换商店后标记会自己跟上。</p>
      {:else}
        <p class="note">游戏里按这个键刷新商店时，旧的商店标记马上消失，认到新的商店再标；游戏默认是 D。</p>
      {/if}
      <!-- 识别场上（v1.0.1）：和点本局页「场上」的「识别」一样，认一次、算一次推荐 -->
      <div class="hk">
        <span class="hkl">识别场上</span>
        <button class="hkb" class:rec={recording === 'field'} onclick={() => ((recording = recording === 'field' ? null : 'field'), (hkMsg = ''))} onkeydown={recordKey}>
          {recording === 'field' ? '按下新的按键…' : keyLabel(app.settings.fieldHotkey ?? '') || '未设置'}
        </button>
        {#if app.settings.fieldHotkey && recording !== 'field'}<button class="link" onclick={() => set('fieldHotkey', '')}>关闭</button>{/if}
      </div>
      {#if hkMsg && hkFor === 'field'}
        <p class="note bad">{hkMsg}</p>
      {:else if recording === 'field'}
        <p class="note">{app.keysBlocked ? 'Ctrl / Alt / Shift 加一个键；Esc 取消。' : '单按一个键、Ctrl / Alt / Shift 加一个键，或按鼠标侧键；Esc 取消。'}</p>
      {:else if app.keysBlocked && rawOnly(app.settings.fieldHotkey ?? '')}
        <p class="note bad">「{keyLabel(app.settings.fieldHotkey)}」{blockedWhat(app.settings.fieldHotkey)}，游戏是以管理员身份运行的，在游戏里按收不到。换成组合键，或者打开下面的「以管理员身份运行」。</p>
      {:else}
        <p class="note">休整期在棋盘画面按一下：认一次场上和整备区的人，算一次推荐站位，游戏画面上的推荐跟着更新。</p>
      {/if}
    </section>

    <p class="note keysnote">敌人路线的快捷键在「游戏」页的敌人路线里设。</p>

    <!-- 以管理员身份运行（v0.9.1）：游戏是管理员身份时，单独按的键要助手也是管理员才收得到 -->
    <section>
      <label class="row">
        <span class="lbl"><b>以管理员身份运行</b><small>游戏是以管理员身份运行的，助手也是管理员才收得到单独按的键：路线的 Alt、「按住才显示」、商店刷新键 D。默认打开，每次启动助手会弹一次系统确认，点「否」就按普通身份运行；游戏不是管理员身份的可以关掉</small></span>
        <input type="checkbox" class="sw" checked={app.settings.runAsAdmin} disabled={adminBusy} onchange={(e) => setAdmin(e.currentTarget.checked)} />
      </label>
      {#if adminMsg}
        <p class="note bad">{adminMsg}</p>
      {:else if adminBusy}
        <p class="note">{app.settings.runAsAdmin ? '正在关闭…' : '正在以管理员身份重启，请在系统弹出的确认框里点「是」。'}</p>
      {:else if app.settings.runAsAdmin && !app.appInfo.elevated}
        <p class="note bad">已经打开，但这次启动没有同意系统确认，助手还是普通身份。重启助手再试一次。</p>
      {:else if app.appInfo.elevated}
        <p class="note">现在是管理员身份，单键都能用。{app.settings.runAsAdmin ? '' : '关掉的设置下次启动生效。'}</p>
      {:else if app.connection.gameElevated === false}
        <p class="note">连上的游戏不是管理员身份运行，不用打开，单键也能用。</p>
      {:else}
        <p class="note">不打开也行：快捷键用组合键（如 Alt+R），按一下切换显示；只是用不了单键和「按住才显示」。</p>
      {/if}
    </section>
    {/if}
    {#if tab === 'control'}
    <!-- 自动操作（任务 390～393，docs/自动操作.md）：总开关先过风险说明页；其余项只在开着时显示 -->
    <section>
      <label class="row">
        <span class="lbl"><b>自动操作</b><small>让助手照推荐、或照外部程序的指令，替你在游戏里点击和拖动。可能导致账号被封，后果自负</small></span>
        <input
          type="checkbox"
          class="sw"
          checked={control.settings.enabled}
          onchange={(e) => {
            const on = e.currentTarget.checked
            e.currentTarget.checked = control.settings.enabled
            control.toggle(on)
          }}
        />
      </label>
      <p class="note">
        {#if !control.consented}第一次打开会先看一页风险说明，勾选同意后才能开。
        {:else}已同意风险说明。<button class="link" onclick={() => (control.riskOpen = true)}>再看一遍</button>{/if}
      </p>
    </section>
    {#if control.settings.enabled}
      <section>
        <h4>执行方式 <small>外部程序要求的也不会比这里更激进</small></h4>
        <Seg items={MODES} value={control.settings.mode} onchange={(v) => control.set({ mode: v })} height={26} />
        <p class="note">{MODES.find((m) => m.id === control.settings.mode)?.tip}。建议从试运行开始，熟悉了再换。</p>
      </section>
      <section class="keyrows">
        <div class="hk">
          <span class="hkl">停止键</span>
          <button class="hkb" class:rec={recording === 'stop'} onclick={() => ((recording = recording === 'stop' ? null : 'stop'), (hkMsg = ''))} onkeydown={recordKey}>
            {recording === 'stop' ? '按下新的按键…' : keyLabel(control.settings.stopHotkey) || '未设置'}
          </button>
        </div>
        {#if hkMsg && hkFor === 'stop'}
          <p class="note bad">{hkMsg}</p>
        {:else if recording === 'stop'}
          <p class="note">建议用组合键（如 Ctrl+Alt+Q），别选游戏里有用的键；Esc 取消。</p>
        {:else if app.keysBlocked && rawOnly(control.settings.stopHotkey)}
          <p class="note bad">游戏是以管理员身份运行的，单独一个键在游戏里按收不到，换成组合键，或者打开「按键」页的「以管理员身份运行」。</p>
        {:else}
          <p class="note">任何时候按一下就停，不用切到助手；同时开了几个助手时，每个都会停。</p>
        {/if}
      </section>
      <section>
        <h4>电脑版和模拟器 <small>操作方式不一样</small></h4>
        <p class="note ctl">
          <b>电脑客户端</b>只认真实的鼠标和键盘：执行时助手把游戏切到前台、接管鼠标，这期间别动鼠标键盘，一动就停；所以电脑版没法一边自己玩、一边让助手操作。
          游戏是以管理员身份运行时，助手也要以管理员身份运行。
        </p>
        <p class="note ctl"><b>模拟器</b>（MuMu、雷电）走 ADB，不占鼠标，窗口可以在后台。想自己玩一个号、让 agent 玩另一个号，agent 那边用模拟器，再开一个助手连它。</p>
      </section>
      <section>
        <h4>外部程序 <small>给 agent、脚本用</small></h4>
        <label class="row">
          <span class="lbl"><b>本机接口</b><small>在本机开一个只有这台电脑能连的接口，外部程序能读当前局面、站位推荐</small></span>
          <input type="checkbox" class="sw" checked={control.settings.api} onchange={(e) => control.set({ api: e.currentTarget.checked })} />
        </label>
        <label class="row">
          <span class="lbl"><b>允许外部程序操作游戏</b><small>关着时外部程序只能读、不能下发操作</small></span>
          <input type="checkbox" class="sw" checked={control.settings.external} disabled={!control.settings.api} onchange={(e) => control.set({ external: e.currentTarget.checked })} />
        </label>
        {#if control.settings.api}
          <p class="note">
            {#if control.address}接口地址 <code>{control.address}</code>（口令在本配置的数据文件夹 api.json 里）。{:else}接口正在启动…{/if}
            命令行：<code>sa{control.profile === 'default' ? '' : ` --profile ${control.profile}`} state</code> 读当前局面，<code>sa --help</code> 看全部命令。
          </p>
        {/if}
      </section>
    {/if}
    {/if}
    {#if tab === 'account'}
    <section>
      <h4>阵容广场账号 <small>发布阵容、点赞要登录；浏览不用</small></h4>
      {#if plaza.me}
        <p class="acct"><span>已登录：<b>{plaza.me.nickname ?? '（还没起昵称）'}</b><em>{plaza.me.emailMasked}</em></span>
          <button class="link" onclick={() => api.openUrl(PLAZA + '/me')}>网站上的「我的」</button>
          <button class="link" onclick={() => plaza.logout()}>退出登录</button>
        </p>
      {:else}
        <p class="acct"><span>{plaza.checked ? '没有登录。' : '…'}</span>
          <button class="link" disabled={plaza.loggingIn} onclick={() => plaza.login().catch((e) => app.showToast(e.message, true))}>{plaza.loggingIn ? '等待登录…' : '登录'}</button>
          <button class="link" onclick={() => api.openUrl(PLAZA)}>打开阵容广场网站</button>
        </p>
      {/if}
      <!-- 新消息提醒（v0.9.3）：怕吵可以关；关了也能在广场分段里点「消息」看 -->
      <label class="row">
        <span class="lbl"
          ><b>新消息提醒</b><small>有人评论、回复、收藏、点赞你的阵容时，标题栏铃铛显示未读数（每 10 分钟查一次）。关掉也能在广场分段里点「消息」看</small></span
        >
        <input type="checkbox" class="sw" checked={app.settings.plazaNotices} onchange={(e) => set('plazaNotices', e.currentTarget.checked)} />
      </label>
      <!-- 云同步（v0.7.0）：登录后才能打开 -->
      <label class="row sync">
        <span class="lbl"
          ><b>同步我的阵容和偏好</b><small
            >{plaza.me ? '我的阵容（含阶段、简介）、特别关注、游戏主题、商店标记、我的默认跟着账号走，换电脑、在网站「我的阵容」里都能用' : '登录阵容广场后才能打开'}</small
          ></span
        >
        <input type="checkbox" class="sw" disabled={!plaza.me && !app.settings.cloudSync.enabled} checked={app.settings.cloudSync.enabled} onchange={(e) => cloud.setEnabled(e.currentTarget.checked)} />
      </label>
      {#if app.settings.cloudSync.enabled}
        <p class="note" class:bad={cloud.status === 'error'}>
          {cloud.status === 'syncing' ? '正在同步…' : cloud.status === 'error' ? `同步失败：${cloud.error}` : !plaza.token ? '没有登录，登录后接着同步。' : cloud.lastAt ? '已同步。改动后几秒内自动上传。' : '启动时和改动后自动同步。'}
          {#if plaza.token && cloud.status !== 'syncing'}<button class="link" onclick={() => cloud.run()}>立即同步</button>{/if}
        </p>
      {/if}
    </section>

    <section>
      <h4>森空岛账号 <small>导入森空岛攻略和历史战绩时用</small></h4>
      <p class="acct">
        {#if skland}
          <span>已登录：<b>{skland.nickname}</b></span>
          <button class="link" onclick={sklandLogout}>退出登录</button>
        {:else}
          <span>{sklandBusy ? '在弹出的森空岛窗口里登录，登录好会自动关掉。' : '还没登录森空岛。'}</span>
          <button class="link" disabled={sklandBusy} onclick={sklandLogin}>{sklandBusy ? '登录中…' : '登录'}</button>
        {/if}
      </p>
    </section>

    <section>
      <label class="row">
        <span class="lbl"><b>帮大家补全资金表</b><small>每局结束匿名上报各回合的起始资金（模式、回合、资金、策略），不带账号；资金表让没打过的模式也能预计下回合资金</small></span>
        <input type="checkbox" class="sw" checked={app.settings.shareIncome} onchange={(e) => set('shareIncome', e.currentTarget.checked)} />
      </label>
      {#if app.settings.shareIncome}
        <p class="note">只带一个本机随机编号，防止一个人刷数据。<button class="link" onclick={() => api.resetShareId().then(() => app.showToast('已换了一个新编号'))}>重置编号</button></p>
      {/if}
    </section>
    {/if}
    {#if tab === 'about'}
    <!-- 产品名（v1.0 改名）：名字是群友「明」提议的；老名字留作副标题，方便老用户认出来 -->
    <div class="brand">
      <img src="./brand/icon-128.png" alt="" width="56" height="56" draggable="false" />
      <div>
        <b>高效雅雅·卫戍协议助手</b>
        <span class="term">GXYY · Stronghold Assistant</span>
        <small>原名「卫戍协议助手」，感谢群友「明」提议的新名字</small>
      </div>
    </div>
    <section>
      <h4>版本 <small class="term">v{app.appInfo.version}</small></h4>
      <div class="upd">
        {#if u.state === 'available'}
          <span class="msg new">发现新版本 <b>v{u.version}</b></span>
          <!-- 发现新版本后也能重新检查：可能又有了更新的版本 -->
          <button class="btn icon" title="重新检查" aria-label="重新检查" onclick={() => app.checkUpdate()}><Icon name="refresh" size={14} /></button>
          <button class="btn main" onclick={() => app.applyUpdate()}>更新</button>
        {:else if u.state === 'downloading'}
          <span class="msg">正在下载 v{u.version}</span>
          <span class="bar"><i style:width="{(u.progress ?? 0) * 100}%"></i></span>
        {:else if u.state === 'ready' && !app.updateTried}
          <span class="msg new">v{u.version} 已下好</span>
          <button class="btn main" onclick={() => app.applyUpdate()}>安装并重启</button>
        {:else if u.state === 'ready'}
          <span class="msg new">正在安装，马上重启</span>
        {:else}
          <span class="msg" class:bad={u.state === 'failed'}>
            {u.state === 'checking' ? '正在检查…' : u.state === 'latest' ? '已是最新版本' : u.state === 'failed' ? u.message : ''}
          </span>
          {#if u.state === 'failed' && u.cdkProblem && mirror}
            <button class="btn" onclick={toGithub}>改用 GitHub</button>
          {:else}
            <button class="btn" disabled={u.state === 'checking'} onclick={() => app.checkUpdate()}>
              <Icon name="refresh" size={14} />检查更新
            </button>
          {/if}
        {/if}
      </div>
      {#if u.state === 'available' || u.state === 'downloading' || u.state === 'ready'}
        <div class="newnotes">
          {#if u.notes}
            <p class="nh">v{u.version} 更新了这些 <span>{u.notes.date}</span></p>
            <NotesList items={u.notes.items} />
          {/if}
          {#if u.notesUrl}<button class="link" onclick={() => api.openUrl(u.notesUrl!)}>{u.notes ? '在发版页查看' : '查看更新内容'}</button>{/if}
        </div>
      {/if}
      <div class="usrc">
        <span class="hkl">更新源</span>
        <div class="scales two">
          <button class:on={!mirror} onclick={() => setUpdateSource('github')}>GitHub</button>
          <button class:on={mirror} onclick={() => setUpdateSource('mirrorchyan')}>Mirror酱</button>
        </div>
      </div>
      {#if mirror}
        <div class="cdk">
          <input
            type={showCdk ? 'text' : 'password'}
            bind:value={cdkDraft}
            placeholder="Mirror酱 CDK（选填）"
            spellcheck="false"
            autocomplete="off"
            onblur={commitCdk}
            onkeydown={(e) => e.key === 'Enter' && e.currentTarget.blur()}
          />
          <button class="btn icon" title={showCdk ? '隐藏' : '显示'} aria-label={showCdk ? '隐藏 CDK' : '显示 CDK'} onclick={() => (showCdk = !showCdk)}>
            <Icon name={showCdk ? 'eyeOff' : 'eye'} size={14} />
          </button>
        </div>
        <p class="note">
          {#if expiry}
            <span class:ok={!expiry.soon} class:warn={expiry.soon}>CDK 有效至 {expiry.date}{expiry.soon ? `，还剩 ${Math.max(expiry.days, 0)} 天` : ''}</span>
          {:else if !app.settings.mirrorChyanCdk}
            国内下载更快，需要 CDK；不填也能检查更新，下载走 GitHub。
          {/if}
          <button class="link" onclick={() => api.openUrl(MIRROR)}>{expiry ? '续费 CDK' : '获取 CDK'}</button>
        </p>
      {/if}
      {#if !mirror}
        <div class="cdk">
          <input
            class:bad={tokenBad}
            type={showToken ? 'text' : 'password'}
            bind:value={tokenDraft}
            placeholder="GitHub Token（选填）"
            spellcheck="false"
            autocomplete="off"
            onblur={commitToken}
            onkeydown={(e) => e.key === 'Enter' && e.currentTarget.blur()}
          />
          <button class="btn icon" title={showToken ? '隐藏' : '显示'} aria-label={showToken ? '隐藏 Token' : '显示 Token'} onclick={() => (showToken = !showToken)}>
            <Icon name={showToken ? 'eyeOff' : 'eye'} size={14} />
          </button>
        </div>
        <p class="note" class:bad={tokenBad}>
          {#if tokenChecking}
            正在验证 Token……
          {:else if u.state === 'failed' && u.githubTokenProblem}
            Token 无效或已过期，请检查输入是否正确。
          {:else if u.state === 'failed' && u.githubRateLimited}
            {u.message}
          {:else if tokenCheck && !tokenCheck.valid}
            {tokenCheck.message}
          {:else if tokenExpiry}
            <span class:ok={!tokenExpiry.soon} class:warn={tokenExpiry.soon}>Token 有效至 {tokenExpiry.date}{tokenExpiry.soon ? `，还剩 ${Math.max(tokenExpiry.days, 0)} 天` : ''}</span>
          {:else if app.settings.githubToken}
            Token 已填。
          {:else}
            不填也能用：GitHub 按 IP 每小时只给 60 次检查额度，反复检查容易被限流；填了自己的 Token 按每小时 5000 次算。
          {/if}
          <button class="link" onclick={() => api.openUrl(TOKEN_URL)}>生成 Token</button>
        </p>
      {/if}
      <div class="usrc">
        <span class="hkl">代理</span>
        <div class="scales two">
          <button class:on={!proxyManual} onclick={() => setProxyManual(false)}>跟随系统</button>
          <button class:on={proxyManual} onclick={() => setProxyManual(true)}>手动</button>
        </div>
      </div>
      {#if proxyManual}
        <div class="cdk">
          <input
            class:bad={proxyBad}
            bind:value={proxyDraft}
            placeholder="http://127.0.0.1:7890"
            spellcheck="false"
            autocomplete="off"
            onblur={commitProxy}
            onkeydown={(e) => e.key === 'Enter' && e.currentTarget.blur()}
          />
        </div>
        <p class="note" class:bad={proxyBad}>
          {proxyBad ? '格式是 http://地址:端口 或 socks5://地址:端口' : '检查和下载更新走这个代理，支持 http 和 socks5。'}
        </p>
      {/if}
      <button class="logbtn" onclick={() => (app.changelogOpen = true)}>
        <span><b>更新日志</b><small>每个版本更新了什么</small></span>
        <Icon name="arrowRight" size={16} />
      </button>
      <label class="row">
        <span class="lbl"><b>自动检查更新</b><small>启动时和开着时每两小时查一次，从{mirror ? ' Mirror酱 ' : ' GitHub '}获取</small></span>
        <input type="checkbox" class="sw" checked={app.settings.autoCheckUpdate} onchange={(e) => set('autoCheckUpdate', e.currentTarget.checked)} />
      </label>
      {#if app.settings.autoCheckUpdate}
        <label class="row">
          <span class="lbl"><b>发现新版自动下载</b><small>关掉后只在顶上提示有新版，点了才开始下载</small></span>
          <input type="checkbox" class="sw" checked={app.settings.autoDownloadUpdate} onchange={(e) => set('autoDownloadUpdate', e.currentTarget.checked)} />
        </label>
      {/if}
    </section>

    <section>
      <h4>数据 <small>阵容、特别关注和偏好设置；换电脑、重装时用备份迁移</small></h4>
      <p class="path">
        <span>保存在 <code>{app.appInfo.dataDir}</code>，同一台电脑换新包不用导入。</span>
        <button class="link" onclick={() => api.openDataFolder()}><Icon name="folder" size={13} />打开</button>
      </p>
      {#if picked}
        {@const s = picked.summary}
        <div class="imp">
          <p>
            备份来自 v{s.appVersion} · {new Date(s.exportedAt).toLocaleDateString()}：<b>{s.lineups}</b> 套阵容，关注
            <b>{s.focusCovenants}</b> 个盟约、<b>{s.focusOperators}</b> 名干员
          </p>
          <div class="modes">
            {#each modes as m (m.id)}
              <button class:on={mode === m.id} onclick={() => (mode = m.id)}>
                <span class="radio"></span>
                <span class="mt"><b>{m.name}</b><small>{m.note}</small></span>
              </button>
            {/each}
          </div>
          <div class="impact">
            <small>导入前会把当前数据备份到数据文件夹的 backups 里</small>
            <button class="btn" disabled={busy} onclick={() => (picked = null)}>取消</button>
            <button class="btn main" disabled={busy} onclick={doImport}>{busy ? '导入中…' : '导入'}</button>
          </div>
        </div>
      {:else}
        <div class="dataops">
          <button class="btn" disabled={busy} onclick={doExport}><Icon name="download" size={14} />导出备份</button>
          <button class="btn" disabled={busy} onclick={doPick}><Icon name="upload" size={14} />导入备份</button>
        </div>
      {/if}
      {#if dataMsg}<p class="dmsg" class:bad={dataMsg.bad}>{dataMsg.text}</p>{/if}
    </section>

    <section>
      <h4>反馈问题 <small>识别不了、没反应时用</small></h4>
      <div class="dataops">
        <button class="btn" disabled={diagBusy} onclick={exportDiag}><Icon name="download" size={14} />{diagBusy ? '正在打包…' : '导出诊断包'}</button>
      </div>
      <p class="note">把日志、最近几次识别失败的截图和原因打成一个 zip，发到 QQ 群或 GitHub Issues。截图是识别失败时的游戏画面，联机时可能带着队友昵称；不含账号、登录凭据和 CDK。</p>
      {#if diagMsg}<p class="dmsg" class:bad={diagMsg.bad}>{diagMsg.text}</p>{/if}
    </section>

    <section>
      <label class="row">
        <span class="lbl"><b>帮助改进识别</b><small>打开后，打对局时助手会存下认人用的游戏截图和识别记录，最近 3 局，每局约 40 MB，只存在这台电脑上。打完一局在下面导出，发到 QQ 群，我们拿来提高认人的准确率</small></span>
        <input type="checkbox" class="sw" checked={app.settings.collect} onchange={(e) => set('collect', e.currentTarget.checked)} />
      </label>
      {#if app.settings.collect}
        <p class="note">认错的格子在本局页「场上」点开改对，再点「都对了」，这一刻整盘的名字就成了答案，比改几格更有用。截图是游戏画面，联机时可能带着队友昵称；不含账号和登录信息。</p>
      {/if}
      {#if colls.length}
        <ul class="colls">
          {#each colls as c (c.id)}
            <li>
              <span class="cw">
                <b>{collWhen(c.startedAt)}{#if c.ongoing}<em>正在打</em>{/if}</b>
                <small>{c.mode ?? '模式没读到'} · 第 {c.rounds} 回合 · {c.frames} 张图 · {c.answers} 格答案 · {mb(c.bytes)}</small>
              </span>
              <button class="btn" disabled={!!collBusy} onclick={() => exportColl(c.id)}><Icon name="download" size={14} />{collBusy === c.id ? '正在打包…' : '导出'}</button>
            </li>
          {/each}
        </ul>
        <div class="dataops">
          <button class="btn" class:danger={clearAsk} disabled={!!collBusy} onclick={clearColls} onblur={() => (clearAsk = false)}><Icon name="trash" size={14} />{clearAsk ? '再点一次，全部删掉' : '清空已存的'}</button>
        </div>
      {/if}
      {#if collMsg}<p class="dmsg" class:bad={collMsg.bad}>{collMsg.text}</p>{/if}
    </section>

    <section class="about">
      <button class="link" onclick={() => api.openUrl(REPO)}>GitHub 开源地址</button>
      <button class="link" onclick={() => api.openUrl(QQ)}>QQ 交流群 1131280375</button>
      <p>非官方玩家自制工具，与鹰角网络无关。游戏美术与数据归上海鹰角网络所有。</p>
    </section>
    {/if}
    </div>
  </aside>
{/if}

<style>
  .acct {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 4px 12px;
    margin: 0;
    font-size: var(--fs-base);
    color: var(--fg-3);
  }
  .acct span {
    flex: 1 1 100%;
  }
  .acct b {
    color: var(--fg-1);
  }
  .acct em {
    margin-left: 8px;
    font-style: normal;
    color: var(--fg-4);
  }
  .scrim {
    position: absolute;
    inset: 0;
    background: var(--scrim);
    z-index: 10;
  }
  .sheet {
    position: absolute;
    left: 0;
    right: 0;
    bottom: 0;
    /* 固定高度：切页时面板不跳 */
    height: 86%;
    display: flex;
    flex-direction: column;
    overflow: hidden;
    z-index: 11;
    background:
      var(--noise),
      linear-gradient(180deg, var(--sheet-top), var(--sheet-bottom) 120px);
    box-shadow:
      0 -1px 0 var(--line),
      0 -12px 30px rgb(var(--shade-rgb) / 0.6);
  }
  .sheet::before {
    content: '';
    position: absolute;
    top: 0;
    left: 12px;
    width: 64px;
    height: 2px;
    background: var(--mint);
    box-shadow: var(--mint-glow);
  }
  header {
    display: flex;
    align-items: baseline;
    gap: 8px;
    margin-bottom: 6px;
  }
  h3 {
    margin: 0;
    font-size: var(--fs-2xl);
  }
  header .term {
    font-size: var(--fs-2xs);
    color: var(--fg-4);
  }
  .x {
    margin-left: auto;
    width: 26px;
    height: 26px;
    display: grid;
    place-items: center;
    color: var(--fg-3);
    align-self: center;
  }
  .x:hover {
    color: var(--fg-1);
    background: var(--bg-4);
  }
  section {
    padding: 10px 0;
    border-top: 1px solid var(--line-soft);
  }
  .brand {
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 6px 0 12px;
  }
  .brand img {
    flex: none;
  }
  .brand div {
    display: flex;
    flex-direction: column;
    gap: 2px;
    min-width: 0;
  }
  .brand b {
    font-size: var(--fs-xl);
    letter-spacing: 0.04em;
  }
  .brand .term {
    font-size: var(--fs-xs);
    color: var(--mint);
    letter-spacing: 0.12em;
  }
  .brand small {
    font-size: var(--fs-sm);
    color: var(--fg-3);
  }
  .scroll > section:first-child {
    border-top: none;
  }
  .tabs {
    flex: none;
    margin-bottom: 4px;
  }
  .keysnote {
    margin-top: 0;
  }
  h4 {
    margin: 0 0 8px;
    font-size: var(--fs-md);
    display: flex;
    flex-wrap: wrap;
    align-items: baseline;
    gap: 2px 8px;
    white-space: nowrap;
  }
  h4 small {
    white-space: normal;
  }
  h4 small {
    font-size: var(--fs-sm);
    font-weight: var(--fw-base);
    color: var(--fg-4);
  }
  .themes {
    display: flex;
    flex-direction: column;
    gap: 2px;
  }
  .themes button {
    display: flex;
    align-items: center;
    gap: 10px;
    height: 34px;
    padding: 0 10px;
    text-align: left;
    background: var(--bg-1);
    color: var(--fg-2);
  }
  .themes button:hover {
    background: var(--bg-2);
  }
  .themes button.on {
    color: var(--fg-1);
    background: linear-gradient(90deg, var(--mint-tint), transparent 70%), var(--bg-2);
    box-shadow: inset var(--edge) 0 0 var(--mint);
  }
  .themes b {
    font-size: var(--fs-md);
  }
  .themes em {
    font-style: normal;
    font-size: var(--fs-sm);
    color: var(--fg-4);
  }
  .scales {
    display: grid;
    grid-template-columns: repeat(6, 1fr);
    gap: 2px;
  }
  /* 通用风格主题的配色：主题列表下面一行 */
  .scheme {
    display: flex;
    align-items: center;
    gap: 10px;
    margin-top: 8px;
    font-size: var(--fs-base);
    color: var(--fg-2);
  }
  .accents {
    flex: 1;
    display: flex;
    flex-wrap: wrap;
    gap: 6px;
  }
  /* 色块：圆点；选中的外面一圈 */
  .acc {
    position: relative;
    width: 20px;
    height: 20px;
    border-radius: 50%;
    background: var(--sw);
    box-shadow: inset 0 0 0 1px rgb(var(--shade-rgb) / 0.15);
    cursor: pointer;
  }
  .acc.on {
    box-shadow:
      0 0 0 2px var(--bg-1),
      0 0 0 3.5px var(--fg-2);
  }
  /* 主题默认：一半主题蓝一半主题紫 */
  .acc.def {
    background: conic-gradient(#3b82f6 0 50%, #8b5cf6 0);
  }
  /* 自选：没选时是彩虹圈 */
  .acc.pick {
    background: var(--sw, conic-gradient(#ef4444, #eab308, #22c55e, #14b8a6, #3b82f6, #8b5cf6, #ec4899, #ef4444));
    overflow: hidden;
  }
  .acc.pick input {
    position: absolute;
    inset: 0;
    opacity: 0;
    cursor: pointer;
  }
  .scheme .scales {
    flex: 1;
    grid-template-columns: repeat(3, 1fr);
  }
  .scales.grow {
    flex: 1;
  }
  .scales.four {
    grid-template-columns: repeat(4, 1fr);
  }
  .scales.two {
    grid-template-columns: repeat(2, 1fr);
  }
  .scales.three {
    grid-template-columns: repeat(3, 1fr);
  }
  .inst {
    margin-top: 8px;
  }
  .note {
    margin: 6px 0 0;
    font-size: var(--fs-sm);
    color: var(--fg-4);
  }
  .scales button {
    height: 30px;
    font-size: var(--fs-base);
    color: var(--fg-2);
    background: var(--bg-1);
  }
  .scales button:hover {
    background: var(--bg-2);
    color: var(--fg-1);
  }
  .scales button.on {
    color: var(--mint-ink);
    font-weight: var(--fw-bold);
    background: var(--mint);
  }
  /* 用不了的选项（如收不到单键时的「按住才显示」）：字压暗、划掉 */
  .scales button:disabled {
    color: var(--fg-4);
    background: var(--bg-1);
    text-decoration: line-through;
    cursor: not-allowed;
  }
  .radio {
    width: 12px;
    height: 12px;
    border-radius: 50%;
    box-shadow: inset 0 0 0 1.5px var(--fg-4);
  }
  .on .radio {
    background: var(--mint);
    box-shadow:
      inset 0 0 0 2.5px var(--bg-2),
      0 0 0 1.5px var(--mint),
      var(--mint-glow);
  }
  .row {
    display: flex;
    align-items: center;
    gap: 10px;
    cursor: pointer;
  }
  .row {
    margin-top: 10px;
  }
  .btn.icon {
    padding: 0 8px;
  }
  .newnotes {
    display: flex;
    flex-direction: column;
    gap: 6px;
    margin-top: 8px;
    align-items: flex-start;
  }
  .newnotes :global(.notes) {
    align-self: stretch;
  }
  .nh {
    margin: 0;
    font-size: var(--fs-base);
    font-weight: var(--fw-bold);
    color: var(--fg-2);
  }
  .nh span {
    font-weight: var(--fw-base);
    color: var(--fg-4);
    margin-left: 4px;
  }
  .logbtn {
    width: 100%;
    display: flex;
    align-items: center;
    gap: 8px;
    margin-top: 10px;
    padding: 8px 10px;
    text-align: left;
    color: var(--fg-3);
    background: var(--bg-1);
  }
  .logbtn:hover {
    color: var(--fg-1);
    background: var(--bg-2);
  }
  .logbtn span {
    flex: 1;
    display: flex;
    flex-direction: column;
  }
  .logbtn b {
    font-size: var(--fs-md);
    color: var(--fg-1);
  }
  .logbtn small {
    font-size: var(--fs-sm);
    color: var(--fg-4);
  }
  .lbl {
    flex: 1;
    display: flex;
    flex-direction: column;
  }
  .lbl b {
    font-size: var(--fs-md);
  }
  .lbl small {
    font-size: var(--fs-sm);
    color: var(--fg-4);
  }
  /* 开关 */
  .sw {
    appearance: none;
    flex: none;
    width: 34px;
    height: 18px;
    margin: 0;
    border-radius: var(--r-pill);
    background: var(--bg-4);
    position: relative;
    cursor: pointer;
    transition: background var(--t-fast);
  }
  .sw::after {
    content: '';
    position: absolute;
    left: 2px;
    top: 2px;
    width: 14px;
    height: 14px;
    border-radius: 50%;
    background: var(--fg-2);
    transition: transform var(--t-mid) var(--ease-out);
  }
  .sw:checked {
    background: var(--mint);
  }
  .sw:checked::after {
    background: var(--mint-ink);
    transform: translateX(16px);
  }
  .upd {
    display: flex;
    align-items: center;
    gap: 8px;
    min-height: 30px;
  }
  .msg {
    flex: 1;
    font-size: var(--fs-base);
    color: var(--fg-3);
  }
  .msg.new {
    color: var(--mint);
  }
  .msg.bad {
    color: var(--red-fg);
  }
  .usrc {
    display: flex;
    align-items: center;
    gap: 10px;
    margin-top: 10px;
  }
  .usrc .scales {
    flex: 1;
  }
  .profs {
    display: flex;
    flex-direction: column;
    gap: 4px;
    margin-top: 8px;
  }
  .pf {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 5px 8px;
    box-shadow: inset 0 0 0 1px var(--line-soft);
  }
  .pf b {
    flex: 1;
    font-size: var(--fs-md);
  }
  .pf em {
    font-style: normal;
    font-size: var(--fs-sm);
    color: var(--mint);
  }
  .pf.cur {
    box-shadow: inset 0 0 0 1px var(--mint-lo);
  }
  .cdk {
    display: flex;
    gap: 6px;
    margin-top: 8px;
  }
  .mks {
    margin-top: 10px;
    padding-top: 8px;
    border-top: 1px dashed var(--line-soft);
  }
  .mk {
    display: flex;
    align-items: center;
    gap: 10px;
    margin-top: 8px;
  }
  .mk .lbl {
    flex: 1;
  }
  .mk.off .mprev {
    opacity: 0.3;
  }
  /* 缩小的商店标记：深色卡身 + 外框（黑缝、主色框、发光）+ 左上和右下的提亮括号 + 框顶左对齐的标签 */
  .mprev {
    position: relative;
    flex: none;
    width: 34px;
    height: 44px;
    margin-top: 8px;
    background: linear-gradient(180deg, #3a3f48, #1c1f25);
    outline: 2px solid var(--mc);
    outline-offset: 2px;
    box-shadow:
      0 0 0 2px #000,
      0 0 0 6px rgb(0 0 0 / 0.7),
      0 0 10px 2px color-mix(in srgb, var(--mc) 55%, transparent);
  }
  .mprev:not(.dash)::before,
  .mprev:not(.dash)::after {
    content: '';
    position: absolute;
    width: 8px;
    height: 8px;
    border: 0 solid color-mix(in srgb, var(--mc) 65%, white);
  }
  .mprev::before {
    left: -4px;
    top: -4px;
    border-left-width: 2px;
    border-top-width: 2px;
  }
  .mprev::after {
    right: -4px;
    bottom: -4px;
    border-right-width: 2px;
    border-bottom-width: 2px;
  }
  .mprev.dash {
    outline: 1.5px dashed var(--mc);
    box-shadow:
      0 0 0 2px #000,
      0 0 0 5px rgb(0 0 0 / 0.65);
  }
  .mtag {
    position: absolute;
    left: -5px;
    top: -14px;
    padding: 0 3px;
    font-style: normal;
    font-size: 8px;
    font-weight: var(--fw-bold);
    line-height: 11px;
    white-space: nowrap;
    color: color-mix(in srgb, var(--mc) 12%, black);
    background: var(--mc);
    box-shadow: 0 0 0 1px rgb(0 0 0 / 0.75);
  }
  .dash .mtag {
    color: var(--mc);
    background: #061410;
    box-shadow:
      inset 0 0 0 1px var(--mc),
      0 0 0 1px rgb(0 0 0 / 0.75);
  }
  .swatches {
    display: flex;
    flex-wrap: wrap;
    gap: 5px;
    margin: 6px 0 2px 44px;
  }
  .swatches button,
  .custom {
    position: relative;
    width: 20px;
    height: 20px;
    background: var(--c);
    box-shadow: inset 0 0 0 1px rgb(0 0 0 / 0.35);
  }
  .swatches .on {
    box-shadow:
      0 0 0 2px var(--bg-0),
      0 0 0 3px var(--fg-1);
  }
  .custom {
    display: grid;
    place-items: center;
    cursor: pointer;
    color: color-mix(in srgb, var(--c) 15%, black);
  }
  .custom input {
    position: absolute;
    inset: 0;
    opacity: 0;
    cursor: pointer;
  }
  .fpick {
    width: 100%;
    display: flex;
    align-items: center;
    justify-content: space-between;
    height: 32px;
    padding: 0 10px;
    font-size: var(--fs-md);
    color: var(--fg-1);
    background: var(--bg-1);
  }
  .fpick:hover {
    background: var(--bg-2);
  }
  .fpick :global(svg) {
    color: var(--fg-3);
    transition: transform var(--t-fast);
  }
  .fpick.open :global(svg) {
    transform: rotate(180deg);
  }
  .flist {
    margin-top: 2px;
    padding: 6px;
    background: var(--bg-1);
  }
  .fscroll {
    display: flex;
    flex-direction: column;
    max-height: 240px;
    margin-top: 6px;
    overflow-y: auto;
  }
  .fscroll button {
    flex: none;
    padding: 6px 8px;
    text-align: left;
    font-size: var(--fs-md);
    color: var(--fg-2);
  }
  .fscroll button:hover {
    color: var(--fg-1);
    background: var(--bg-2);
  }
  .fscroll button.on {
    color: var(--mint);
    box-shadow: inset var(--edge) 0 0 var(--mint);
  }
  .cdk input {
    flex: 1;
    min-width: 0;
    height: 28px;
    padding: 0 8px;
    font-family: var(--font-term);
    font-size: var(--fs-base);
    color: var(--fg-1);
    background: var(--bg-1);
    border: 0;
    outline: 0;
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .cdk input:focus {
    box-shadow: inset 0 0 0 1px var(--mint-lo);
  }
  .cdk input.bad {
    box-shadow: inset 0 0 0 1px var(--gold);
  }
  .cdk input::placeholder {
    font-family: var(--font-ui);
    color: var(--fg-4);
  }
  .note .ok {
    color: var(--mint);
  }
  .note .warn {
    color: var(--gold);
  }
  .bar {
    flex: 1;
    height: 4px;
    background: var(--bg-4);
  }
  .bar i {
    display: block;
    height: 100%;
    background: var(--mint);
    box-shadow: var(--mint-glow);
    transition: width var(--t-mid);
  }
  .btn {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    height: 28px;
    padding: 0 12px;
    font-size: var(--fs-base);
    font-weight: var(--fw-bold);
    color: var(--fg-1);
    background: var(--bg-4);
  }
  .btn:hover {
    background: var(--bg-5);
  }
  .btn.main {
    color: var(--mint-ink);
    background: var(--mint);
  }
  .btn:disabled {
    color: var(--fg-4);
    cursor: default;
  }
  .link {
    font-size: var(--fs-base);
    color: var(--mint);
    text-decoration: underline;
    text-underline-offset: 2px;
  }
  .path {
    display: flex;
    align-items: flex-start;
    gap: 8px;
    margin: 0 0 8px;
    font-size: var(--fs-sm);
    line-height: 1.5;
    color: var(--fg-3);
  }
  .path code {
    font-family: var(--font-term);
    color: var(--fg-2);
    word-break: break-all;
  }
  .path .link {
    flex: none;
    display: inline-flex;
    align-items: center;
    gap: 3px;
  }
  .dataops {
    display: flex;
    gap: 6px;
  }
  .imp {
    display: flex;
    flex-direction: column;
    gap: 8px;
    padding: 8px;
    background: var(--bg-1);
    box-shadow: inset var(--edge) 0 0 var(--mint);
  }
  .imp p {
    margin: 0;
    font-size: var(--fs-base);
    color: var(--fg-2);
  }
  .imp p b {
    color: var(--mint);
  }
  .modes {
    display: flex;
    flex-direction: column;
    gap: 2px;
  }
  .modes button {
    display: flex;
    align-items: flex-start;
    gap: 10px;
    padding: 6px 8px;
    text-align: left;
    color: var(--fg-2);
    background: var(--bg-2);
  }
  .modes button.on {
    color: var(--fg-1);
    box-shadow: inset var(--edge) 0 0 var(--mint);
  }
  .modes .radio {
    flex: none;
    margin-top: 3px;
  }
  .mt {
    display: flex;
    flex-direction: column;
    gap: 1px;
  }
  .mt b {
    font-size: var(--fs-md);
  }
  .mt small {
    font-size: var(--fs-sm);
    color: var(--fg-4);
  }
  .impact {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    gap: 6px;
  }
  .impact small {
    flex: 1;
    min-width: 10em;
    font-size: var(--fs-sm);
    color: var(--fg-4);
  }
  /* 帮助改进识别：已存的对局，一行一局，右边导出 */
  .colls {
    list-style: none;
    margin: 8px 0;
    padding: 0;
    display: flex;
    flex-direction: column;
    gap: 2px;
  }
  .colls li {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 6px 8px;
    background: var(--bg-2);
  }
  .cw {
    flex: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 1px;
  }
  .cw b {
    font-size: var(--fs-md);
    font-weight: 500;
    color: var(--fg-1);
  }
  .cw em {
    margin-left: 6px;
    font-style: normal;
    font-size: var(--fs-xs);
    color: var(--mint);
  }
  .cw small {
    font-size: var(--fs-sm);
    color: var(--fg-4);
  }
  .btn.danger {
    color: var(--red-fg);
  }
  .dmsg {
    margin: 8px 0 0;
    font-size: var(--fs-base);
    color: var(--mint);
    word-break: break-all;
  }
  .dmsg.bad {
    color: var(--red-fg);
  }
  .about {
    display: flex;
    flex-wrap: wrap;
    gap: 6px 16px;
  }
  .about p {
    width: 100%;
    margin: 4px 0 0;
    font-size: var(--fs-sm);
    color: var(--fg-4);
  }
  .hk {
    display: flex;
    align-items: center;
    gap: 10px;
    margin-top: 10px;
  }
  /* 标题列定宽，同一块里的控件左边对齐（v1.2.6，群友反馈「快捷键」那行按钮缩进去了） */
  .hkl {
    font-size: var(--fs-md);
    flex: none;
    min-width: 4em;
  }
  .keyrows .hkl {
    min-width: 8.5em;
  }
  .rng {
    flex: 1;
    min-width: 0;
    accent-color: var(--mint);
  }
  .rngv {
    flex: none;
    min-width: 44px;
    text-align: right;
    font-size: var(--fs-base);
    color: var(--fg-2);
    font-variant-numeric: tabular-nums;
  }
  .hkb {
    min-width: 92px;
    height: 28px;
    padding: 0 10px;
    font-family: var(--font-term);
    font-size: var(--fs-base);
    color: var(--fg-1);
    background: var(--bg-3);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .hkb:hover {
    box-shadow: inset 0 0 0 1px var(--fg-4);
  }
  .hkb.rec {
    color: var(--mint);
    box-shadow: inset 0 0 0 1px var(--mint);
  }
  .note.bad {
    color: var(--gold);
  }
  /* 自动操作页 */
  .note code {
    font-family: var(--font-term);
    font-size: var(--fs-xs);
    padding: 0 4px;
    color: var(--fg-1);
    background: var(--bg-2);
    user-select: all;
  }
  .note.ctl b {
    color: var(--fg-1);
  }
</style>
