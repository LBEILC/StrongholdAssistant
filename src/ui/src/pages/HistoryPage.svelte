<script lang="ts">
  import { slide } from 'svelte/transition'
  import { app } from '../lib/state.svelte'
  import { asset } from '../lib/assets'
  import { history, modeText, resultText, stats, pct, defaultsFromHistory, type ModeId, type RangeId } from '../lib/history.svelte'
  import { charMap, itemMap, recordToLineup } from '../lib/skland'
  import type { MatchRecord } from '../lib/api/types'
  import SectionHeader from '../components/SectionHeader.svelte'
  import Seg from '../components/Seg.svelte'
  import BondIcon from '../components/BondIcon.svelte'
  import Icon from '../components/Icon.svelte'
  import { openMenu } from '../lib/menu.svelte'
  import { api } from '../lib/api'

  // 历史记录：上面统计（只算有结果的局），下面一局一行，点开看详情
  $effect(() => {
    history.load().catch((e) => app.showToast(e.message, true))
  })

  let mode = $state<ModeId>('all')
  let range = $state<RangeId>('all')
  const records = $derived(history.records ?? [])
  const s = $derived(stats(records, mode, range))
  const byChar = charMap(app.catalog)
  const byItem = itemMap(app.catalog)
  const strategyName = (id: string | null) => app.catalog.strategies.find((x) => x.id === id)?.name ?? (id ? '未知策略' : '无策略')
  const bossName = (id: string) => app.catalog.bosses.find((b) => b.id === id)?.name ?? '未知首领'

  let open = $state<string | null>(null)
  let confirmDel = $state<string | null>(null)

  const when = (iso: string) => {
    const d = new Date(iso)
    const p = (n: number) => String(n).padStart(2, '0')
    return `${d.getMonth() + 1}月${d.getDate()}日 ${p(d.getHours())}:${p(d.getMinutes())}`
  }
  const minutes = (a: string, b: string) => Math.max(1, Math.round((Date.parse(b) - Date.parse(a)) / 60000))

  // 从战绩生成「我的默认」技能、模组：编辑阵容时加干员就默认用自己常用的那套
  function makeDefaults() {
    const d = defaultsFromHistory(records, byChar)
    const n = Object.keys(d).length
    if (!n) return app.showToast('战绩里没有技能记录，先「从森空岛更新」', true)
    app.settings.operatorDefaults = { ...app.settings.operatorDefaults, ...d }
    app.saveSettings()
    app.showToast(`已按战绩设好 ${n} 名干员的默认技能和模组`)
  }
  async function sync() {
    try {
      const got = await history.sync()
      if (got) app.showToast(`已从森空岛读到 ${got.length} 局`)
    } catch (e) {
      app.showToast((e as Error).message, true)
    }
  }
  function toLineup(r: MatchRecord) {
    if (r.skland) return app.importLineup(recordToLineup(r.skland, app.catalog).lineup, false)
    const l = r.local!.lineup!
    app.importLineup({ ...$state.snapshot(l), id: '', name: `${when(r.time)} ${l.name}`.slice(0, 30), source: 'user' }, false)
  }
  async function del(id: string) {
    if (confirmDel !== id) return void (confirmDel = id)
    await history.remove(id)
    confirmDel = null
    open = null
  }

  // 右键一局：展开、生成阵容、复制摘要、删除
  function recordMenu(ev: MouseEvent, r: MatchRecord) {
    const sk = r.skland
    const lo = r.local
    const canMake = !!sk || !!lo?.lineup
    const summary = [when(r.time), modeText(r), strategyName(sk?.strategyId ?? lo?.strategyId ?? null), sk ? (sk.success ? '通关' : resultText(sk)) : lo?.lastRound ? `到第 ${lo.lastRound} 回合` : ''].filter(Boolean).join(' · ')
    openMenu(ev, {
      title: when(r.time),
      sub: modeText(r),
      items: [
        { label: open === r.id ? '收起' : '展开', icon: open === r.id ? 'arrowUp' : 'arrowDown', run: () => ((open = open === r.id ? null : r.id), (confirmDel = null)) },
        { label: '生成阵容', icon: 'add', disabled: !canMake, run: () => toLineup(r) },
        { label: '复制这局的摘要', icon: 'copy', run: () => api.copyText(summary).then(() => app.showToast('已复制')) },
        'sep',
        { label: '删除这条记录', icon: 'trash', tone: 'danger', confirm: '再点一次删除', run: () => history.remove(r.id) },
      ],
    })
  }

  // 分组可折叠，状态记在本机（同本局页）
  const shown = (key: string) => !app.folded[`history.${key}`]
  const fold = (key: string) => () => app.toggleFold(`history.${key}`)
  const sl = { duration: 180 }

  // 资金曲线：每回合起始资金，折线 + 点
  function curve(rounds: { round: number; startMoney: number }[]) {
    const max = Math.max(...rounds.map((r) => r.startMoney), 1)
    const w = 100 / Math.max(rounds.length - 1, 1)
    return rounds.map((r, i) => ({ x: i * w, y: 36 - (r.startMoney / max) * 30, ...r }))
  }
</script>

<div class="page">
  <section class="rec">
    <SectionHeader title="对局记录" tag="{records.length} 局" en="MATCH RECORDS" ontoggle={fold('records')} folded={!shown('records')}>
      {#snippet icon()}<Icon name="history" size={18} />{/snippet}
      {#snippet right()}
        <button class="ghost" class:busy={history.syncing} disabled={history.syncing} onclick={sync} title="读森空岛里的历史战绩，补上结果、最终阵容和花费">
          <span class="spin"><Icon name="refresh" size={14} /></span>{history.syncing ? '正在读取…' : '从森空岛更新'}
        </button>
      {/snippet}
    </SectionHeader>

    {#if shown('records')}
    <div class="fold" transition:slide={sl}>
    {#if records.some((r) => r.skland)}
      <p class="mkdef">编辑阵容时加干员，技能和模组想默认用自己常用的？<button class="ghost" onclick={makeDefaults}>按战绩生成我的默认</button></p>
    {/if}
    {#if history.records && !records.length}
      <p class="empty">还没有记录。助手认到新的一局会自动记下；点「从森空岛更新」，把森空岛里的战绩也补进来。</p>
    {/if}

    <div class="list">
      {#each records as r (r.id)}
        {@const sk = r.skland}
        {@const lo = r.local}
        {@const isOpen = open === r.id}
        <div class="row" class:open={isOpen} class:win={sk?.success}>
          <button class="head" onclick={() => ((open = isOpen ? null : r.id), (confirmDel = null))} oncontextmenu={(ev) => recordMenu(ev, r)} aria-expanded={isOpen}>
            <span class="time term">{when(r.time)}</span>
            <span class="mode">{modeText(r)}</span>
            <span class="strat">{strategyName(sk?.strategyId ?? lo?.strategyId ?? null)}</span>
            <span class="res">
              {#if sk}
                {#if sk.success}<b class="ok">通关</b>{:else}{resultText(sk)}{/if}
              {:else if lo?.lastRound}
                <span class="muted">到第 {lo.lastRound} 回合</span>
              {:else}
                <span class="muted">没有结果</span>
              {/if}
            </span>
            <span class="arrow"><Icon name="arrowDown" size={14} /></span>
          </button>

          {#if isOpen}
            <div class="body" transition:slide={{ duration: 180 }}>
              {#if !sk}
                <p class="note">只有助手的本地记录：结果、最终阵容要「从森空岛更新」后才有。</p>
              {/if}

              <!-- 森空岛给结束时激活的盟约和层数；本地只知道这局开局有哪些盟约（太多，不列），写当时的目标阵容 -->
              {#if sk?.covenants.length}
                <div class="bonds">
                  {#each sk.covenants as c (c.id)}
                    <span class="bond" title="{app.cov(c.id)?.name ?? c.id} {c.layer} 层"><BondIcon id={c.id} size={30} on /><i class="term">{c.layer}</i></span>
                  {/each}
                </div>
              {/if}
              {#if lo}
                <dl class="facts">
                  <div><dt>当时的目标</dt><dd>{lo.lineup?.name ?? '没有设目标'}</dd></div>
                  {#if lo.bossId}<div><dt>首领</dt><dd>{bossName(lo.bossId)}</dd></div>{/if}
                  {#if lo.bannedOperators.length}<div><dt>本局禁用</dt><dd class="term">{lo.bannedOperators.length} 名</dd></div>{/if}
                </dl>
              {/if}

              {#if sk}
                <div class="faces">
                  {#each sk.operators as o, i (i)}
                    {@const op = byChar.get(o.id)}
                    {#if op}
                      <button class="face" class:elite={o.elite} title="{op.name}{o.elite ? '（精锐）' : ''}" onclick={() => (app.detail = { kind: 'operator', name: op.name })}>
                        <img src={asset.operator(op.name)} alt={op.name} />
                      </button>
                    {/if}
                  {/each}
                  {#each sk.equipment as id, i (i)}
                    {@const name = byItem.get(id)}
                    {#if name}
                      <button class="face eq" title={name} onclick={() => (app.detail = { kind: 'equipment', name })}>
                        <img src={asset.equipment(name)} alt={name} />
                      </button>
                    {/if}
                  {/each}
                </div>
                <dl class="facts">
                  <div><dt>花费资金</dt><dd class="term">{sk.coinSpent}</dd></div>
                  <div><dt>击倒</dt><dd class="term">{sk.kills}</dd></div>
                  <div><dt>用时</dt><dd class="term">{minutes(sk.startTime, sk.time)} 分钟</dd></div>
                  {#if sk.bosses.length}
                    <div class="wide">
                      <dt>首领</dt>
                      <dd>{sk.bosses.map((b) => `${bossName(b.id)}${b.killed ? '' : '（未击败）'}`).join('、')}</dd>
                    </div>
                  {/if}
                </dl>
              {/if}

              {#if lo?.rounds.length}
                {@const pts = curve(lo.rounds)}
                <div class="money">
                  <span class="lbl">每回合起始资金</span>
                  <svg viewBox="-4 0 108 44" preserveAspectRatio="none" aria-hidden="true">
                    <polyline points={pts.map((p) => `${p.x},${p.y}`).join(' ')} />
                  </svg>
                  <div class="ticks">
                    {#each pts as p (p.round)}<span title="第 {p.round} 回合"><b class="term">{p.startMoney}</b></span>{/each}
                  </div>
                </div>
              {/if}

              <div class="acts">
                {#if sk || lo?.lineup}
                  <button class="ghost" onclick={() => toLineup(r)}><Icon name="add" size={14} />生成阵容</button>
                {/if}
                <span class="sp"></span>
                <button class="ghost danger" class:armed={confirmDel === r.id} onclick={() => del(r.id)}>
                  <Icon name="trash" size={14} />{confirmDel === r.id ? '再点一次删除' : '删除'}
                </button>
              </div>
            </div>
          {/if}
        </div>
      {/each}
    </div>
    </div>
    {/if}
  </section>

  <section class="stats">
    <SectionHeader title="战绩统计" tag="{s.total} 局有结果" en="STATISTICS" ontoggle={fold('stats')} folded={!shown('stats')}>
      {#snippet icon()}<Icon name="chart" size={18} />{/snippet}
    </SectionHeader>
    {#if shown('stats')}
    <div class="fold" transition:slide={sl}>
    <div class="filters">
      <Seg items={[{ id: 'all', label: '全部' }, { id: 'MULTI', label: '联机' }, { id: 'SINGLE', label: '单人' }]} value={mode} onchange={(v) => (mode = v)} />
      <Seg items={[{ id: '7', label: '近 7 天' }, { id: '30', label: '近 30 天' }, { id: 'all', label: '全部时间' }]} value={range} onchange={(v) => (range = v)} />
    </div>

    {#if !s.total}
      <p class="empty">没有带结果的对局。统计只算对上了森空岛战绩的局。</p>
    {:else}
      <div class="kpis">
        <div><b class="term">{s.total}</b><span>局数</span></div>
        <div><b class="term ok">{pct(s.wins, s.total)}</b><span>通关率</span></div>
        <div><b class="term">{s.avgRound.toFixed(1)}</b><span>平均到达回合</span></div>
        <div><b class="term">{Math.round(s.avgSpent)}</b><span>平均花费资金</span></div>
      </div>

      <button class="h5" class:folded={!shown('trend')} onclick={fold('trend')} aria-expanded={shown('trend')}>最近 {s.trend.length} 局到达回合 <small>亮色为通关</small><Icon name="arrowDown" size={13} /></button>
      {#if shown('trend')}
      <div class="trend" role="img" aria-label="最近对局到达的回合">
        {#each s.trend as t (t.id)}
          <span class="bar" class:ok={t.success} style:height="{(Math.max(t.passRound, 0.5) / 15) * 100}%" title="{when(t.time)} · {resultText(t)}"></span>
        {/each}
      </div>
      {/if}

      <button class="h5" class:folded={!shown('strats')} onclick={fold('strats')} aria-expanded={shown('strats')}>策略<small>{s.strategies.length} 种</small><Icon name="arrowDown" size={13} /></button>
      {#if shown('strats')}
      <div class="strats">
        {#each s.strategies as g (g.id)}
          <div class="srow">
            <img src={asset.strategy(g.id)} alt="" />
            <span class="sname">{strategyName(g.id)}</span>
            <span class="meter"><i style:width={pct(g.wins, g.n)}></i></span>
            <span class="num term">{g.n} 局 · {pct(g.wins, g.n)}</span>
          </div>
        {/each}
      </div>
      {/if}

      <button class="h5" class:folded={!shown('ops')} onclick={fold('ops')} aria-expanded={shown('ops')}>常用干员 <small>出场局数 · 出场时通关率</small><Icon name="arrowDown" size={13} /></button>
      {#if shown('ops')}
      <div class="ops">
        {#each s.operators as g (g.id)}
          {@const op = byChar.get(g.id)}
          {#if op}
            <button class="opc" onclick={() => (app.detail = { kind: 'operator', name: op.name })} title={op.name}>
              <img src={asset.operator(op.name)} alt="" />
              <span class="on">{op.name}</span>
              <span class="term">{g.n} · {pct(g.wins, g.n)}</span>
            </button>
          {/if}
        {/each}
      </div>
      {/if}
    {/if}
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
  section {
    display: flex;
    flex-direction: column;
    gap: 8px;
    min-width: 0;
  }
  /* 侧栏里统计在上；工作台里记录在左、统计在右 */
  .stats {
    order: -1;
  }
  @container (min-width: 860px) {
    .page {
      display: grid;
      grid-template-columns: minmax(0, 1.25fr) minmax(0, 1fr);
      gap: 16px;
      align-items: start;
    }
    /* 两栏各自滚动：统计比窗口高时，不用等左边滚到底（用户 2026-11-12 反馈） */
    .page {
      height: 100%;
      box-sizing: border-box;
      padding-bottom: 10px;
      align-items: stretch;
    }
    .rec,
    .stats {
      min-height: 0;
      max-height: 100%;
      overflow-y: auto;
      padding-right: 4px;
      /* 不写 scrollbar-width：写了 Chromium 就改用系统滚动条（带箭头），全局的细滚动条样式失效 */
    }
    .stats {
      order: 0;
    }
  }
  .ghost {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    font-size: var(--fs-base);
    color: var(--mint);
    padding: 3px 4px;
  }
  .ghost:hover:not(:disabled) {
    color: var(--mint-hi);
  }
  .ghost:disabled {
    color: var(--fg-3);
    cursor: default;
  }
  .spin {
    display: grid;
  }
  .busy .spin {
    animation: spin var(--t-spin) linear infinite;
  }
  @keyframes spin {
    to {
      transform: rotate(360deg);
    }
  }
  .mkdef {
    margin: 0;
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 4px;
    font-size: var(--fs-sm);
    color: var(--fg-3);
  }
  .empty,
  .note {
    margin: 0;
    font-size: var(--fs-base);
    line-height: 1.6;
    color: var(--fg-3);
  }
  .note {
    color: var(--gold);
  }

  /* 记录 */
  .list {
    display: flex;
    flex-direction: column;
    gap: 4px;
  }
  .row {
    background: var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line-soft);
    border-left: 2px solid var(--bg-4);
  }
  .row.win {
    border-left-color: var(--mint);
  }
  .row.open {
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .head {
    width: 100%;
    display: grid;
    grid-template-columns: auto 1fr auto 14px;
    grid-template-areas: 'time strat res arrow' 'mode mode res arrow';
    column-gap: 10px;
    row-gap: 2px;
    align-items: center;
    padding: 7px 9px;
    text-align: left;
  }
  .head:hover {
    background: var(--bg-2);
  }
  .time {
    grid-area: time;
    font-size: var(--fs-sm);
    color: var(--fg-3);
  }
  .mode {
    grid-area: mode;
    font-size: var(--fs-sm);
    color: var(--fg-3);
  }
  .strat {
    grid-area: strat;
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    font-size: var(--fs-md);
    font-weight: var(--fw-bold);
  }
  .res {
    grid-area: res;
    font-size: var(--fs-base);
    color: var(--fg-2);
    white-space: nowrap;
  }
  .ok {
    color: var(--mint);
  }
  .muted {
    color: var(--fg-4);
  }
  .arrow {
    grid-area: arrow;
    display: grid;
    color: var(--fg-4);
    transition: transform var(--t-mid);
  }
  .open .arrow {
    transform: rotate(180deg);
  }
  .body {
    display: flex;
    flex-direction: column;
    gap: 9px;
    padding: 2px 9px 9px;
  }
  .bonds,
  .faces {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
  }
  .bond {
    position: relative;
    display: grid;
  }
  .bond i {
    position: absolute;
    right: -3px;
    bottom: -3px;
    font-style: normal;
    font-size: var(--fs-2xs);
    padding: 0 2px;
    color: var(--fg-1);
    background: var(--bg-0);
  }
  .face {
    position: relative;
    width: 38px;
    height: 38px;
    box-shadow: 0 0 0 1px var(--line);
    background: var(--bg-2);
  }
  .face img {
    width: 100%;
    height: 100%;
    object-fit: cover;
  }
  .face.eq img {
    object-fit: contain;
    padding: 3px;
  }
  .face:hover {
    box-shadow: 0 0 0 1px var(--mint);
  }
  /* 精锐：右上角金色三角 */
  .face.elite::after {
    content: '';
    position: absolute;
    right: 0;
    top: 0;
    border: 6px solid transparent;
    border-top-color: var(--gold);
    border-right-color: var(--gold);
  }
  .facts {
    margin: 0;
    display: flex;
    flex-wrap: wrap;
    gap: 6px 16px;
    font-size: var(--fs-base);
  }
  .facts div {
    display: flex;
    gap: 6px;
  }
  .facts .wide {
    flex-basis: 100%;
  }
  dt {
    color: var(--fg-3);
  }
  dd {
    margin: 0;
  }
  .money .lbl {
    font-size: var(--fs-sm);
    color: var(--fg-3);
  }
  .money svg {
    display: block;
    width: 100%;
    height: 44px;
    overflow: visible;
  }
  .money polyline {
    fill: none;
    stroke: var(--mint);
    stroke-width: 1.5;
    vector-effect: non-scaling-stroke;
  }
  .ticks {
    display: flex;
    justify-content: space-between;
    font-size: var(--fs-xs);
    color: var(--fg-2);
  }
  .acts {
    display: flex;
    align-items: center;
  }
  .sp {
    flex: 1;
  }
  .danger {
    color: var(--fg-3);
  }
  .danger:hover,
  .danger.armed {
    color: var(--red);
  }

  /* 统计 */
  .filters {
    display: flex;
    flex-direction: column;
    gap: 6px;
  }
  .kpis {
    display: grid;
    grid-template-columns: repeat(4, 1fr);
    gap: 4px;
  }
  .kpis div {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 2px;
    padding: 8px 2px;
    background: var(--hatch), var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line-soft);
  }
  .kpis b {
    font-size: var(--fs-2xl);
  }
  .kpis span {
    font-size: var(--fs-xs);
    color: var(--fg-3);
    white-space: nowrap;
  }
  .fold {
    display: flex;
    flex-direction: column;
    gap: 8px;
  }
  .h5 {
    display: flex;
    align-items: center;
    gap: 6px;
    margin: 6px 0 0;
    font-size: var(--fs-base);
    font-weight: var(--fw-bold);
    color: var(--fg-1);
    text-align: left;
  }
  .h5 :global(svg) {
    margin-left: auto;
    color: var(--fg-4);
    transition: transform var(--t-exit);
  }
  .h5.folded :global(svg) {
    transform: rotate(-90deg);
  }
  .h5:hover :global(svg) {
    color: var(--fg-2);
  }
  .h5 small {
    font-weight: var(--fw-base);
    color: var(--fg-4);
    margin-left: 6px;
  }
  .trend {
    display: flex;
    align-items: flex-end;
    gap: 2px;
    height: 56px;
    padding: 0 1px;
    border-bottom: 1px solid var(--line);
  }
  .bar {
    flex: 1;
    max-width: 18px;
    background: var(--bg-4);
  }
  .bar.ok {
    background: var(--mint);
    box-shadow: var(--mint-glow);
  }
  .strats {
    display: flex;
    flex-direction: column;
    gap: 4px;
  }
  .srow {
    display: grid;
    grid-template-columns: 24px minmax(0, 1fr) 60px auto;
    gap: 8px;
    align-items: center;
    font-size: var(--fs-base);
  }
  .srow img {
    width: 24px;
    height: 24px;
    object-fit: cover;
    box-shadow: 0 0 0 1px var(--line);
  }
  .sname {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .meter {
    height: 4px;
    background: var(--bg-3);
  }
  .meter i {
    display: block;
    height: 100%;
    background: var(--mint);
  }
  .num {
    font-size: var(--fs-sm);
    color: var(--fg-3);
    white-space: nowrap;
  }
  .ops {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(58px, 1fr));
    gap: 6px;
  }
  .opc {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 2px;
    font-size: var(--fs-xs);
    color: var(--fg-3);
  }
  .opc img {
    width: 42px;
    height: 42px;
    object-fit: cover;
    box-shadow: 0 0 0 1px var(--line);
  }
  .opc:hover img {
    box-shadow: 0 0 0 1px var(--mint);
  }
  .opc .on {
    max-width: 100%;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    font-size: var(--fs-sm);
    color: var(--fg-1);
  }
</style>
