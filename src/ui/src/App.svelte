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

  let scroller: HTMLElement | undefined = $state()
  $effect(() => {
    app.tab
    if (scroller) scroller.scrollTop = 0
  })
</script>

{#if app.collapsed}
  <CollapsedStrip />
{:else}
<div class="shell">
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
      {#if app.tab === 'match'}
        <MatchPage />
      {:else if app.tab === 'lineup'}
        <LineupPage />
      {:else}
        <CodexPage />
      {/if}
    </main>
    <DetailSheet />
    <LineupEditor />
    <SettingsSheet />
  </div>
</div>
{/if}

<style>
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
    overflow-y: auto;
  }
</style>
