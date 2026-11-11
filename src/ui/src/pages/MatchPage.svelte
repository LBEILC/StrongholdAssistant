<script lang="ts">
  import { slide } from 'svelte/transition'
  import { cubicOut } from 'svelte/easing'
  import { app } from '../lib/state.svelte'
  import { asset } from '../lib/assets'
  import SectionHeader from '../components/SectionHeader.svelte'
  import BondIcon from '../components/BondIcon.svelte'
  import OpTile from '../components/OpTile.svelte'
  import LineupCard from '../components/LineupCard.svelte'
  import Icon from '../components/Icon.svelte'

  // 本局：开局识别到的信息，排版仿游戏「确认本局信息」页；可手动修正；后面接本局能用的已存阵容
  const m = $derived(app.match)
  const core = $derived(m.covenants.filter((c) => app.cov(c.id).kind === 'core'))
  const extra = $derived(m.covenants.filter((c) => app.cov(c.id).kind === 'extra'))
  const boss = $derived(app.catalog.bosses.find((b) => b.id === m.bossId))
  const strategy = $derived(app.catalog.strategies.find((s) => s.id === m.strategyId))
  // 我的阵容按本局被禁人数排序，取前 3
  const usable = $derived(
    app.lineups
      .map((l) => ({ l, bad: app.bannedIn(l).length }))
      .sort((a, b) => a.bad - b.bad)
      .slice(0, 3),
  )
  const edit = $derived(app.matchEdit)
  const tapCov = (id: string) => (edit ? app.toggleCovenantIncomplete(id) : (app.detail = { kind: 'covenant', id }))

  const ago = $derived.by(() => {
    if (!m.recognizedAt) return ''
    const min = Math.round((Date.now() - new Date(m.recognizedAt).getTime()) / 60000)
    return min < 1 ? '刚刚' : `${min} 分钟前`
  })
  const incompleteCount = (list: typeof core) => list.filter((c) => c.incomplete).length

  // 分组可折叠，状态记在本机；修正时盟约和禁用干员总是展开
  const open = (key: string, forceOpen = false) => forceOpen || !app.folded[`match.${key}`]
  const fold = (key: string) => () => app.toggleFold(`match.${key}`)
  const sl = { duration: 200, easing: cubicOut }

  // 禁用干员仿游戏按盟约分组：先按不完整的核心盟约，再按不完整的附加盟约，剩下的（手动禁用等）归「其他」
  const bannedGroups = $derived.by(() => {
    const left = new Set(m.bannedOperators)
    const groups: { id: string | null; names: string[] }[] = []
    const take = (id: string | null, hit: (o: ReturnType<typeof app.op>) => boolean) => {
      const names = [...left].filter((n) => hit(app.op(n))).sort((a, b) => app.op(b).tier - app.op(a).tier)
      names.forEach((n) => left.delete(n))
      if (names.length) groups.push({ id, names })
    }
    for (const c of core.filter((c) => c.incomplete)) take(c.id, (o) => o.core.includes(c.id))
    for (const c of extra.filter((c) => c.incomplete)) take(c.id, (o) => o.extra.includes(c.id))
    take(null, () => true)
    return groups
  })
</script>

<div class="page">
  <div class="status">
    <span class="st" class:ok={m.source !== 'none'}>
      <span class="term">Info</span>
      {m.source === 'recognized' ? '已识别' : m.source === 'manual' ? '已修正' : '未识别'}
      <em>{ago}</em>
    </span>
    <span class="tools">
      <button class="tool" class:on={edit} onclick={() => (app.matchEdit = !edit)}>
        <Icon name="pencil" size={13} />{edit ? '完成' : '修正'}
      </button>
      <button class="tool" class:busy={app.scanning} onclick={() => app.rescan()} disabled={app.scanning}>
        <span class="spin"><Icon name="refresh" size={13} /></span>
        {app.scanning ? '识别中…' : '重新识别'}
      </button>
    </span>
  </div>
  {#if app.scanNote?.message}
    <div class="note" class:bad={!app.scanNote.ok}>{app.scanNote.message}</div>
  {/if}
  {#if edit}
    <div class="edithint">点盟约切换「可用 / 阵容不完整」，禁用干员会跟着重新推算；点禁用干员可解除。要单独禁用某名干员，在干员详情里操作。</div>
  {/if}

  {#if strategy}
    <div class="strategy">
      <img src={asset.strategy(strategy.id)} alt="" />
      <div>
        <div class="sn"><b>{strategy.name}</b><span>{strategy.initiator}</span></div>
        <p>{strategy.description}</p>
      </div>
    </div>
  {/if}

  <section>
    <SectionHeader
      title="核心盟约"
      tag={incompleteCount(core) ? `${incompleteCount(core)} 个阵容不完整` : undefined}
      ontoggle={edit ? undefined : fold('core')}
      folded={!open('core', edit)}
    >
      {#snippet icon()}
        <img src={asset.stage('img_title_icon_temp_bond')} alt="" class="ti" />
      {/snippet}
    </SectionHeader>
    {#if open('core', edit)}
      <div class="grid" class:editing={edit} transition:slide={sl}>
        {#each core as c (c.id)}
          <BondIcon id={c.id} size={42} on={!c.incomplete} incomplete={c.incomplete} name onclick={() => tapCov(c.id)} />
        {/each}
      </div>
    {/if}
  </section>

  <section>
    <SectionHeader
      title="附加盟约"
      tag={incompleteCount(extra) ? `${incompleteCount(extra)} 个阵容不完整` : undefined}
      ontoggle={edit ? undefined : fold('extra')}
      folded={!open('extra', edit)}
    >
      {#snippet icon()}
        <img src={asset.stage('img_title_icon_perm_bond')} alt="" class="ti" />
      {/snippet}
    </SectionHeader>
    {#if open('extra', edit)}
      <div class="grid" class:editing={edit} transition:slide={sl}>
        {#each extra as c (c.id)}
          <BondIcon id={c.id} size={42} on={!c.incomplete} incomplete={c.incomplete} name onclick={() => tapCov(c.id)} />
        {/each}
      </div>
    {/if}
  </section>

  <section>
    <SectionHeader
      title="本局禁用干员"
      tag="{m.bannedOperators.length} 名"
      tagTone="warn"
      ontoggle={edit ? undefined : fold('banned')}
      folded={!open('banned', edit)}
    >
      {#snippet icon()}
        <img src={asset.stage('img_title_icon_chess')} alt="" class="ti" />
      {/snippet}
    </SectionHeader>
    {#if open('banned', edit)}
      <div class="banned" class:editing={edit} transition:slide={sl}>
        {#each bannedGroups as g (g.id ?? '')}
          <div class="bg">
            <div class="bgh">
              {#if g.id}
                <BondIcon id={g.id} size={20} />
                <b>{app.cov(g.id).name}</b>
              {:else}
                <b>其他</b>
              {/if}
              <span>{g.names.length}</span>
            </div>
            <div class="tiles">
              {#each g.names as n (n)}
                <OpTile name={n} size={42} onclick={() => (edit ? app.toggleBanned(n) : (app.detail = { kind: 'operator', name: n }))} />
              {/each}
            </div>
          </div>
        {:else}
          <p class="none">没有禁用干员</p>
        {/each}
      </div>
    {/if}
  </section>

  <section>
    <SectionHeader title="敌方情报" en="HOSTILE INTEL" ontoggle={fold('enemy')} folded={!open('enemy')}>
      {#snippet icon()}
        <img src={asset.stage('img_title_icon_enemy')} alt="" class="ti" />
      {/snippet}
    </SectionHeader>
    {#if open('enemy')}
    <div class="enemy" transition:slide={sl}>
      {#if boss}
        <button class="boss" onclick={() => (app.detail = { kind: 'boss', id: boss.id })}>
          <span class="bpic"><img src={asset.boss(boss.id)} alt="" /></span>
          <span class="binfo">
            <span class="leader">敌方领袖</span>
            <b>{boss.name}</b>
          </span>
          <span class="more term">Detail<Icon name="arrowRight" size={12} /></span>
        </button>
      {/if}
      {#each m.enemyTypes as code (code)}
        {@const t = app.catalog.enemyTypes.find((x) => x.code === code)!}
        <div class="etype">
          <span class="ei"><img src={asset.enemyType(code)} alt="" /></span>
          <span>特训敌人 · {t.name}</span>
        </div>
      {/each}
    </div>
    {/if}
  </section>

  <section>
    <SectionHeader title="本局可用阵容" en="LINEUP PLANNER" ontoggle={fold('lineups')} folded={!open('lineups')}>
      {#snippet icon()}
        <Icon name="grid" size={18} />
      {/snippet}
      {#snippet right()}
        <button class="hbtn" onclick={() => ((app.tab = 'lineup'), (app.lineupView = 'plan'))}><Icon name="magic" size={14} />按本局规划</button>
      {/snippet}
    </SectionHeader>
    {#if open('lineups')}
    <div class="recs" transition:slide={sl}>
      {#each usable as { l } (l.id)}
        {@const isT = app.session.targetLineupId === l.id}
        <LineupCard title={l.name} members={l.members} target={isT}>
          {#snippet actions()}
            {#if !isT}<button class="act" onclick={() => app.setTarget(l.id)}>设为目标</button>{/if}
          {/snippet}
        </LineupCard>
      {/each}
      <button class="all" onclick={() => ((app.tab = 'lineup'), (app.lineupView = 'mine'))}>我的阵容<Icon name="arrowRight" size={14} /></button>
    </div>
    {/if}
  </section>
</div>

<style>
  .page {
    display: flex;
    flex-direction: column;
    gap: 12px;
    padding: 10px 10px 24px;
  }
  .status {
    display: flex;
    align-items: center;
    justify-content: space-between;
    font-size: 12px;
  }
  .st {
    min-width: 0;
    white-space: nowrap;
    display: flex;
    align-items: center;
    gap: 6px;
    color: var(--fg-2);
  }
  .st .term {
    font-size: 9px;
    padding: 2px 4px 1px;
    color: var(--bg-0);
    background: var(--fg-4);
  }
  .st.ok .term {
    background: var(--mint);
  }
  .st em {
    font-style: normal;
    color: var(--fg-4);
  }
  .tools {
    display: flex;
    gap: 4px;
  }
  .tool {
    white-space: nowrap;
    display: flex;
    align-items: center;
    gap: 4px;
    font-size: 12px;
    color: var(--fg-2);
    padding: 3px 8px;
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .tool:hover {
    color: var(--fg-1);
    box-shadow: inset 0 0 0 1px var(--fg-3);
  }
  .tool.on {
    color: var(--mint-ink);
    background: var(--mint);
    box-shadow: none;
  }
  .note {
    font-size: 12px;
    line-height: 1.5;
    color: var(--mint);
    padding: 6px 8px;
    background: var(--mint-tint);
    box-shadow: inset 2px 0 0 var(--mint);
  }
  .note.bad {
    color: #f2b8ba;
    background: var(--red-tint);
    box-shadow: inset 2px 0 0 var(--red);
  }
  .edithint {
    font-size: 11px;
    line-height: 1.5;
    color: var(--mint);
    padding: 6px 8px;
    background: var(--mint-tint);
    box-shadow: inset 2px 0 0 var(--mint);
  }
  .editing {
    outline: 1px dashed rgb(34 227 178 / 0.45);
    outline-offset: -1px;
  }
  .none {
    margin: 0;
    font-size: 12px;
    color: var(--fg-4);
  }
  .hbtn {
    display: flex;
    align-items: center;
    gap: 3px;
    font-size: 12px;
    color: var(--mint);
    padding: 3px 4px;
  }
  .hbtn:hover {
    color: var(--mint-hi);
  }
  .spin {
    display: grid;
  }
  .tool.busy .spin {
    animation: spin 0.8s linear infinite;
  }
  @keyframes spin {
    to {
      transform: rotate(360deg);
    }
  }
  .strategy {
    display: flex;
    gap: 9px;
    align-items: center;
    padding: 7px 9px;
    background: var(--hatch), var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line-soft);
  }
  .strategy img {
    width: 38px;
    height: 38px;
    object-fit: cover;
    flex: none;
    box-shadow: 0 0 0 1px var(--line);
  }
  .sn {
    display: flex;
    gap: 6px;
    align-items: baseline;
  }
  .sn b {
    font-size: 13px;
  }
  .sn span {
    font-size: 11px;
    color: var(--fg-3);
  }
  .strategy p {
    margin: 1px 0 0;
    font-size: 11px;
    color: var(--fg-3);
    line-height: 1.35;
  }
  section {
    display: flex;
    flex-direction: column;
  }
  .ti {
    width: 20px;
    height: 20px;
    object-fit: contain;
  }
  .grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(58px, 1fr));
    gap: 12px 2px;
    padding: 14px 2px 6px;
    justify-items: center;
  }
  .banned {
    display: flex;
    flex-direction: column;
    gap: 10px;
    padding: 10px 4px 4px;
  }
  .bg {
    display: flex;
    flex-direction: column;
    gap: 6px;
  }
  .bgh {
    display: flex;
    align-items: center;
    gap: 6px;
    font-size: 12px;
    color: var(--fg-2);
  }
  .bgh span {
    font-size: 11px;
    color: var(--fg-4);
  }
  .tiles {
    display: flex;
    flex-wrap: wrap;
    gap: 8px 6px;
  }
  .enemy {
    display: flex;
    flex-direction: column;
    gap: 4px;
    padding-top: 8px;
  }
  .boss {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 6px;
    text-align: left;
    background: linear-gradient(90deg, #2a2a26, var(--bg-2));
    box-shadow: inset 0 0 0 1px #3b3a33;
  }
  .boss:hover {
    box-shadow: inset 0 0 0 1px var(--gold);
  }
  .bpic {
    width: 54px;
    height: 54px;
    flex: none;
    background: #fff1;
    box-shadow: 0 0 0 1px #4a4a44;
    overflow: hidden;
  }
  .bpic img {
    width: 100%;
    height: 100%;
    object-fit: cover;
  }
  .binfo {
    flex: 1;
    display: flex;
    flex-direction: column;
    align-items: flex-start;
    gap: 3px;
  }
  .leader {
    font-size: 11px;
    font-weight: 700;
    padding: 1px 8px;
    color: var(--gold-ink);
    background: var(--gold);
  }
  .binfo b {
    font-size: 15px;
  }
  .more {
    display: flex;
    align-items: center;
    font-size: 9px;
    color: var(--fg-4);
    align-self: flex-end;
  }
  .etype {
    display: flex;
    align-items: center;
    gap: 0;
    background: var(--bg-2);
    font-size: 13px;
    font-weight: 600;
    color: var(--fg-1);
  }
  .etype > span:last-child {
    flex: 1;
    text-align: center;
    letter-spacing: 0.05em;
  }
  .ei {
    width: 32px;
    height: 32px;
    display: grid;
    place-items: center;
    background: #101113;
  }
  .ei img {
    width: 22px;
    height: 22px;
  }
  .recs {
    display: flex;
    flex-direction: column;
    gap: 6px;
    padding-top: 8px;
  }
  .all {
    display: flex;
    align-items: center;
    align-self: flex-end;
    font-size: 12px;
    color: var(--fg-3);
    padding: 2px 0;
  }
  .all:hover {
    color: var(--mint);
  }
</style>
