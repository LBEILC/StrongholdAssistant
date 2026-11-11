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
  import CollapsedStrip from './components/CollapsedStrip.svelte'
  import { fly } from 'svelte/transition'
  import { cubicOut } from 'svelte/easing'

  let scroller: HTMLElement | undefined = $state()
  $effect(() => {
    app.tab
    if (scroller) scroller.scrollTop = 0
  })

  // 切换标签时页面从切换方向滑入：往右边的标签切，新页面从右边进来
  const order = ['match', 'lineup', 'codex']
  let prev = app.tab
  let dir = $state(1)
  $effect.pre(() => {
    const t = app.tab
    dir = order.indexOf(t) >= order.indexOf(prev) ? 1 : -1
    prev = t
  })
</script>

{#if app.collapsed}
  <CollapsedStrip />
{:else}
<div class="shell {app.phase}">
  <TitleBar />
  <BondTracker />
  <Tabs />
  {#if app.update.state === 'available' || app.update.state === 'downloading' || app.update.state === 'ready'}
    <button class="updbar" onclick={() => (app.settingsOpen = true)}>
      <b class="term">Update</b>
      {app.update.state === 'available' ? `发现新版本 v${app.update.version}，点这里更新` : app.update.state === 'downloading' ? `正在下载 v${app.update.version}…` : '下载完成，即将重启'}
    </button>
  {/if}
  <div class="content">
    <main bind:this={scroller}>
      {#key app.tab}
        <div in:fly={{ x: 28 * dir, duration: 240, easing: cubicOut }}>
          {#if app.tab === 'match'}
            <MatchPage />
          {:else if app.tab === 'lineup'}
            <LineupPage />
          {:else}
            <CodexPage />
          {/if}
        </div>
      {/key}
    </main>
    <DetailSheet />
    <LineupEditor />
    <SettingsSheet />
  </div>
</div>
{/if}

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
  }
</style>
