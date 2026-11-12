<script lang="ts">
  import { sheet } from '../lib/sheet'
  import { fade, slide } from 'svelte/transition'
  import { cubicOut } from 'svelte/easing'
  import { app } from '../lib/state.svelte'
  import { CHANGELOG } from '../lib/changelog'
  import NotesList from './NotesList.svelte'
  import Icon from './Icon.svelte'

  // 更新日志：历来每个版本更新了什么（打包进来的，不用联网）。最新一版和当前版本默认展开，其余点标题展开
  const close = () => (app.changelogOpen = false)
  let open = $state<Record<string, boolean>>({})
  $effect(() => {
    if (app.changelogOpen) open = { [CHANGELOG[0]?.version]: true, [app.appInfo.version]: true }
  })
</script>

<svelte:window onkeydown={(e) => app.changelogOpen && e.key === 'Escape' && (e.stopPropagation(), close())} />

{#if app.changelogOpen}
  <div class="scrim" transition:fade={{ duration: 160 }} onclick={close} role="presentation"></div>
  <aside class="sheet" transition:sheet={{ y: 360, duration: 240 }}>
    <header>
      <h3>更新日志</h3>
      <span class="term">Changelog</span>
      <button class="x" onclick={close} aria-label="关闭"><Icon name="close" size={16} /></button>
    </header>
    {#each CHANGELOG as n (n.version)}
      <section>
        <button class="vh" onclick={() => (open[n.version] = !open[n.version])} aria-expanded={!!open[n.version]}>
          <b class="term">v{n.version}</b>
          {#if n.version === app.appInfo.version}<em class="cur">当前版本</em>{/if}
          <span class="date">{n.date}</span>
          <span class="cnt">{n.items.length} 项</span>
          <span class="chev" class:open={open[n.version]}><Icon name="arrowDown" size={14} /></span>
        </button>
        {#if open[n.version]}
          <div transition:slide={{ duration: 200, easing: cubicOut }}><NotesList items={n.items} /></div>
        {/if}
      </section>
    {/each}
  </aside>
{/if}

<style>
  .scrim {
    position: absolute;
    inset: 0;
    background: rgb(0 0 0 / 0.55);
    z-index: 12;
  }
  .sheet {
    position: absolute;
    left: 0;
    right: 0;
    bottom: 0;
    max-height: 90%;
    overflow-y: auto;
    z-index: 13;
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
    border-top: 1px solid var(--line-soft);
    padding: 4px 0;
  }
  .vh {
    width: 100%;
    display: flex;
    align-items: center;
    gap: 8px;
    height: 32px;
    text-align: left;
  }
  .vh b {
    font-size: 14px;
    color: var(--fg-1);
  }
  .cur {
    font-style: normal;
    font-size: 10px;
    padding: 1px 5px;
    color: var(--mint-ink);
    background: var(--mint);
  }
  .date {
    font-size: 11px;
    color: var(--fg-4);
  }
  .cnt {
    margin-left: auto;
    font-size: 11px;
    color: var(--fg-4);
  }
  .chev {
    display: grid;
    color: var(--fg-3);
    transition: transform var(--t-mid) var(--ease-out);
  }
  .chev.open {
    transform: rotate(180deg);
  }
  section > div {
    padding-bottom: 6px;
  }
</style>
