<script lang="ts">
  import { app, type LineupView } from '../lib/state.svelte'
  import SectionHeader from '../components/SectionHeader.svelte'
  import BondIcon from '../components/BondIcon.svelte'
  import OpTile from '../components/OpTile.svelte'
  import LineupCard from '../components/LineupCard.svelte'
  import Icon from '../components/Icon.svelte'

  // 阵容：目标（目标盟约进度、想抓的干员、还能抓的干员）/ 我的阵容
  const views: { id: LineupView; label: string }[] = [
    { id: 'target', label: '目标' },
    { id: 'mine', label: '我的阵容' },
  ]
  const t = $derived(app.target)
  // 「还能抓」默认只露两行，多了点展开
  let allPicks = $state(false)
  const picks = $derived(allPicks ? app.pickable : app.pickable.slice(0, 10))
</script>

<div class="page">
  <div class="views">
    {#each views as v (v.id)}
      <button class:on={app.lineupView === v.id} onclick={() => (app.lineupView = v.id)}>
        {v.label}
        {#if v.id === 'mine'}<em>{app.lineups.length}</em>{/if}
      </button>
    {/each}
  </div>

  {#if app.lineupView === 'target'}
    {#if t}
      <section>
        <SectionHeader title={t.name} en="CURRENT TARGET">
          {#snippet icon()}
            <Icon name="focus" size={18} />
          {/snippet}
          {#snippet right()}
            <span class="hacts">
              <button class="ghost" onclick={() => app.edit(t)}><Icon name="pencil" size={14} />编辑</button>
              <button class="ghost" onclick={() => app.setTarget(null)}>取消目标</button>
            </span>
          {/snippet}
        </SectionHeader>

        <div class="bondlist">
          {#each app.goalProgress as g (g.covenant.id)}
            {@const eff = g.covenant.effects[0]}
            <button class="brow" class:active={g.active} class:done={g.done} onclick={() => (app.detail = { kind: 'covenant', id: g.covenant.id })}>
              <BondIcon id={g.covenant.id} size={30} on={g.active} incomplete={app.matchCovenant(g.covenant.id)?.incomplete} />
              <span class="bmain">
                <span class="bt">
                  <b>{g.covenant.name}</b>
                  <span class="lvl term">{g.level === 2 ? 'II' : 'I'}</span>
                  <span class="ticks">
                    {#each Array(Math.max(g.have, g.need)) as _, i}
                      <i class:have={i < g.have} class:th={g.covenant.thresholds.includes(i + 1)}></i>
                    {/each}
                  </span>
                  <span class="cnt"><em>{g.have}</em>/{g.need}</span>
                </span>
                {#if eff}<span class="eff" class:off={!g.active}>{eff.text}</span>{/if}
              </span>
            </button>
          {:else}
            <p class="none">还没有目标盟约，点「编辑」选。</p>
          {/each}
        </div>

        <div class="sub">想抓的干员<em>{t.members.length} 名 · 点头像标记已持有，左上角金色是关键</em></div>
        <div class="ops">
          {#each t.members as m (m.name)}
            <OpTile name={m.name} transform={m.transform} isKey={m.key} size={54} owned={app.owned.has(m.name)} dim={!app.owned.has(m.name)} onclick={() => app.toggleOwned(m.name)} />
          {:else}
            <p class="none">还没有想抓的干员。</p>
          {/each}
        </div>

        {#if t.goals.length}
          <div class="sub">还能抓<em>目标盟约里没被禁、没持有的其他干员 · 点开可加入目标</em></div>
          <div class="ops">
            {#each picks as o (o.name)}
              <OpTile name={o.name} size={48} onclick={() => (app.detail = { kind: 'operator', name: o.name })} />
            {:else}
              <p class="none">没有了。</p>
            {/each}
          </div>
          {#if app.pickable.length > 10}
            <button class="more" onclick={() => (allPicks = !allPicks)}>{allPicks ? '收起' : `展开全部 ${app.pickable.length} 名`}</button>
          {/if}
        {/if}
      </section>
    {:else}
      <div class="empty">
        <p>还没有目标阵容。</p>
        <div class="row">
          <button class="primary" onclick={() => app.edit(null)}><Icon name="add" size={15} />新建阵容</button>
          <button class="secondary" onclick={() => (app.lineupView = 'mine')}>从我的阵容选</button>
        </div>
      </div>
    {/if}
  {:else}
    <section>
      <SectionHeader title="我的阵容" tag="{app.lineups.length} 套" en="SAVED LINEUPS">
        {#snippet icon()}
          <Icon name="grid" size={18} />
        {/snippet}
        {#snippet right()}
          <button class="ghost" onclick={() => app.edit(null)}><Icon name="add" size={14} />新建</button>
        {/snippet}
      </SectionHeader>
      <p class="hint">点阵容名编辑。目标盟约本局阵容不完整、名单里被禁的多的排在后面。</p>
      <div class="list">
        {#each [...app.lineups].sort((a, b) => app.lineupPenalty(a) - app.lineupPenalty(b)) as l (l.id)}
          {@const isT = app.session.targetLineupId === l.id}
          <LineupCard title={l.name} goals={l.goals} members={l.members} target={isT} ontitle={() => app.edit(l)}>
            {#snippet sub()}{#if l.note}<span class="note">{l.note}</span>{/if}{/snippet}
            {#snippet actions()}
              {#if !isT}<button class="act" onclick={() => app.setTarget(l.id)}>设为目标</button>{/if}
            {/snippet}
          </LineupCard>
        {/each}
      </div>
    </section>
  {/if}
</div>

<style>
  .page {
    display: flex;
    flex-direction: column;
    gap: 12px;
    padding: 10px 10px 24px;
  }
  section {
    display: flex;
    flex-direction: column;
  }
  .views {
    display: grid;
    grid-template-columns: repeat(2, 1fr);
    background: var(--bg-2);
    padding: 2px;
  }
  .views button {
    height: 28px;
    font-size: 13px;
    font-weight: 600;
    color: var(--fg-3);
  }
  .views button em {
    font-style: normal;
    font-family: var(--font-term);
    font-size: 11px;
    margin-left: 3px;
    color: var(--fg-4);
  }
  .views button.on {
    color: var(--fg-1);
    background: var(--bg-4);
    box-shadow: inset 0 -2px 0 var(--mint);
  }
  .views button.on em {
    color: var(--mint);
  }
  .hacts {
    display: flex;
  }
  .ghost {
    display: flex;
    align-items: center;
    gap: 2px;
    font-size: 12px;
    color: var(--fg-3);
    padding: 3px 6px;
    white-space: nowrap;
  }
  .ghost:hover {
    color: var(--fg-1);
  }
  .hint {
    margin: 8px 2px;
    font-size: 11px;
    color: var(--fg-4);
  }
  .ops {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(58px, 1fr));
    gap: 8px 4px;
    justify-items: center;
  }
  .bondlist {
    display: flex;
    flex-direction: column;
    gap: 2px;
    margin-top: 12px;
  }
  .brow {
    display: flex;
    gap: 10px;
    align-items: flex-start;
    text-align: left;
    padding: 7px 8px;
    background: var(--bg-1);
  }
  .brow:hover {
    background: var(--bg-2);
  }
  .bmain {
    flex: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 3px;
  }
  .bt {
    display: flex;
    align-items: center;
    gap: 8px;
  }
  .bt b {
    font-size: 13px;
    min-width: 4em;
  }
  .ticks {
    flex: 1;
    display: flex;
    gap: 2px;
  }
  .ticks i {
    width: 9px;
    height: 7px;
    background: var(--bg-4);
  }
  .ticks i.have {
    background: var(--mint);
  }
  .ticks i.th {
    margin-right: 5px;
  }
  .brow.done .cnt em {
    color: var(--mint);
  }
  .lvl {
    font-size: 9px;
    padding: 1px 4px 0;
    color: var(--gold-ink);
    background: var(--gold);
  }
  .sub {
    display: flex;
    flex-wrap: wrap;
    align-items: baseline;
    gap: 2px 6px;
    margin: 14px 2px 6px;
    font-size: 12px;
    font-weight: 700;
    color: var(--fg-2);
  }
  .sub em {
    font-style: normal;
    font-weight: 400;
    font-size: 11px;
    color: var(--fg-4);
  }
  .more {
    align-self: center;
    margin-top: 8px;
    font-size: 12px;
    color: var(--mint);
  }
  .none {
    margin: 0 2px;
    font-size: 12px;
    color: var(--fg-4);
  }
  .cnt {
    font-family: var(--font-term);
    font-size: 11px;
    color: var(--fg-3);
  }
  .cnt em {
    font-style: normal;
    font-size: 13px;
    color: var(--fg-1);
  }
  .active .cnt em {
    color: var(--mint);
  }
  .eff {
    font-size: 11.5px;
    color: var(--fg-2);
    line-height: 1.4;
  }
  .eff.off {
    color: var(--fg-4);
  }
  .empty {
    padding: 28px 8px;
    text-align: center;
    color: var(--fg-3);
  }
  .empty .row {
    display: flex;
    justify-content: center;
    gap: 8px;
  }
  .primary,
  .secondary {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: 5px;
    height: 32px;
    padding: 0 14px;
    font-size: 13px;
    font-weight: 700;
  }
  .primary {
    color: var(--mint-ink);
    background: var(--mint);
  }
  .primary:hover {
    background: var(--mint-hi);
  }
  .primary:disabled {
    color: var(--fg-4);
    background: var(--bg-3);
    cursor: default;
  }
  .secondary {
    color: var(--fg-1);
    background: var(--bg-4);
  }
  .list {
    display: flex;
    flex-direction: column;
    gap: 6px;
    margin-top: 8px;
  }
  .note {
    color: var(--gold);
    margin-left: 6px;
  }
</style>
