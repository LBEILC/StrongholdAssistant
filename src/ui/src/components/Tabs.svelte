<script lang="ts">
  import { app, type Tab } from '../lib/state.svelte'

  const tabs: { id: Tab; label: string; en: string }[] = [
    { id: 'match', label: '本局', en: 'Match' },
    { id: 'lineup', label: '阵容', en: 'Lineup' },
    { id: 'codex', label: '图鉴', en: 'Archive' },
  ]
</script>

<nav class="tabs">
  {#each tabs as t (t.id)}
    <button class:on={app.tab === t.id} onclick={() => (app.tab = t.id)}>
      <span class="in"><span class="lb">{t.label}</span><span class="en term">{t.en}</span></span>
    </button>
  {/each}
</nav>

<style>
  .tabs {
    flex: none;
    display: grid;
    grid-template-columns: repeat(3, 1fr);
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
    transition: color var(--t-fast);
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
  button.on::after {
    /* 底部斜切亮条 */
    content: '';
    position: absolute;
    left: 18%;
    right: 18%;
    bottom: -1px;
    height: 3px;
    background: var(--mint);
    clip-path: polygon(3px 0, calc(100% - 3px) 0, 100% 100%, 0 100%);
    box-shadow: var(--mint-glow);
  }
</style>
