<script lang="ts">
  import { asset } from '../lib/assets'
  import { app, type LineupView, lineupEquipment } from '../lib/state.svelte'
  import { allStages, stageLabel } from '../lib/stages'
  import { customSkill } from '../lib/opconfig'
  import SectionHeader from '../components/SectionHeader.svelte'
  import BondIcon from '../components/BondIcon.svelte'
  import OpTile from '../components/OpTile.svelte'
  import EqTile from '../components/EqTile.svelte'
  import LineupCard from '../components/LineupCard.svelte'
  import Icon from '../components/Icon.svelte'
  import Seg from '../components/Seg.svelte'
  import PlazaView from '../components/PlazaView.svelte'
  import { fly } from 'svelte/transition'
  import { cubicOut } from 'svelte/easing'

  const IMPORT_TIP = '粘贴分享码、森空岛攻略链接，或者从森空岛历史战绩里挑一局'

  // 阵容：目标（目标盟约进度、想抓的干员、还能抓的干员）/ 我的阵容；切换时内容从切换方向滑入
  const views = $derived<{ id: LineupView; label: string; count?: number }[]>([
    { id: 'target', label: '目标' },
    { id: 'mine', label: '我的阵容', count: app.lineups.length },
    { id: 'plaza', label: '广场' },
  ])
  // 切换方向：按分段顺序，往右的从右边滑入
  const order: LineupView[] = ['target', 'mine', 'plaza']
  let prev: LineupView = app.lineupView
  let dir = $state(1)
  $effect.pre(() => {
    const v = app.lineupView
    dir = order.indexOf(v) >= order.indexOf(prev) ? 1 : -1
    prev = v
  })
  const t = $derived(app.target)
  // 当前阶段的样子（本阶段 + 后面阶段的关键干员）；没有阶段时就是整套阵容
  const a = $derived(app.active)
  const stages = $derived(t ? allStages(t) : [])
  const own = $derived(new Set(stages[app.stageIx]?.members.map((m) => m.name) ?? []))
  const round = $derived(app.match.economy?.round ?? null)
  // 「还能抓」默认只露两行，多了点展开
  let allPicks = $state(false)
  const picks = $derived(allPicks ? app.pickable : app.pickable.slice(0, 10))
  const targetEqs = $derived(a ? lineupEquipment(a) : [])
</script>

<div class="page">
  <Seg items={views} value={app.lineupView} onchange={(v) => (app.lineupView = v)} />

  {#key app.lineupView}
  <div class="pane" in:fly={{ x: 24 * dir, duration: 220, easing: cubicOut }}>
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

        <!-- 阶段：默认自动按回合切；点某个阶段固定住，点「自动」恢复 -->
        {#if stages.length > 1}
          <div class="stagesw">
            <button class="auto" class:on={app.session.stage == null} onclick={() => app.setStage(null)} title="按读到的回合自动切换">自动</button>
            {#each stages as s, i (i)}
              <button class:on={app.stageIx === i} onclick={() => app.setStage(i)}>{s.final ? '成型阵容' : stageLabel(s)}</button>
            {/each}
          </div>
          <p class="stagenote">
            {#if app.session.stage != null}已固定在这个阶段，点「自动」按回合切换。
            {:else if round != null}第 {round} 回合 · 自动切到这个阶段。
            {:else}还没读到回合，先按成型阵容；进休整期后自动切换。{/if}
            后面阶段的关键干员也一起追踪。
          </p>
        {/if}

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

        <div class="sub">想抓的干员<em>{a!.members.length} 名 · 点头像切换追踪，暗的是没在追踪；左上角金色是关键</em></div>
        <div class="ops">
          {#each a!.members as m (m.name)}
            {@const pe = m.equipment?.filter((x) => x.priority) ?? []}
            <span class="mw" class:later={!own.has(m.name)} title={own.has(m.name) ? undefined : '后面阶段的关键干员，提前追踪'}>
              <OpTile name={m.name} transform={m.transform} isKey={m.key} elite={!!m.elite} skill={customSkill(m, app.op(m.name), app.settings.operatorDefaults)} size={54} dim={!app.tracked.has(m.name)} onclick={() => app.toggleTracked(m.name)} />
              {#if pe.length}
                <span class="pe" title="优先装备：{pe.map((x) => x.name).join('、')}">
                  {#each pe.slice(0, 3) as x (x.name)}<img src={asset.equipment(x.name)} alt="" />{/each}
                </span>
              {/if}
            </span>
          {:else}
            <p class="none">还没有想抓的干员。</p>
          {/each}
        </div>

        <div class="sub">装备<em>{targetEqs.length} 件 · 点一下切换追踪；金框是干员的优先装备，商店里按关键标记</em></div>
        <div class="ops">
          {#each targetEqs as n (n)}
            <span class="ew" class:pri={app.priorityEq.has(n)}><EqTile name={n} size={48} dim={!app.trackedEq.has(n)} onclick={() => app.toggleTrackedEq(n)} /></span>
          {:else}
            <p class="none wide">还没有。在「编辑」里加，或在装备详情、右键菜单里「加入目标」。</p>
          {/each}
        </div>

        {#if a!.goals.length}
          <div class="sub">还能抓<em>目标盟约里没被禁的其他干员 · 点开可加入目标或追踪</em></div>
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
          <button class="secondary" onclick={() => (app.importOpen = true)} title={IMPORT_TIP}><Icon name="paste" size={14} />导入</button>
        </div>
        <p class="sk">导入可以粘贴分享码、森空岛攻略链接，<br />或者从森空岛历史战绩里挑一局。</p>
      </div>
    {/if}
  {:else if app.lineupView === 'mine'}
    <section>
      <SectionHeader title="我的阵容" tag="{app.lineups.length} 套" en="SAVED LINEUPS">
        {#snippet icon()}
          <Icon name="grid" size={18} />
        {/snippet}
        {#snippet right()}
          <span class="hacts">
            <button class="ghost" onclick={() => (app.importOpen = true)} title={IMPORT_TIP}><Icon name="paste" size={14} />导入</button>
            <button class="ghost" onclick={() => app.edit(null)}><Icon name="add" size={14} />新建</button>
          </span>
        {/snippet}
      </SectionHeader>
      <p class="hint">点「导入」可以粘贴分享码、森空岛攻略链接，或者从森空岛历史战绩里挑一局。右键阵容卡也能编辑、复制分享码、设为目标、删除；目标盟约本局阵容不完整、名单里被禁的多的排在后面。</p>
      {#if !app.lineups.length}
        <div class="empty">
          <p>还没有阵容。</p>
          <div class="row">
            <button class="primary" onclick={() => app.edit(null)}><Icon name="add" size={15} />新建阵容</button>
            <button class="secondary" onclick={() => (app.importOpen = true)} title={IMPORT_TIP}><Icon name="paste" size={14} />导入</button>
          </div>
          <p class="sk">导入可以粘贴分享码、森空岛攻略链接，<br />或者从森空岛历史战绩里挑一局。</p>
        </div>
      {/if}
      <div class="list">
        {#each [...app.lineups].sort((a, b) => app.lineupPenalty(a) - app.lineupPenalty(b)) as l (l.id)}
          {@const isT = app.session.targetLineupId === l.id}
          <LineupCard lineup={l} target={isT} ontitle={() => app.edit(l)}>
            {#snippet sub()}{#if l.note}<span class="note">{l.note}</span>{/if}{/snippet}
            {#snippet actions()}
              <button class="act quiet" onclick={() => app.edit(l)}><span class="ei"><Icon name="pencil" size={13} /></span>编辑</button>
              {#if !isT}<button class="act" onclick={() => app.setTarget(l.id)}>设为目标</button>{/if}
            {/snippet}
          </LineupCard>
        {/each}
      </div>
    </section>
  {:else if app.lineupView === 'plaza'}
    <PlazaView />
  {/if}
  </div>
  {/key}
</div>

<style>
  .mw {
    position: relative;
    display: inline-flex;
  }
  .pe {
    position: absolute;
    right: -3px;
    bottom: -3px;
    display: flex;
    gap: 1px;
    padding: 1px;
    background: var(--bg-0);
    box-shadow: inset 0 0 0 1px var(--gold);
    pointer-events: none;
  }
  .pe img {
    width: 15px;
    height: 15px;
    object-fit: contain;
  }
  .ew {
    display: inline-flex;
  }
  .ew.pri {
    outline: 1px solid var(--gold);
    outline-offset: 1px;
  }
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
  .pane {
    display: flex;
    flex-direction: column;
    gap: 12px;
  }
  .hacts {
    display: flex;
  }
  .ghost {
    display: flex;
    align-items: center;
    gap: 2px;
    font-size: var(--fs-base);
    color: var(--fg-3);
    padding: 3px 6px;
    white-space: nowrap;
  }
  .ghost:hover {
    color: var(--fg-1);
  }
  .hint {
    margin: 8px 2px;
    font-size: var(--fs-sm);
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
    font-size: var(--fs-md);
    min-width: 4em;
  }
  /* 格子等距；到某一档激活人数的那一格高一点（以前用加宽间隔标档位，各盟约档位不同，看起来间隔乱） */
  .ticks {
    flex: 1;
    display: flex;
    align-items: flex-end;
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
    height: 11px;
  }
  .brow.done .cnt em {
    color: var(--mint);
  }
  .lvl {
    font-size: var(--fs-2xs);
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
    font-size: var(--fs-base);
    font-weight: 700;
    color: var(--fg-2);
  }
  .sub em {
    font-style: normal;
    font-weight: 400;
    font-size: var(--fs-sm);
    color: var(--fg-4);
  }
  .more {
    align-self: center;
    margin-top: 8px;
    font-size: var(--fs-base);
    color: var(--mint);
  }
  .none {
    margin: 0 2px;
    font-size: var(--fs-base);
    color: var(--fg-4);
  }
  .none.wide {
    grid-column: 1 / -1;
    justify-self: start;
  }
  .ei {
    display: inline-grid;
    vertical-align: -2px;
    margin-right: 2px;
  }
  .cnt {
    font-family: var(--font-term);
    font-size: var(--fs-sm);
    color: var(--fg-3);
  }
  .cnt em {
    font-style: normal;
    font-size: var(--fs-md);
    color: var(--fg-1);
  }
  .active .cnt em {
    color: var(--mint);
  }
  .eff {
    font-size: var(--fs-base);
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
  .empty .sk {
    margin: 10px 0 0;
    font-size: var(--fs-sm);
    color: var(--fg-4);
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
    font-size: var(--fs-md);
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
  /* 宽（工作台）：阵容卡两三列 */
  @container (min-width: 720px) {
    .list {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(340px, 1fr));
      /* 同一行的卡片一样高（以前顶对齐，高矮不齐） */
      align-items: stretch;
      gap: 8px;
    }
  }
  .stagesw {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
  }
  .stagesw button {
    height: 26px;
    padding: 0 10px;
    font-size: var(--fs-base);
    color: var(--fg-2);
    background: var(--bg-2);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .stagesw button.on {
    color: var(--fg-1);
    background: var(--mint-tint);
    box-shadow: inset 0 0 0 1px var(--mint);
  }
  .stagesw .auto.on {
    color: var(--mint-ink);
    background: var(--mint);
  }
  .stagenote {
    margin: 0;
    font-size: var(--fs-sm);
    color: var(--fg-3);
  }
  /* 后面阶段的关键干员：右下角小标 */
  .mw.later::after {
    content: '后';
    position: absolute;
    right: 0;
    bottom: 16px;
    padding: 0 3px;
    font-size: var(--fs-2xs);
    color: var(--gold-ink);
    background: var(--gold);
  }
</style>
