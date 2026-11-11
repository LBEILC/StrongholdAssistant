<script lang="ts">
  import type { Snippet } from 'svelte'
  import type { CovenantGoal, LineupMember } from '../lib/api'
  import { app, goalProgress } from '../lib/state.svelte'
  import BondIcon from './BondIcon.svelte'
  import OpTile from './OpTile.svelte'

  // 阵容卡：我的阵容、本局可用阵容共用。盟约一行是目标盟约，数字是名单里有这个盟约的人数 / 目标档位人数
  interface Props {
    title: string
    goals: CovenantGoal[]
    members: LineupMember[]
    target?: boolean
    sub?: Snippet
    actions?: Snippet
    /** 点标题（如打开编辑） */
    ontitle?: () => void
  }
  let { title, goals, members, target = false, sub, actions, ontitle }: Props = $props()
  const bannedHere = $derived(app.bannedIn({ members }))
  const progress = $derived(goalProgress(goals, members, members.map((m) => m.name)))
</script>

<article class="lc" class:target>
  <header>
    <div class="ttl">
      {#if ontitle}
        <button class="h3" onclick={ontitle}>{title}</button>
      {:else}
        <span class="h3">{title}</span>
      {/if}
      <p>
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
        owned={target && app.owned.has(m.name)}
        onclick={() => (app.detail = { kind: 'operator', name: m.name })}
      />
    {/each}
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
