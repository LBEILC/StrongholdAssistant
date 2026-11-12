<script lang="ts">
  import { app, type Tab } from '../lib/state.svelte'

  const tabs: { id: Tab; label: string; en: string }[] = [
    { id: 'match', label: '本局', en: 'Match' },
    { id: 'lineup', label: '阵容', en: 'Lineup' },
    { id: 'codex', label: '图鉴', en: 'Archive' },
    { id: 'history', label: '历史', en: 'Record' },
  ]
  const idx = $derived(tabs.findIndex((t) => t.id === app.tab))
  // vertical：工作台左边的竖排导航
  let { vertical = false }: { vertical?: boolean } = $props()
</script>

<nav class="tabs" class:vertical style:--n={tabs.length}>
  {#each tabs as t (t.id)}
    <button class:on={app.tab === t.id} onclick={() => app.setTab(t.id)}>
      <span class="in"><span class="lb">{t.label}</span><span class="en term">{t.en}</span></span>
    </button>
  {/each}
  <!-- 底部亮条：一条，切换时滑到目标标签 -->
  <span class="ink" style:transform={vertical ? `translateY(${idx * 100}%)` : `translateX(${idx * 100}%)`}></span>
</nav>

<style>
  .tabs {
    position: relative;
    flex: none;
    display: grid;
    grid-template-columns: repeat(var(--n), 1fr);
    background: var(--bg-1);
    border-bottom: 1px solid var(--line);
  }
  button {
    position: relative;
    display: flex;
    align-items: center;
    justify-content: center;
    height: 38px;
    padding-bottom: 1px;
    color: var(--fg-3);
    transition: color var(--t-mid);
  }
  button:hover {
    color: var(--fg-1);
  }
  .in {
    display: flex;
    align-items: baseline;
    gap: 5px;
    line-height: 1;
  }
  .lb {
    font-size: 14px;
    font-weight: 700;
    letter-spacing: 0.1em;
  }
  .en {
    font-size: 8px;
    color: var(--fg-4);
  }
  button.on {
    color: var(--fg-1);
    background: linear-gradient(180deg, transparent 40%, var(--mint-tint));
  }
  button.on .en {
    color: var(--mint);
  }
  .ink {
    position: absolute;
    left: 0;
    bottom: -1px;
    width: calc(100% / var(--n));
    height: 3px;
    pointer-events: none;
    transition: transform var(--t-mid) var(--ease-out);
  }
  .ink::after {
    /* 斜切亮条 */
    content: '';
    position: absolute;
    left: 18%;
    right: 18%;
    top: 0;
    bottom: 0;
    background: var(--mint);
    clip-path: polygon(3px 0, calc(100% - 3px) 0, 100% 100%, 0 100%);
    box-shadow: var(--mint-glow);
  }
  /* 竖排：每项一行，亮条在左边 */
  .vertical {
    grid-template-columns: none;
    grid-auto-rows: 46px;
    align-content: start;
    border-bottom: none;
    border-right: 1px solid var(--line);
  }
  .vertical button {
    height: auto;
    justify-content: flex-start;
    padding: 0 0 0 18px;
  }
  .vertical button.on {
    background: linear-gradient(90deg, var(--mint-tint), transparent 70%);
  }
  .vertical .in {
    flex-direction: column;
    align-items: flex-start;
    gap: 3px;
  }
  .vertical .ink {
    bottom: auto;
    top: 0;
    width: 3px;
    height: 46px;
  }
  .vertical .ink::after {
    left: 0;
    right: 0;
    top: 10px;
    bottom: 10px;
    clip-path: polygon(0 0, 100% 3px, 100% calc(100% - 3px), 0 100%);
  }
</style>
