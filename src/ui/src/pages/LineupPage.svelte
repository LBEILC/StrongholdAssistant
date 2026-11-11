<script lang="ts">
  import { app, type LineupView } from '../lib/state.svelte'
  import { asset } from '../lib/assets'
  import SectionHeader from '../components/SectionHeader.svelte'
  import BondIcon from '../components/BondIcon.svelte'
  import OpTile from '../components/OpTile.svelte'
  import LineupCard from '../components/LineupCard.svelte'
  import Icon from '../components/Icon.svelte'

  // 阵容：目标（持有情况与盟约效果）/ 规划（选目标盟约生成候选）/ 我的阵容
  const views: { id: LineupView; label: string }[] = [
    { id: 'target', label: '目标' },
    { id: 'plan', label: '规划' },
    { id: 'mine', label: '我的阵容' },
  ]
  const t = $derived(app.target)
  const goalCount = $derived(Object.keys(app.goals).length)
  const core = $derived(app.catalog.covenants.filter((c) => c.kind === 'core'))
  const extra = $derived(app.catalog.covenants.filter((c) => c.kind === 'extra'))
  const toggle = <T,>(arr: T[], v: T) => (arr.includes(v) ? arr.filter((x) => x !== v) : [...arr, v].sort())
</script>

<div class="page">
  <div class="views">
    {#each views as v (v.id)}
      <button class:on={app.lineupView === v.id} onclick={() => (app.lineupView = v.id)}>
        {v.label}
        {#if v.id === 'mine'}<em>{app.lineups.length}</em>{/if}
        {#if v.id === 'plan' && goalCount}<em>{goalCount}</em>{/if}
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
        <p class="hint">点头像标记「已持有」；顶栏的待购头像也可以直接点。</p>
        <div class="ops">
          {#each t.members as m (m.name)}
            <OpTile name={m.name} transform={m.transform} size={54} owned={app.owned.has(m.name)} dim={!app.owned.has(m.name)} onclick={() => app.toggleOwned(m.name)} />
          {/each}
        </div>
        <div class="bondlist">
          {#each app.targetBonds as b (b.covenant.id)}
            {@const eff = b.covenant.effects[0]}
            <button class="brow" class:active={b.active} onclick={() => (app.detail = { kind: 'covenant', id: b.covenant.id })}>
              <BondIcon id={b.covenant.id} size={30} on={b.active} incomplete={app.matchCovenant(b.covenant.id)?.incomplete} />
              <span class="bmain">
                <span class="bt">
                  <b>{b.covenant.name}</b>
                  <span class="ticks">
                    {#each Array(Math.max(b.planned, b.covenant.thresholds.at(-1)!)) as _, i}
                      <i class:have={i < b.owned} class:plan={i >= b.owned && i < b.planned} class:th={b.covenant.thresholds.includes(i + 1)}></i>
                    {/each}
                  </span>
                  <span class="cnt"><em>{b.owned}</em>/{b.planned}</span>
                </span>
                {#if eff}<span class="eff" class:off={!b.active}>{eff.text}</span>{/if}
              </span>
            </button>
          {/each}
        </div>
      </section>
    {:else}
      <div class="empty">
        <p>还没有目标阵容。</p>
        <div class="row">
          <button class="primary" onclick={() => (app.lineupView = 'plan')}><Icon name="magic" size={15} />按本局规划</button>
          <button class="secondary" onclick={() => (app.lineupView = 'mine')}>从我的阵容选</button>
        </div>
      </div>
    {/if}
  {:else if app.lineupView === 'plan'}
    <section>
      <SectionHeader title="目标盟约" tag={goalCount ? `${goalCount} 个` : undefined} en="LINEUP PLANNER">
        {#snippet icon()}
          <Icon name="magic" size={18} />
        {/snippet}
        {#snippet right()}
          {#if goalCount}<button class="ghost" onclick={() => (app.goals = {})}>清空</button>{/if}
        {/snippet}
      </SectionHeader>
      <p class="hint">点一次选 I 档，再点选 II 档，再点取消。本局阵容不完整的核心盟约不能选。</p>
      {#each [core, extra] as group, gi (gi)}
        <div class="goals">
          {#each group as c (c.id)}
            {@const lv = app.goals[c.id]}
            {@const blocked = c.kind === 'core' && app.matchCovenant(c.id)?.incomplete}
            <span class="goal" class:blocked>
              <BondIcon id={c.id} size={36} on={!!lv} incomplete={app.matchCovenant(c.id)?.incomplete} name onclick={blocked ? undefined : () => app.cycleGoal(c.id)} />
              {#if lv}<span class="lv term">{lv === 2 ? 'II' : 'I'}·{c.thresholds[lv - 1]}</span>{/if}
            </span>
          {/each}
        </div>
      {/each}

      <div class="opts">
        <div class="opt">
          <span>锁定干员</span>
          <span class="locked">
            {#each app.locked as n (n)}
              <button class="lk" onclick={() => app.toggleLocked(n)} title="取消锁定">
                <img src={asset.operator(n)} alt="" />{n}<Icon name="close" size={12} />
              </button>
            {:else}
              <em>在干员详情里「规划时锁定」</em>
            {/each}
          </span>
        </div>
        <div class="opt">
          <span>人数</span>
          {#each [8, 9] as const as p (p)}
            <button class="chip" class:on={app.people.includes(p)} onclick={() => (app.people = toggle(app.people, p))}>{p} 人</button>
          {/each}
          <span class="sp"></span>
          <span>转职</span>
          {#each [0, 1, 2] as const as n (n)}
            <button class="chip" class:on={app.transforms.includes(n)} onclick={() => (app.transforms = toggle(app.transforms, n))}>{n}</button>
          {/each}
        </div>
      </div>
      <button class="primary wide" disabled={!goalCount || app.planning || !app.people.length || !app.transforms.length} onclick={() => app.runPlan()}>
        <Icon name="magic" size={15} />{app.planning ? '规划中…' : '生成候选阵容'}
      </button>
    </section>

    {#if app.planResult}
      <section>
        <SectionHeader title="候选阵容" tag={app.planResult.candidates.length ? `${app.planResult.candidates.length} 套` : undefined} en="CANDIDATES">
          {#snippet icon()}
            <Icon name="grid" size={18} />
          {/snippet}
        </SectionHeader>
        {#if app.planResult.error}
          <p class="err">{app.planResult.error}</p>
        {/if}
        <div class="list">
          {#each app.planResult.candidates as c, i (i)}
            <LineupCard title="{c.people} 人 · 转职 {c.transforms}" members={c.members}>
              {#snippet sub()}{c.freeSlots ? `空 ${c.freeSlots} 位可自由补` : '满编'}{/snippet}
              {#snippet actions()}
                <button class="act quiet" onclick={() => app.adopt(c, false)}>存为阵容</button>
                <button class="act" onclick={() => app.adopt(c, true)}>设为目标</button>
              {/snippet}
            </LineupCard>
          {/each}
        </div>
      </section>
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
      <p class="hint">点阵容名编辑。按本局被禁用的人数排序。</p>
      <div class="list">
        {#each [...app.lineups].sort((a, b) => app.bannedIn(a).length - app.bannedIn(b).length) as l (l.id)}
          {@const isT = app.session.targetLineupId === l.id}
          <LineupCard title={l.name} members={l.members} target={isT} ontitle={() => app.edit(l)}>
            {#snippet sub()}{l.source === 'planned' ? '规划生成' : ''}{#if l.note}<span class="note">{l.note}</span>{/if}{/snippet}
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
    grid-template-columns: repeat(3, 1fr);
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
  .ticks i.plan {
    background: rgb(34 227 178 / 0.22);
  }
  .ticks i.have {
    background: var(--mint);
  }
  .ticks i.th {
    margin-right: 5px;
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
  .wide {
    width: 100%;
    margin-top: 12px;
  }
  .goals {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(56px, 1fr));
    gap: 10px 2px;
    justify-items: center;
    padding: 6px 0 10px;
  }
  .goals + .goals {
    border-top: 1px solid var(--line-soft);
    padding-top: 12px;
  }
  .goal {
    position: relative;
    display: flex;
    justify-content: center;
  }
  .goal.blocked {
    opacity: 0.35;
  }
  .lv {
    position: absolute;
    top: 26px;
    left: 50%;
    transform: translateX(-50%);
    font-size: 9px;
    line-height: 1;
    padding: 2px 4px 1px;
    color: var(--gold-ink);
    background: var(--gold);
    white-space: nowrap;
    pointer-events: none;
  }
  .opts {
    display: flex;
    flex-direction: column;
    gap: 8px;
    padding: 10px 8px;
    background: var(--bg-1);
    font-size: 12px;
    color: var(--fg-3);
  }
  .opt {
    display: flex;
    align-items: center;
    gap: 5px;
    flex-wrap: wrap;
  }
  .opt > span:first-child {
    width: 4.5em;
  }
  .sp {
    width: 10px;
  }
  .locked {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
    flex: 1;
  }
  .locked em {
    font-style: normal;
    color: var(--fg-4);
  }
  .lk {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    padding: 1px 5px 1px 1px;
    font-size: 12px;
    color: var(--fg-1);
    background: var(--bg-3);
  }
  .lk img {
    width: 20px;
    height: 20px;
    object-fit: cover;
  }
  .lk:hover {
    background: var(--bg-4);
  }
  .chip {
    font-size: 12px;
    min-width: 28px;
    padding: 2px 8px;
    color: var(--fg-2);
    background: var(--bg-3);
  }
  .chip.on {
    color: var(--mint-ink);
    background: var(--mint);
  }
  .err {
    margin: 10px 2px 0;
    font-size: 12px;
    color: #f2b8ba;
    padding: 8px;
    background: var(--red-tint);
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
