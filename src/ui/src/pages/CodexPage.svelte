<script lang="ts">
  import { app } from '../lib/state.svelte'
  import { asset, ROMAN } from '../lib/assets'
  import OpCard from '../components/OpCard.svelte'
  import TierBadge from '../components/TierBadge.svelte'
  import Icon from '../components/Icon.svelte'

  // 图鉴：干员 / 装备网格，筛选常驻展开；点卡片在底部面板看详情（不靠悬停）
  let kind = $state<'operator' | 'equipment'>('operator')
  let q = $state('')
  let tiers = $state<number[]>([])
  let bonds = $state<string[]>([])
  let onlyTracked = $state(false)
  let onlyFocus = $state(false)
  let hideBanned = $state(false)
  let cat = $state<string | null>(null)

  const toggle = <T,>(arr: T[], v: T) => (arr.includes(v) ? arr.filter((x) => x !== v) : [...arr, v])

  const ops = $derived(
    app.catalog.operators.filter(
      (o) =>
        (!q || o.name.includes(q)) &&
        (!tiers.length || tiers.includes(o.tier)) &&
        (!bonds.length || bonds.every((b) => o.core.includes(b) || o.extra.includes(b))) &&
        (!onlyTracked || app.tracked.has(o.name)) &&
        (!onlyFocus || app.focusOps.has(o.name)) &&
        (!hideBanned || !app.banned.has(o.name)),
    ),
  )
  const cats = [...new Set(app.catalog.equipment.flatMap((e) => e.categories))]
  const eqs = $derived(
    app.catalog.equipment.filter(
      (e) => (!q || e.name.includes(q)) && (!tiers.length || tiers.includes(e.tier)) && (!cat || e.categories.includes(cat)),
    ),
  )
  // 按等阶分组；不可招募的特殊干员（如暴躁铁皮）单独放最后一组，t = 0
  const groups = $derived(
    [1, 2, 3, 4, 5, 6, 0]
      .map((t) => ({
        t,
        ops: ops.filter((o) => (t ? !o.obtain && o.tier === t : !!o.obtain)),
        eqs: t ? eqs.filter((e) => e.tier === t) : [],
      }))
      .filter((g) => (kind === 'operator' ? g.ops.length : g.eqs.length)),
  )
  const cur = $derived(app.detail)
  const filtered = $derived(tiers.length + bonds.length + (onlyTracked ? 1 : 0) + (onlyFocus ? 1 : 0) + (hideBanned ? 1 : 0) + (cat ? 1 : 0) + (q ? 1 : 0))
</script>

<div class="page">
  <!-- 只让类型切换和搜索吸顶，筛选条件随页面滚走，免得占掉半屏 -->
  <div class="top">
    <div class="seg">
      <button class:on={kind === 'operator'} onclick={() => (kind = 'operator')}>干员 <em>{app.catalog.operators.length}</em></button>
      <button class:on={kind === 'equipment'} onclick={() => (kind = 'equipment')}>装备 <em>{app.catalog.equipment.length}</em></button>
    </div>
    <div class="search">
      <span class="si"><Icon name="search" size={15} /></span>
      <input bind:value={q} placeholder={kind === 'operator' ? '搜索干员' : '搜索装备'} />
      {#if filtered}
        <button class="clear" onclick={() => { q = ''; tiers = []; bonds = []; onlyTracked = false; onlyFocus = false; hideBanned = false; cat = null }}>清除筛选</button>
      {/if}
    </div>
  </div>
  <div class="filters">
    <div class="chips">
      {#each [1, 2, 3, 4, 5, 6] as t (t)}
        <button class="chip tier" class:on={tiers.includes(t)} style:--c="var(--tier-{t})" onclick={() => (tiers = toggle(tiers, t))}>{ROMAN[t]}</button>
      {/each}
      {#if kind === 'operator'}
        <span class="sep"></span>
        <button class="chip" class:on={onlyTracked} onclick={() => (onlyTracked = !onlyTracked)}>追踪中</button>
        <button class="chip" class:on={onlyFocus} onclick={() => (onlyFocus = !onlyFocus)}>关注</button>
        <button class="chip" class:on={hideBanned} onclick={() => (hideBanned = !hideBanned)}>隐藏禁用</button>
      {/if}
    </div>
    {#if kind === 'operator'}
      <div class="bondpick">
        {#each app.catalog.covenants as c (c.id)}
          <button class="bp" class:on={bonds.includes(c.id)} class:core={c.kind === 'core'} title={c.name} onclick={() => (bonds = toggle(bonds, c.id))}>
            <span class="glyph" style:--src="url({asset.bond(c.id)})"></span>
          </button>
        {/each}
      </div>
      {#if bonds.length}
        <div class="picked">盟约：{bonds.map((b) => app.cov(b).name).join(' + ')}</div>
      {/if}
    {:else}
      <div class="chips">
        {#each cats as c (c)}
          <button class="chip" class:on={cat === c} onclick={() => (cat = cat === c ? null : c)}>{c}</button>
        {/each}
      </div>
    {/if}
  </div>

  {#each groups as g (g.t)}
    <div class="ghead" style:--c={g.t ? `var(--tier-${g.t})` : 'var(--gold)'}>
      {#if g.t}
        <TierBadge tier={g.t} size={18} />
        <span class="term">Tier {ROMAN[g.t]}</span>
      {:else}
        <span class="special">特殊 · 不可招募</span>
      {/if}
      <i></i>
      <em>{kind === 'operator' ? g.ops.length : g.eqs.length}</em>
    </div>
    <div class="grid">
      {#if kind === 'operator'}
        {#each g.ops as o (o.name)}
          <OpCard name={o.name} selected={cur?.kind === 'operator' && cur.name === o.name} onclick={() => (app.detail = { kind: 'operator', name: o.name })} />
        {/each}
      {:else}
        {#each g.eqs as e (e.name)}
          <button
            class="eq"
            class:selected={cur?.kind === 'equipment' && cur.name === e.name}
            style:--c="var(--tier-{e.tier})"
            style:--bg="url({asset.card('bg_black')})"
            onclick={() => (app.detail = { kind: 'equipment', name: e.name })}
          >
            <span class="et"><TierBadge tier={e.tier} size={18} /></span>
            <img src={asset.equipment(e.name)} alt="" loading="lazy" />
            <span class="en">{e.name}</span>
            <span class="ec">{e.categories.join(' · ')}</span>
          </button>
        {/each}
      {/if}
    </div>
  {:else}
    <p class="none">没有符合条件的{kind === 'operator' ? '干员' : '装备'}。</p>
  {/each}
</div>

<style>
  .special {
    font-size: 12px;
    font-weight: 700;
    color: var(--gold);
  }
  .page {
    padding: 0 10px 24px;
  }
  .filters {
    display: flex;
    flex-direction: column;
    gap: 7px;
    padding-top: 2px;
  }
  .top {
    position: sticky;
    top: 0;
    z-index: 2;
    display: flex;
    flex-direction: column;
    gap: 7px;
    margin: 0 -10px 4px;
    padding: 10px 10px 9px;
    background: var(--bg-0);
    border-bottom: 1px solid var(--line-soft);
  }
  .seg {
    display: grid;
    grid-template-columns: 1fr 1fr;
    background: var(--bg-2);
    padding: 2px;
  }
  .seg button {
    height: 26px;
    font-size: 13px;
    font-weight: 600;
    color: var(--fg-3);
  }
  .seg button em {
    font-style: normal;
    font-family: var(--font-term);
    font-size: 11px;
    color: var(--fg-4);
  }
  .seg button.on {
    color: var(--fg-1);
    background: var(--bg-4);
    box-shadow: inset 0 -2px 0 var(--mint);
  }
  .search {
    display: flex;
    align-items: center;
    gap: 6px;
    height: 30px;
    padding: 0 8px;
    background: var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .search:focus-within {
    box-shadow: inset 0 0 0 1px var(--mint-lo);
  }
  .si {
    display: grid;
    color: var(--fg-3);
  }
  .search input {
    flex: 1;
    min-width: 0;
    background: none;
    border: 0;
    outline: 0;
    font-size: 13px;
  }
  .search input::placeholder {
    color: var(--fg-4);
  }
  .clear {
    font-size: 11px;
    color: var(--mint);
    white-space: nowrap;
  }
  .chips {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
    align-items: center;
  }
  .chip {
    font-size: 12px;
    padding: 2px 8px;
    color: var(--fg-2);
    background: var(--bg-2);
    box-shadow: inset 0 0 0 1px var(--line-soft);
  }
  .chip:hover {
    color: var(--fg-1);
  }
  .chip.on {
    color: var(--mint-ink);
    background: var(--mint);
    box-shadow: none;
  }
  .chip.tier {
    font-family: var(--font-term);
    font-weight: 700;
    min-width: 30px;
    color: var(--c);
  }
  .chip.tier.on {
    color: #111;
    background: var(--c);
  }
  .sep {
    width: 1px;
    height: 14px;
    background: var(--line);
    margin: 0 3px;
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
  .bp.core {
    color: var(--fg-2);
  }
  .bp .glyph {
    width: 62%;
    height: 62%;
  }
  .bp:hover {
    color: var(--fg-1);
  }
  .bp.on {
    color: var(--mint-ink);
    background: var(--mint);
    box-shadow: var(--mint-glow);
  }
  .picked {
    font-size: 11px;
    color: var(--mint);
  }
  .ghead {
    display: flex;
    align-items: center;
    gap: 6px;
    margin: 12px 0 6px;
  }
  .ghead span {
    font-size: 10px;
    color: var(--c);
  }
  .ghead i {
    flex: 1;
    height: 1px;
    background: linear-gradient(90deg, var(--c), transparent);
    opacity: 0.4;
  }
  .ghead em {
    font-style: normal;
    font-family: var(--font-term);
    font-size: 11px;
    color: var(--fg-4);
  }
  .grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(78px, 1fr));
    gap: 6px;
  }
  .eq {
    position: relative;
    display: flex;
    flex-direction: column;
    align-items: center;
    padding: 8px 4px 6px;
    background: var(--bg) center / cover, var(--bg-2);
    box-shadow:
      inset 0 0 0 1px #2f3337,
      inset 0 2px 0 var(--c);
  }
  .eq:hover {
    box-shadow:
      inset 0 0 0 1px #5a6066,
      inset 0 2px 0 var(--c);
  }
  .eq.selected {
    box-shadow:
      inset 0 0 0 1.5px var(--fg-1),
      inset 0 2px 0 var(--c);
  }
  .et {
    position: absolute;
    left: 3px;
    top: 5px;
  }
  .eq img {
    width: 56px;
    height: 56px;
    object-fit: contain;
    margin: 6px 0 4px;
  }
  .en {
    font-size: 12px;
    font-weight: 600;
    text-align: center;
    line-height: 1.25;
  }
  .ec {
    font-size: 10px;
    color: var(--fg-4);
  }
  .none {
    color: var(--fg-3);
    text-align: center;
    padding: 30px 0;
  }
</style>
