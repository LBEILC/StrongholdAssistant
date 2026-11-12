<script lang="ts">
  import { fly, fade } from 'svelte/transition'
  import { cubicOut } from 'svelte/easing'
  import { app } from '../lib/state.svelte'
  import { api, type BackupSummary, type GameTheme, type ImportMode, type MumuInstance, type Settings } from '../lib/api'
  import Icon from './Icon.svelte'
  import NotesList from './NotesList.svelte'
  import { plaza, PLAZA } from '../lib/plaza.svelte'

  // 设置：游戏主题（商店识别要用）、商店标记、版本与更新、关于
  const themes: { id: GameTheme; name: string; note: string }[] = [
    { id: 'terminal', name: '卫戍协议终端', note: '默认主题' },
    { id: 'ursus', name: '乌萨斯齐心团', note: '' },
    { id: 'closure', name: '「全能的」可露希尔', note: '' },
  ]
  const scales: { v: 'auto' | number; label: string }[] = [
    { v: 'auto', label: '自动' },
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
  const u = $derived(app.update)

  // 游戏来源：多开 MuMu 时才列出实例让用户选
  const sources: { v: Settings['gameSource']; label: string; note: string }[] = [
    { v: 'auto', label: '自动', note: '先找 PC 客户端，没开再找 MuMu 模拟器。' },
    { v: 'pc', label: 'PC 客户端', note: '只连明日方舟 PC 版。' },
    { v: 'mumu', label: 'MuMu 模拟器', note: 'MuMu 画面要横屏 16:9（如 1920×1080）。窗口被挡住也能识别。' },
  ]
  let instances = $state<MumuInstance[]>([])
  const loadInstances = async () => (instances = app.settings.gameSource === 'pc' ? [] : await api.mumuInstances())
  $effect(() => {
    if (app.settingsOpen) {
      loadInstances()
      plaza.check()
    }
  })
  const setSource = (v: Settings['gameSource']) => {
    set('gameSource', v)
    loadInstances()
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
  <aside class="sheet" transition:fly={{ y: 360, duration: 240, easing: cubicOut }}>
    <header>
      <h3>设置</h3>
      <span class="term">Settings</span>
      <button class="x" onclick={close} aria-label="关闭"><Icon name="close" size={16} /></button>
    </header>

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
      <h4>界面大小 <small>自动：和游戏界面一样大</small></h4>
      <div class="scales">
        {#each scales as s (s.v)}
          <button class:on={app.settings.uiScale === s.v} onclick={() => set('uiScale', s.v)}>{s.label}</button>
        {/each}
      </div>
    </section>

    <section>
      <label class="row">
        <span class="lbl"><b>商店标记</b><small>在游戏商店里给追踪的干员打标记</small></span>
        <input type="checkbox" class="sw" checked={app.settings.shopMarkers} onchange={(e) => set('shopMarkers', e.currentTarget.checked)} />
      </label>
    </section>

    <section>
      <label class="row">
        <span class="lbl"
          ><b>跟随游戏窗口</b><small
            >{app.settings.followGame
              ? '侧栏贴在游戏右边，全屏或放不下时盖在游戏左侧'
              : '侧栏停在你拖到的位置，不再跟着游戏；要盖在全屏游戏上请打开置顶'}</small
          ></span
        >
        <input type="checkbox" class="sw" checked={app.settings.followGame} onchange={(e) => set('followGame', e.currentTarget.checked)} />
      </label>
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
          <button class="btn" disabled={u.state === 'checking'} onclick={() => app.checkUpdate()}>
            <Icon name="refresh" size={14} />检查更新
          </button>
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
      <button class="logbtn" onclick={() => (app.changelogOpen = true)}>
        <span><b>更新日志</b><small>每个版本更新了什么</small></span>
        <Icon name="arrowRight" size={16} />
      </button>
      <label class="row">
        <span class="lbl"><b>自动检查更新</b><small>启动时检查一次，从 GitHub Releases 获取</small></span>
        <input type="checkbox" class="sw" checked={app.settings.autoCheckUpdate} onchange={(e) => set('autoCheckUpdate', e.currentTarget.checked)} />
      </label>
    </section>

    <section class="about">
      <button class="link" onclick={() => api.openUrl(REPO)}>GitHub 开源地址</button>
      <button class="link" onclick={() => api.openUrl(QQ)}>QQ 交流群 1132134217</button>
      <p>非官方玩家自制工具，与鹰角网络无关。游戏美术与数据归上海鹰角网络所有。</p>
    </section>
  </aside>
{/if}

<style>
  .acct {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 4px 12px;
    margin: 0;
    font-size: 12px;
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
    background: rgb(0 0 0 / 0.55);
    z-index: 10;
  }
  .sheet {
    position: absolute;
    left: 0;
    right: 0;
    bottom: 0;
    max-height: 86%;
    overflow-y: auto;
    z-index: 11;
    padding: 12px 12px 16px;
    background:
      var(--noise),
      linear-gradient(180deg, #202326, #16181a 120px);
    box-shadow:
      0 -1px 0 var(--line),
      0 -12px 30px rgb(0 0 0 / 0.6);
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
    font-size: 18px;
  }
  header .term {
    font-size: 9px;
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
    font-size: 13px;
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
    font-size: 11px;
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
    font-size: 13px;
  }
  .themes em {
    font-style: normal;
    font-size: 11px;
    color: var(--fg-4);
  }
  .scales {
    display: grid;
    grid-template-columns: repeat(6, 1fr);
    gap: 2px;
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
    font-size: 11px;
    color: var(--fg-4);
  }
  .scales button {
    height: 30px;
    font-size: 12px;
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
    font-size: 12px;
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
    font-size: 13px;
    color: var(--fg-1);
  }
  .logbtn small {
    font-size: 11px;
    color: var(--fg-4);
  }
  .lbl {
    flex: 1;
    display: flex;
    flex-direction: column;
  }
  .lbl b {
    font-size: 13px;
  }
  .lbl small {
    font-size: 11px;
    color: var(--fg-4);
  }
  /* 开关 */
  .sw {
    appearance: none;
    flex: none;
    width: 34px;
    height: 18px;
    margin: 0;
    border-radius: 9px;
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
    font-size: 12px;
    color: var(--fg-3);
  }
  .msg.new {
    color: var(--mint);
  }
  .msg.bad {
    color: #f2b8ba;
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
    font-size: 12px;
    font-weight: 700;
    color: var(--fg-1);
    background: var(--bg-4);
  }
  .btn:hover {
    background: #3a3e42;
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
    font-size: 12px;
    color: var(--mint);
    text-decoration: underline;
    text-underline-offset: 2px;
  }
  .path {
    display: flex;
    align-items: flex-start;
    gap: 8px;
    margin: 0 0 8px;
    font-size: 11px;
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
    font-size: 12px;
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
    font-size: 13px;
  }
  .mt small {
    font-size: 11px;
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
    font-size: 11px;
    color: var(--fg-4);
  }
  .dmsg {
    margin: 8px 0 0;
    font-size: 12px;
    color: var(--mint);
    word-break: break-all;
  }
  .dmsg.bad {
    color: #f2b8ba;
  }
  .about {
    display: flex;
    flex-wrap: wrap;
    gap: 6px 16px;
  }
  .about p {
    width: 100%;
    margin: 4px 0 0;
    font-size: 11px;
    color: var(--fg-4);
  }
</style>
