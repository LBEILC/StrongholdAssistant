<script lang="ts">
  import type { Snippet } from 'svelte'
  import type { Lineup } from '../lib/api'
  import { app, goalProgress } from '../lib/state.svelte'
  import { openMenu, lineupMenu } from '../lib/menu.svelte'
  import BondIcon from './BondIcon.svelte'
  import OpTile from './OpTile.svelte'
  import EqTile from './EqTile.svelte'
  import { asset } from '../lib/assets'

  // 阵容卡：我的阵容、本局可用阵容共用。盟约一行是目标盟约，数字是名单里有这个盟约的人数 / 目标档位人数。
  // 右键整张卡：编辑、设为目标、删除
  interface Props {
    lineup: Lineup
    target?: boolean
    sub?: Snippet
    actions?: Snippet
    /** 点标题（如打开编辑） */
    ontitle?: () => void
    /** 右键弹阵容菜单；导入预览这类还没保存的阵容关掉 */
    menu?: boolean
  }
  let { lineup, target = false, sub, actions, ontitle, menu = true }: Props = $props()
  const title = $derived(lineup.name)
  const members = $derived(lineup.members)
  const bannedHere = $derived(app.bannedIn({ members }))
  const strat = $derived(lineup.strategyId ? app.catalog.strategies.find((s) => s.id === lineup.strategyId) : null)
  const progress = $derived(goalProgress(lineup.goals, members, members.map((m) => m.name)))
</script>

<article class="lc" class:target oncontextmenu={menu ? (e) => openMenu(e, lineupMenu(lineup)) : undefined}>
  <header>
    <div class="ttl">
      {#if ontitle}
        <button class="h3" onclick={ontitle}>{title}</button>
      {:else}
        <span class="h3">{title}</span>
      {/if}
      <p>
        {#if strat}<span class="st"><img src={asset.strategy(strat.id)} alt="" />{strat.name}</span>{/if}
        {#if bannedHere.length}<span class="bad">{bannedHere.length} 名本局禁用</span>{/if}
        {@render sub?.()}
      </p>
    </div>
    {#if target}<span class="badge term">Target</span>{/if}
    {@render actions?.()}
  </header>
  <div class="bonds">
    {#each progress as g (g.covenant.id)}
      {@const inc = app.matchCovenant(g.covenant.id)?.incomplete}
      <span class="bc" class:inc class:short={!g.done}>
        <BondIcon id={g.covenant.id} size={20} on={!inc} />
        <span>{g.covenant.name}<em class="term">{g.level === 2 ? 'II' : 'I'}</em><b>{g.have}/{g.need}</b></span>
      </span>
    {:else}
      <span class="none">没有目标盟约</span>
    {/each}
  </div>
  <div class="ops">
    {#each members as m (m.name)}
      <OpTile
        name={m.name}
        transform={m.transform}
        isKey={m.key}
        size={30}
        showName={false}
        onclick={() => (app.detail = { kind: 'operator', name: m.name })}
      />
    {/each}
    {#if lineup.equipment.length}
      <i class="div"></i>
      {#each lineup.equipment as n (n)}
        <EqTile name={n} size={30} showName={false} onclick={() => (app.detail = { kind: 'equipment', name: n })} />
      {/each}
    {/if}
  </div>
</article>

<style>
  .lc {
    position: relative;
    background: var(--bg-2);
    box-shadow: inset 0 0 0 1px var(--line-soft);
    padding: 9px 10px 10px;
    display: flex;
    flex-direction: column;
    gap: 8px;
  }
  .lc.target {
    background: linear-gradient(90deg, var(--mint-tint), transparent 60%), var(--bg-2);
    box-shadow: inset 0 0 0 1px rgb(34 227 178 / 0.35);
  }
  .lc.target::before {
    content: '';
    position: absolute;
    left: 0;
    top: 0;
    bottom: 0;
    width: 2px;
    background: var(--mint);
    box-shadow: var(--mint-glow);
  }
  header {
    display: flex;
    align-items: center;
    gap: 8px;
  }
  .ttl {
    flex: 1;
    min-width: 0;
  }
  .h3 {
    display: block;
    font-size: 14px;
    font-weight: 700;
    text-align: left;
    max-width: 100%;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  button.h3:hover {
    color: var(--mint-hi);
  }
  p {
    margin: 1px 0 0;
    font-size: 11px;
    color: var(--fg-3);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  p:empty {
    display: none;
  }
  .bad {
    color: var(--red);
    margin-right: 6px;
  }
  .st {
    display: inline-flex;
    align-items: center;
    gap: 3px;
    margin-right: 8px;
    color: var(--fg-2);
    vertical-align: middle;
  }
  .st img {
    width: 14px;
    height: 14px;
    object-fit: cover;
  }
  .badge {
    flex: none;
    font-size: 10px;
    padding: 3px 7px 2px;
    color: var(--mint-ink);
    background: var(--mint);
  }
  .bonds {
    display: flex;
    flex-wrap: wrap;
    gap: 4px 10px;
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
    margin-left: 2px;
    color: var(--mint);
  }
  .bc em {
    font-style: normal;
    font-size: 9px;
    margin-left: 3px;
    color: var(--fg-3);
  }
  .bc.short b {
    color: var(--gold);
  }
  .bc.inc,
  .bc.inc b {
    color: var(--fg-3);
  }
  .none {
    font-size: 11px;
    color: var(--fg-4);
  }
  .ops {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
  }
  .div {
    width: 1px;
    height: 22px;
    align-self: center;
    margin: 0 3px;
    background: var(--line);
  }
  /* 卡片右上角按钮，供调用方的 actions 使用 */
  .lc :global(.act) {
    flex: none;
    font-size: 12px;
    padding: 4px 9px;
    color: var(--fg-2);
    box-shadow: inset 0 0 0 1px var(--line);
    background: var(--bg-3);
    white-space: nowrap;
  }
  .lc :global(.act:hover) {
    color: var(--mint-ink);
    background: var(--mint);
    box-shadow: none;
  }
  .lc :global(.act.quiet) {
    background: none;
    box-shadow: none;
    color: var(--fg-3);
    padding: 4px 4px;
  }
  .lc :global(.act.quiet:hover) {
    color: var(--fg-1);
  }
</style>
