<script lang="ts">
  import { fly, fade } from 'svelte/transition'
  import { cubicOut } from 'svelte/easing'
  import { app, bondProgress } from '../lib/state.svelte'
  import { asset } from '../lib/assets'
  import BondIcon from './BondIcon.svelte'
  import OpTile from './OpTile.svelte'
  import Icon from './Icon.svelte'

  // 阵容编辑器：几乎全高的面板。上半是阵容本身（名字、9 个位置、盟约人数），下半是挑干员
  const MAX = 9
  const e = $derived(app.editing)
  let q = $state('')
  let bond = $state<string | null>(null)

  const inLineup = (n: string) => !!e?.members.some((m) => m.name === n)
  const add = (n: string) => {
    if (!e) return
    if (inLineup(n)) e.members = e.members.filter((m) => m.name !== n)
    else if (e.members.length < MAX) e.members = [...e.members, { name: n }]
  }
  const pool = $derived(
    app.catalog.operators
      .filter((o) => (!q || o.name.includes(q)) && (!bond || o.core.includes(bond) || o.extra.includes(bond)))
      .sort((a, b) => a.tier - b.tier),
  )
  const bonds = $derived(e ? bondProgress(e.members, new Set()) : [])
  let confirmDelete = $state(false)
</script>

{#if e}
  <div class="scrim" transition:fade={{ duration: 160 }}></div>
  <aside class="ed" transition:fly={{ y: 400, duration: 260, easing: cubicOut }}>
    <header>
      <input class="nm" bind:value={e.name} placeholder="阵容名称" />
      <button class="x" onclick={() => (app.editing = null)} aria-label="关闭"><Icon name="close" size={16} /></button>
    </header>

    <div class="slots">
      {#each Array(MAX) as _, i (i)}
        {@const m = e.members[i]}
        {#if m}
          <OpTile name={m.name} transform={m.transform} size={34} showName={false} onclick={() => add(m.name)} />
        {:else}
          <span class="slot"></span>
        {/if}
      {/each}
      <span class="cap term">{e.members.length}/{MAX}</span>
    </div>
    <div class="bonds">
      {#each bonds as b (b.covenant.id)}
        <span class="bc"><BondIcon id={b.covenant.id} size={18} on />{b.covenant.name}<b>{b.planned}</b></span>
      {:else}
        <span class="muted">还没有达到激活人数的盟约</span>
      {/each}
    </div>

    <div class="pick">
      <div class="search">
        <Icon name="search" size={14} />
        <input bind:value={q} placeholder="搜索干员" />
      </div>
      <div class="bondpick">
        {#each app.catalog.covenants as c (c.id)}
          <button class="bp" class:on={bond === c.id} title={c.name} onclick={() => (bond = bond === c.id ? null : c.id)}>
            <span class="glyph" style:--src="url({asset.bond(c.id)})"></span>
          </button>
        {/each}
      </div>
      <div class="pool">
        {#each pool as o (o.name)}
          {@const sel = inLineup(o.name)}
          <span class="pi" class:sel class:full={!sel && e.members.length >= MAX}>
            <OpTile name={o.name} size={44} owned={sel} onclick={() => add(o.name)} />
          </span>
        {/each}
      </div>
    </div>

    <footer>
      {#if e.id}
        {#if confirmDelete}
          <button class="danger" onclick={() => app.deleteLineup(e.id)}>确认删除</button>
          <button class="ghost" onclick={() => (confirmDelete = false)}>不删</button>
        {:else}
          <button class="ghost" onclick={() => (confirmDelete = true)}><Icon name="trash" size={14} />删除</button>
        {/if}
      {/if}
      <span class="spacer"></span>
      <button class="ghost" onclick={() => (app.editing = null)}>取消</button>
      <button class="primary" disabled={!e.name.trim() || !e.members.length} onclick={() => app.saveEditing()}>保存</button>
    </footer>
  </aside>
{/if}

<style>
  .scrim {
    position: absolute;
    inset: 0;
    background: rgb(0 0 0 / 0.6);
    z-index: 20;
  }
  .ed {
    position: absolute;
    left: 0;
    right: 0;
    bottom: 0;
    top: 24px;
    z-index: 21;
    display: flex;
    flex-direction: column;
    background:
      var(--noise),
      linear-gradient(180deg, #202326, #141618 160px);
    box-shadow:
      0 -1px 0 var(--line),
      0 -12px 30px rgb(0 0 0 / 0.6);
  }
  .ed::before {
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
    align-items: center;
    gap: 6px;
    padding: 12px 8px 8px 12px;
  }
  .nm {
    flex: 1;
    min-width: 0;
    height: 32px;
    padding: 0 8px;
    font-size: 15px;
    font-weight: 700;
    background: var(--bg-1);
    border: 0;
    outline: 0;
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .nm:focus {
    box-shadow: inset 0 0 0 1px var(--mint-lo);
  }
  .nm::placeholder {
    color: var(--fg-4);
    font-weight: 400;
  }
  .x {
    width: 30px;
    height: 30px;
    display: grid;
    place-items: center;
    color: var(--fg-3);
  }
  .x:hover {
    color: var(--fg-1);
    background: var(--bg-4);
  }
  .slots {
    display: flex;
    align-items: center;
    gap: 3px;
    padding: 0 12px;
  }
  .slot {
    width: 34px;
    height: 34px;
    background: var(--hatch), var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line-soft);
  }
  .cap {
    margin-left: auto;
    font-size: 11px;
    color: var(--fg-3);
  }
  .bonds {
    display: flex;
    flex-wrap: wrap;
    gap: 4px 10px;
    padding: 8px 12px 10px;
    min-height: 34px;
    border-bottom: 1px solid var(--line);
  }
  .bc {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    font-size: 11px;
    color: var(--fg-2);
  }
  .bc b {
    font-family: var(--font-term);
    color: var(--mint);
  }
  .muted {
    font-size: 11px;
    color: var(--fg-4);
  }
  .pick {
    flex: 1;
    min-height: 0;
    display: flex;
    flex-direction: column;
    gap: 6px;
    padding: 10px 12px 0;
  }
  .search {
    display: flex;
    align-items: center;
    gap: 6px;
    height: 28px;
    padding: 0 8px;
    color: var(--fg-3);
    background: var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .search input {
    flex: 1;
    min-width: 0;
    background: none;
    border: 0;
    outline: 0;
    font-size: 12px;
    color: var(--fg-1);
  }
  .bondpick {
    display: grid;
    grid-template-columns: repeat(13, 1fr);
    gap: 3px;
  }
  .bp {
    aspect-ratio: 1;
    display: grid;
    place-items: center;
    border-radius: 50%;
    color: var(--fg-3);
    background: var(--bg-2);
  }
  .bp .glyph {
    width: 62%;
    height: 62%;
  }
  .bp.on {
    color: var(--mint-ink);
    background: var(--mint);
  }
  .pool {
    flex: 1;
    overflow-y: auto;
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(52px, 1fr));
    gap: 6px 2px;
    justify-items: center;
    align-content: start;
    padding: 4px 0 10px;
  }
  .pi.full {
    opacity: 0.35;
  }
  footer {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 10px 12px;
    border-top: 1px solid var(--line);
    background: var(--bg-1);
  }
  .spacer {
    flex: 1;
  }
  .ghost {
    display: flex;
    align-items: center;
    gap: 3px;
    font-size: 12px;
    color: var(--fg-3);
    padding: 5px 8px;
  }
  .ghost:hover {
    color: var(--fg-1);
  }
  .danger {
    font-size: 12px;
    padding: 5px 10px;
    color: #fff;
    background: var(--red);
  }
  .primary {
    height: 30px;
    padding: 0 18px;
    font-size: 13px;
    font-weight: 700;
    color: var(--mint-ink);
    background: var(--mint);
  }
  .primary:disabled {
    color: var(--fg-4);
    background: var(--bg-3);
    cursor: default;
  }
</style>
