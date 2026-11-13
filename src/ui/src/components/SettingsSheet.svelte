<script lang="ts">
  import { sheet } from '../lib/sheet'
  import { fade } from 'svelte/transition'
  import { app } from '../lib/state.svelte'
  import { api, type BackupSummary, type GameTheme, type ImportMode, type MumuInstance, type Settings, type UiSkin } from '../lib/api'
  import Icon from './Icon.svelte'
  import NotesList from './NotesList.svelte'
  import { plaza, PLAZA } from '../lib/plaza.svelte'
  import { cloud } from '../lib/sync.svelte'

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
  const QQ = 'https://qm.qq.com/q/AuRBCSxWEg'
  const close = () => (app.settingsOpen = false)
  const set = <K extends keyof typeof app.settings>(k: K, v: (typeof app.settings)[K]) => {
    app.settings[k] = v
    app.saveSettings()
  }
  // 敌人路线（v0.9.0）：一组设置整体存
  const rt = $derived(app.settings.routes)
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
  const expiry = $derived.by(() => {
    if (!u.cdkExpiresAt) return null
    const d = new Date(u.cdkExpiresAt)
    const days = Math.ceil((d.getTime() - Date.now()) / 86400_000)
    return { date: d.toLocaleDateString('zh-CN', { year: 'numeric', month: '2-digit', day: '2-digit' }).replaceAll('/', '-'), soon: days <= 7, days }
  })

  // 游戏来源：多开 MuMu 时才列出实例让用户选
  const sources: { v: Settings['gameSource']; label: string; note: string }[] = [
    { v: 'auto', label: '自动', note: '先找 PC 客户端，没开再找 MuMu 模拟器。' },
    { v: 'pc', label: 'PC 客户端', note: '只连明日方舟 PC 版。' },
    { v: 'mumu', label: 'MuMu 模拟器', note: 'MuMu 画面要横屏 16:9（如 1920×1080）。窗口被挡住也能识别。' },
  ]
  let instances = $state<MumuInstance[]>([])
  const loadInstances = async () => (instances = app.settings.gameSource === 'pc' ? [] : await api.mumuInstances())
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
    loadInstances()
  }

  // 按键录制：修饰键按 Ctrl、Alt、Shift、Win 的顺序，再接主键（写法见 Settings.toggleHotkey）。
  // v0.7.2 起允许单键：单键只在游戏或助手在前台时生效、不拦截按键（宿主实现）
  let recording = $state<'toggle' | 'refresh' | 'route' | null>(null)
  let hkMsg = $state('')
  const keyName = (e: KeyboardEvent) =>
    e.key === ' ' ? 'Space' : /^Key[A-Z]$/.test(e.code) ? e.code.slice(3) : /^Digit\d$/.test(e.code) ? e.code.slice(5) : e.key.length === 1 ? e.key.toUpperCase() : e.key
  function recordKey(e: KeyboardEvent) {
    if (!recording) return
    e.preventDefault()
    e.stopPropagation()
    if (e.key === 'Escape') return void (recording = null)
    // 路线快捷键是单个键（按住显示），Alt、Ctrl、Shift 单独按也算
    if (recording === 'route') {
      recording = null
      return setRoutes({ hotkey: e.key === 'Control' ? 'Ctrl' : e.key === 'Meta' ? 'Win' : keyName(e) })
    }
    if (['Control', 'Alt', 'Shift', 'Meta'].includes(e.key)) return
    const mods = [e.ctrlKey && 'Ctrl', e.altKey && 'Alt', e.shiftKey && 'Shift', e.metaKey && 'Win'].filter(Boolean)
    const combo = [...mods, keyName(e)].join('+')
    const target = recording
    recording = null
    if (target === 'toggle') applyHotkey(combo)
    else set('refreshKey', combo)
  }
  const single = (combo: string) => !!combo && !/(Ctrl|Alt|Shift|Win)\+/.test(combo)
  async function applyHotkey(combo: string) {
    try {
      await api.setHotkey(combo)
      app.settings.toggleHotkey = combo
      app.appInfo.hotkeyFailed = false
      hkMsg = ''
    } catch (e) {
      hkMsg = (e as Error).message
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

<svelte:window onkeydown={(e) => e.key === 'Escape' && close()} />

{#if app.settingsOpen}
  <div class="scrim" transition:fade={{ duration: 160 }} onclick={close} role="presentation"></div>
  <aside class="sheet win" transition:sheet={{ y: 360, duration: 240 }}>
    <header>
      <h3>设置</h3>
      <span class="term">Settings</span>
      <button class="x" onclick={close} aria-label="关闭"><Icon name="close" size={16} /></button>
    </header>
    <div class="scroll">

    <section>
      <h4>游戏来源 <small>PC 客户端或 MuMu 模拟器</small></h4>
      <div class="scales three">
        {#each sources as s (s.v)}
          <button class:on={app.settings.gameSource === s.v} onclick={() => setSource(s.v)}>{s.label}</button>
        {/each}
      </div>
      <p class="note">{sources.find((s) => s.v === app.settings.gameSource)!.note}</p>
      {#if app.settings.gameSource !== 'pc' && instances.length > 1}
        <div class="themes inst">
          <button class:on={!app.settings.mumuSerial} onclick={() => set('mumuSerial', '')}>
            <span class="radio"></span><b>自动</b><em>只开一个实例时用它</em>
          </button>
          {#each instances as i (i.serial)}
            <button class:on={app.settings.mumuSerial === i.serial} onclick={() => set('mumuSerial', i.serial)}>
              <span class="radio"></span><b>{i.name}</b><em>{i.serial}</em>
            </button>
          {/each}
        </div>
      {/if}
    </section>

    <section>
      <h4>游戏主题 <small>选游戏里正在用的主题，商店识别要靠它</small></h4>
      <div class="themes">
        {#each themes as t (t.id)}
          <button class:on={app.settings.gameTheme === t.id} onclick={() => set('gameTheme', t.id)}>
            <span class="radio"></span>
            <b>{t.name}</b>
            {#if t.note}<em>{t.note}</em>{/if}
          </button>
        {/each}
      </div>
    </section>

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
          <button class:on={rt.display === 'hold'} onclick={() => setRoutes({ display: 'hold' })}>按住才显示</button>
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
        <button class="hkb" class:rec={recording === 'route'} onclick={() => ((recording = recording === 'route' ? null : 'route'), (hkMsg = ''))} onkeydown={recordKey}>
          {recording === 'route' ? '按下想用的键…' : rt.hotkey}
        </button>
      </div>
      <p class="note">
        {rt.display === 'always'
          ? `休整期一直显示，进入战斗、离开棋盘时自动隐藏；按 ${rt.hotkey} 临时隐藏，再按一次显示。`
          : rt.display === 'hold'
          ? `平时不显示，按住 ${rt.hotkey} 时显示，松开就隐藏。`
          : `每个新回合进休整期时显示 ${rt.peekSeconds} 秒再淡出，之后按住 ${rt.hotkey} 还能再看。`}
        按键照样会传给游戏，别选游戏里有用的键。
      </p>
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
      <!-- 全局快捷键：游戏在前台也能按；点按钮后按下新组合 -->
      <div class="hk">
        <span class="hkl">快捷键收起 / 展开</span>
        <button class="hkb" class:rec={recording === 'toggle'} onclick={() => ((recording = recording === 'toggle' ? null : 'toggle'), (hkMsg = ''))} onkeydown={recordKey}>
          {recording === 'toggle' ? '按下新的按键…' : app.settings.toggleHotkey || '未设置'}
        </button>
        {#if app.settings.toggleHotkey && recording !== 'toggle'}<button class="link" onclick={() => applyHotkey('')}>关闭</button>{/if}
      </div>
      {#if hkMsg}
        <p class="note bad">{hkMsg}</p>
      {:else if recording === 'toggle'}
        <p class="note">单按一个键（如 Tab、`）或 Ctrl / Alt / Shift 加一个键；Esc 取消。</p>
      {:else if app.appInfo.hotkeyFailed && app.settings.toggleHotkey}
        <p class="note bad">「{app.settings.toggleHotkey}」被别的软件占用了，没生效，换一个试试。</p>
      {:else if single(app.settings.toggleHotkey)}
        <p class="note">单键只在游戏或助手在前台时生效，按键照样会传给游戏，别选游戏里有用的键。</p>
      {:else}
        <p class="note">游戏在前台时也能按：侧栏里收起、展开；工作台里最小化、恢复。</p>
      {/if}
      <!-- 商店刷新键：游戏里刷新商店时先把旧标记藏起来 -->
      <div class="hk">
        <span class="hkl">商店刷新键</span>
        <button class="hkb" class:rec={recording === 'refresh'} onclick={() => ((recording = recording === 'refresh' ? null : 'refresh'), (hkMsg = ''))} onkeydown={recordKey}>
          {recording === 'refresh' ? '按下游戏里的刷新键…' : app.settings.refreshKey || '未设置'}
        </button>
        {#if app.settings.refreshKey && recording !== 'refresh'}<button class="link" onclick={() => set('refreshKey', '')}>关闭</button>{/if}
      </div>
      <p class="note">游戏里按这个键刷新商店时，旧的商店标记马上消失，认到新的商店再标；游戏默认是 D。</p>
    </section>

    <section>
      <h4>启动时打开 <small>侧栏贴着游戏，工作台是宽窗口</small></h4>
      <div class="scales three">
        <button class:on={app.settings.startupMode === 'remember'} onclick={() => set('startupMode', 'remember')}>记住上次</button>
        <button class:on={app.settings.startupMode === 'sidebar'} onclick={() => set('startupMode', 'sidebar')}>侧栏</button>
        <button class:on={app.settings.startupMode === 'workbench'} onclick={() => set('startupMode', 'workbench')}>工作台</button>
      </div>
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
        {:else if u.state === 'ready'}
          <span class="msg new">下载完成，即将重启完成更新</span>
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
      {#if u.state === 'available'}
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
        <span class="lbl"><b>自动检查更新</b><small>启动时检查一次，从{mirror ? ' Mirror酱 ' : ' GitHub '}获取</small></span>
        <input type="checkbox" class="sw" checked={app.settings.autoCheckUpdate} onchange={(e) => set('autoCheckUpdate', e.currentTarget.checked)} />
      </label>
    </section>

    <section class="about">
      <button class="link" onclick={() => api.openUrl(REPO)}>GitHub 开源地址</button>
      <button class="link" onclick={() => api.openUrl(QQ)}>QQ 交流群 1132134217</button>
      <p>非官方玩家自制工具，与鹰角网络无关。游戏美术与数据归上海鹰角网络所有。</p>
    </section>
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
    max-height: 86%;
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
    font-weight: 400;
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
    box-shadow: inset 2px 0 0 var(--mint);
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
    font-weight: 700;
    background: var(--mint);
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
    font-weight: 700;
    color: var(--fg-2);
  }
  .nh span {
    font-weight: 400;
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
  .cdk {
    display: flex;
    gap: 6px;
    margin-top: 8px;
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
    font-weight: 700;
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
    box-shadow: inset 2px 0 0 var(--mint);
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
    box-shadow: inset 2px 0 0 var(--mint);
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
  .hkl {
    font-size: var(--fs-md);
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
</style>
