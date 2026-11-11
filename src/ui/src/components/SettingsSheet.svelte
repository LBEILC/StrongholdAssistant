<script lang="ts">
  import { fly, fade } from 'svelte/transition'
  import { cubicOut } from 'svelte/easing'
  import { app } from '../lib/state.svelte'
  import { api, type GameTheme } from '../lib/api'
  import Icon from './Icon.svelte'

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
      <h4>版本 <small class="term">v{app.appInfo.version}</small></h4>
      <div class="upd">
        {#if u.state === 'available'}
          <span class="msg new">发现新版本 <b>v{u.version}</b></span>
          {#if u.notesUrl}<button class="link" onclick={() => api.openUrl(u.notesUrl!)}>更新内容</button>{/if}
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
    align-items: baseline;
    gap: 8px;
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
  .upd + .row {
    margin-top: 10px;
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
