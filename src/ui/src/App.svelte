<script lang="ts">
  import { app } from './lib/state.svelte'
  import TitleBar from './components/TitleBar.svelte'
  import BondTracker from './components/BondTracker.svelte'
  import Tabs from './components/Tabs.svelte'
  import DetailSheet from './components/DetailSheet.svelte'
  import LineupEditor from './components/LineupEditor.svelte'
  import SettingsSheet from './components/SettingsSheet.svelte'
  import MatchPage from './pages/MatchPage.svelte'
  import LineupPage from './pages/LineupPage.svelte'
  import CodexPage from './pages/CodexPage.svelte'
  import HistoryPage from './pages/HistoryPage.svelte'
  import CollapsedStrip from './components/CollapsedStrip.svelte'
  import EconomyBar from './components/EconomyBar.svelte'
  import ContextMenu from './components/ContextMenu.svelte'
  import BanPicker from './components/BanPicker.svelte'
  import ImportSheet from './components/ImportSheet.svelte'
  import PlazaSheet from './components/PlazaSheet.svelte'
  import { plaza } from './lib/plaza.svelte'
  import Toast from './components/Toast.svelte'
  import ChangelogSheet from './components/ChangelogSheet.svelte'
  import WhatsNew from './components/WhatsNew.svelte'
  import Tooltip from './components/Tooltip.svelte'
  import { api } from './lib/api'
  import { fly } from 'svelte/transition'
  import { cubicOut } from 'svelte/easing'

  let upH = $state(0)
  $effect(() => {
    if (app.collapsed === 'up' && upH) api.collapseUp(upH)
  })

  let scroller: HTMLElement | undefined = $state()
  $effect(() => {
    app.tab
    if (scroller) scroller.scrollTop = 0
  })

  // 切换标签时页面从切换方向滑入：往右边的标签切，新页面从右边进来
  const order = ['match', 'lineup', 'codex', 'history']
  let prev = app.tab
  let dir = $state(1)
  $effect.pre(() => {
    const t = app.tab
    dir = order.indexOf(t) >= order.indexOf(prev) ? 1 : -1
    prev = t
  })

  // 网站上点「在助手里打开」：stronghold://import?post=<帖子 id> 打开广场详情，?code=<分享码> 打开导入面板
  $effect(() =>
    api.onDeepLink((url) => {
      let u: URL
      try {
        u = new URL(url)
      } catch {
        return
      }
      const post = u.searchParams.get('post')
      const code = u.searchParams.get('code')
      app.setTab('lineup')
      if (post)
        plaza
          .post(post)
          .then((p) => (plaza.viewing = p))
          .catch((e) => app.showToast(e.message, true))
      else if (code) {
        app.importPrefill = code
        app.importOpen = true
      }
    }),
  )
</script>

{#if app.collapsed === 'strip'}
  <CollapsedStrip />
{:else if app.collapsed === 'up'}
  <!-- 向上收起：只留标题栏和追踪列表，窗口高度跟着内容（追踪增减时再告诉宿主） -->
  <div class="shell up" bind:clientHeight={upH}>
    <TitleBar />
    <EconomyBar compact />
    <BondTracker />
  </div>
{:else}
<div class="shell {app.phase}" class:wb={app.workbench}>
  <TitleBar />
  {#if app.workbench}
    <!-- 工作台：左边竖排导航，右边追踪列表和内容；详情面板停在右侧，不挡左边 -->
    <div class="wbody">
      <Tabs vertical />
      <div class="wcol">
        <BondTracker />
        {@render body()}
      </div>
    </div>
  {:else}
    <BondTracker />
    <Tabs />
    {@render body()}
  {/if}
</div>
{/if}

{#snippet body()}
  {#if app.update.state === 'available' || app.update.state === 'downloading' || app.update.state === 'ready'}
    <button class="updbar" onclick={() => (app.settingsOpen = true)}>
      <b class="term">Update</b>
      {app.update.state === 'available' ? `发现新版本 v${app.update.version}，点这里更新` : app.update.state === 'downloading' ? `正在下载 v${app.update.version}…` : '下载完成，即将重启'}
    </button>
  {/if}
  <div class="content" class:wb={app.workbench} class:docked={app.workbench && (!!app.detail || !!plaza.viewing)}>
    <main bind:this={scroller}>
      {#key app.tab}
        <div in:fly={{ x: 28 * dir, duration: 240, easing: cubicOut }}>
          {#if app.tab === 'match'}
            <MatchPage />
          {:else if app.tab === 'lineup'}
            <LineupPage />
          {:else if app.tab === 'codex'}
            <CodexPage />
          {:else}
            <HistoryPage />
          {/if}
        </div>
      {/key}
    </main>
    <DetailSheet />
    <LineupEditor />
    <BanPicker />
    <ImportSheet />
    <PlazaSheet />
    <Toast />
    <SettingsSheet />
    <ChangelogSheet />
  </div>
{/snippet}
<ContextMenu />
<Tooltip />
{#if app.phase !== 'boot'}<WhatsNew />{/if}

<style>
  /* 启动：标题栏和原生骨架一致、不动，其余区块等宿主显示网页后依次淡入上移 */
  .shell.boot > :global(:not(:first-child)) {
    opacity: 0;
  }
  .shell.in > :global(:not(:first-child)) {
    animation: rise 260ms var(--ease-out) both;
  }
  .shell.in > :global(:nth-child(3)) {
    animation-delay: 50ms;
  }
  .shell.in > :global(:nth-child(n + 4)) {
    animation-delay: 100ms;
  }
  @keyframes rise {
    from {
      opacity: 0;
      transform: translateY(10px);
    }
  }
  /* 关闭：标题栏留着，其余淡出到底色 */
  .shell.out > :global(:not(:first-child)) {
    opacity: 0;
    transform: translateY(6px);
    transition:
      opacity 150ms ease-in,
      transform 150ms ease-in;
  }
  .shell {
    height: 100%;
    display: flex;
    flex-direction: column;
    background:
      var(--noise),
      radial-gradient(120% 60% at 50% 0%, #15171a, var(--bg-0));
    overflow: hidden;
  }
  /* 向上收起：高度由内容决定，底边一道薄荷绿细线表示下面还收着东西 */
  .shell.up {
    height: auto;
    border-bottom: 1px solid var(--mint-lo);
  }
  .updbar {
    flex: none;
    display: flex;
    align-items: center;
    gap: 8px;
    height: 28px;
    padding: 0 10px;
    font-size: 12px;
    text-align: left;
    color: var(--gold);
    background: rgb(243 179 34 / 0.1);
    border-bottom: 1px solid rgb(243 179 34 / 0.3);
  }
  .updbar b {
    font-size: 9px;
    padding: 2px 4px 1px;
    color: var(--gold-ink);
    background: var(--gold);
  }
  .content {
    position: relative;
    flex: 1;
    min-height: 0;
  }
  main {
    height: 100%;
    overflow-x: hidden;
    overflow-y: auto;
    /* 页面按自己的可用宽度换多栏（容器查询），侧栏和工作台用同一套页面 */
    container-type: inline-size;
    transition: margin-right 240ms var(--ease-out);
  }

  /* 工作台 */
  .wbody {
    flex: 1;
    min-height: 0;
    display: flex;
  }
  .wbody > :global(.tabs) {
    flex: none;
    width: 132px;
  }
  .wcol {
    flex: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
  }
  .content.wb main > :global(div) {
    max-width: 1180px;
    margin: 0 auto;
  }
  /* 面板都停在右边：详情、广场帖子一栏宽，编辑器和禁用选择宽一些 */
  .content.wb > :global(aside) {
    top: 0;
    bottom: 0;
    left: auto;
    right: 0;
    width: min(440px, 46%);
    max-height: none;
    box-shadow:
      -1px 0 0 var(--line),
      -16px 0 32px rgb(0 0 0 / 0.45);
  }
  .content.wb > :global(aside.ed) {
    width: min(720px, 66%);
  }
  .content.wb :global(aside > .grip) {
    display: none;
  }
  .content.wb > :global(.scrim) {
    background: rgb(0 0 0 / 0.3);
  }
  /* 详情和广场帖子不遮左边：左边照常能点，点别的卡片直接换内容 */
  .content.wb > :global(.scrim.dock) {
    display: none;
  }
  .content.docked main {
    margin-right: min(440px, 46%);
  }
</style>
