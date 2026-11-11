<script lang="ts">
  import type { Strategy } from '../lib/api'
  import { app } from '../lib/state.svelte'
  import { asset } from '../lib/assets'
  import Icon from './Icon.svelte'
  import GameText from './GameText.svelte'

  // 策略网格，仿游戏「策略报告」：上面是当前看的那个策略的详情，下面按「泛用型 / 盟约型」分组排头像。
  // 编辑器里点头像就是选上（再点取消）；图鉴里点头像只是看详情
  interface Props {
    /** 已选的策略（编辑器）；图鉴不传 */
    selected?: string | null
    /** 本局策略：头像上标「本局」 */
    current?: string | null
    /** 搜索词：按名字、发起者、效果过滤 */
    q?: string
    onpick?: (id: string) => void
  }
  let { selected = null, current = null, q = '', onpick }: Props = $props()

  // 盟约型 = 效果里点名了某个核心盟约（国家）；当期 17 个，和游戏里的分组一致
  const cores = app.catalog.covenants.filter((c) => c.kind === 'core')
  const covOf = (s: Strategy) => cores.find((c) => s.description.includes(`【${c.name}】`))?.id ?? null
  const all = app.catalog.strategies.map((s) => ({ s, cov: covOf(s) }))
  const shown = $derived(all.filter(({ s }) => !q || s.name.includes(q) || s.initiator.includes(q) || s.description.includes(q)))
  const groups = $derived([
    { title: '泛用型策略', items: shown.filter((x) => !x.cov) },
    { title: '盟约型策略', items: shown.filter((x) => x.cov) },
  ].filter((g) => g.items.length))

  let focus = $state<string | null>(null)
  const viewing = $derived(app.catalog.strategies.find((s) => s.id === (focus ?? selected ?? current)) ?? null)
  const pick = (id: string) => {
    focus = id
    onpick?.(id)
  }
</script>

<div class="sg">
  {#if viewing}
    <div class="detail" class:on={selected === viewing.id}>
      <img src={asset.strategy(viewing.id)} alt="" />
      <div class="dt">
        <div class="dn">
          <b>{viewing.name}</b><span>{viewing.initiator}</span>
          {#if viewing.id === current}<em class="term now">本局</em>{/if}
          {#if onpick && selected === viewing.id}<em class="sel"><Icon name="check" size={11} />已选</em>{/if}
        </div>
        <p><GameText text={viewing.description} /></p>
      </div>
    </div>
  {:else}
    <p class="empty">{onpick ? '点头像选一个策略，可以不选。' : '点头像看策略效果。'}</p>
  {/if}

  {#each groups as g (g.title)}
    <div class="gh"><i></i><span>{g.title}</span><i></i></div>
    <div class="grid">
      {#each g.items as { s, cov } (s.id)}
        <button
          class="cell"
          class:sel={selected === s.id}
          class:focus={viewing?.id === s.id}
          title="{s.name} · {s.initiator}"
          onclick={() => pick(s.id)}
        >
          <img src={asset.strategy(s.id)} alt={s.name} loading="lazy" />
          {#if cov}<span class="cv"><span class="glyph" style:--src="url({asset.bond(cov)})"></span></span>{/if}
          {#if s.id === current}<span class="now term">本局</span>{/if}
          {#if selected === s.id}<span class="ck"><Icon name="check" size={11} /></span>{/if}
        </button>
      {/each}
    </div>
  {:else}
    <p class="empty">没有符合条件的策略。</p>
  {/each}
</div>

<style>
  .sg {
    display: flex;
    flex-direction: column;
    gap: 8px;
  }
  .detail {
    position: sticky;
    top: var(--sticky-top, 0px);
    z-index: 1;
    display: flex;
    gap: 10px;
    align-items: flex-start;
    padding: 8px;
    background: var(--hatch), #15171a;
    box-shadow:
      inset 0 0 0 1px var(--line-soft),
      0 6px 10px rgb(0 0 0 / 0.45);
  }
  .detail.on {
    background: linear-gradient(90deg, var(--mint-tint), transparent 70%), #15171a;
    box-shadow: inset 2px 0 0 var(--mint), inset 0 0 0 1px rgb(34 227 178 / 0.3);
  }
  .detail img {
    flex: none;
    width: 52px;
    height: 52px;
    object-fit: cover;
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .dt {
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 3px;
  }
  .dn {
    display: flex;
    flex-wrap: wrap;
    align-items: baseline;
    gap: 2px 6px;
  }
  .dn b {
    font-size: 14px;
  }
  .dn span {
    font-size: 11px;
    color: var(--fg-3);
  }
  .dn em {
    font-style: normal;
    font-size: 10px;
    padding: 1px 5px;
  }
  .dn .now {
    color: var(--mint-ink);
    background: var(--mint);
  }
  .dn .sel {
    display: inline-flex;
    align-items: center;
    gap: 2px;
    color: var(--mint);
    box-shadow: inset 0 0 0 1px var(--mint-lo);
  }
  .dt p {
    margin: 0;
    font-size: 12px;
    line-height: 1.55;
    color: var(--fg-2);
  }
  .empty {
    margin: 0;
    font-size: 11px;
    color: var(--fg-4);
  }
  /* 分组标题：仿游戏的「[ 泛用型策略 ]」 */
  .gh {
    display: flex;
    align-items: center;
    gap: 8px;
    margin-top: 4px;
    font-size: 12px;
    font-weight: 700;
    color: var(--fg-2);
  }
  .gh i {
    flex: 1;
    height: 1px;
    background: linear-gradient(90deg, transparent, var(--line));
  }
  .gh i:last-child {
    background: linear-gradient(90deg, var(--line), transparent);
  }
  .gh span {
    padding: 0 6px;
    box-shadow:
      inset 2px 0 0 var(--fg-4),
      inset -2px 0 0 var(--fg-4);
  }
  .grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(54px, 1fr));
    gap: 4px;
  }
  .cell {
    position: relative;
    aspect-ratio: 1;
    background: var(--bg-3);
    box-shadow: inset 0 0 0 1px var(--line);
    overflow: hidden;
  }
  .cell img {
    width: 100%;
    height: 100%;
    object-fit: cover;
    filter: saturate(0.85);
    transition: filter var(--t-fast);
  }
  .cell:hover img {
    filter: none;
  }
  .cell::after {
    content: '';
    position: absolute;
    inset: 0;
    pointer-events: none;
    transition: box-shadow var(--t-fast);
  }
  .cell.focus::after {
    box-shadow: inset 0 0 0 2px var(--fg-1);
  }
  .cell.sel::after {
    box-shadow:
      inset 0 0 0 2px var(--mint),
      inset 0 0 10px rgb(34 227 178 / 0.45);
  }
  .cv {
    position: absolute;
    right: 2px;
    bottom: 2px;
    width: 16px;
    height: 16px;
    display: grid;
    place-items: center;
    border-radius: 50%;
    color: var(--mint-ink);
    background: var(--mint);
    box-shadow: 0 0 0 1px rgb(0 0 0 / 0.6);
  }
  .cv .glyph {
    width: 11px;
    height: 11px;
  }
  .cell .now {
    position: absolute;
    left: 0;
    top: 0;
    font-size: 9px;
    padding: 1px 3px 0;
    color: var(--mint-ink);
    background: var(--mint);
  }
  .ck {
    position: absolute;
    right: 0;
    top: 0;
    width: 16px;
    height: 16px;
    display: grid;
    place-items: center;
    color: var(--mint-ink);
    background: var(--mint);
  }
</style>
