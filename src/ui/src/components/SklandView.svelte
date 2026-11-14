<script lang="ts" module>
  // 搜索和筛选放在模块里：切到别的分段再回来还在，重启清空
  type Section = 'all' | 'collected' | 'published'
  type Sort = 'match' | 'recommended' | 'latest'
  const st = $state({
    section: 'all' as Section,
    sort: 'recommended' as Sort,
    q: '',
    core: null as string | null,
    extras: [] as string[],
    strategy: null as string | null,
  })
</script>

<script lang="ts">
  import { untrack } from 'svelte'
  import { app } from '../lib/state.svelte'
  import { api, type Lineup, type SklandGuideItem } from '../lib/api'
  import { guideToLineup } from '../lib/skland'
  import { asset } from '../lib/assets'
  import LineupCard from './LineupCard.svelte'
  import Icon from './Icon.svelte'
  import BondIcon from './BondIcon.svelte'
  import StrategyGrid from './StrategyGrid.svelte'

  // 阵容页的「森空岛」分段（v0.9.2，099）：森空岛上的卫戍协议攻略。宿主在隐藏的官方攻略页里取数据，一次一页（10 条），
  // 只在翻页、搜索、改筛选时取。列表自带整篇攻略，直接换成阵容卡，导入不再读详情。
  // 「本局」排序只在已经读到的这些里排（不是森空岛全部攻略），按本局盟约和禁用
  const hasMatch = $derived(app.match.source !== 'none')
  const isAll = $derived(st.section === 'all')

  let items = $state<SklandGuideItem[]>([])
  let token = $state<string | null>(null)
  let loginRequired = $state(false)
  let loading = $state(false)
  let error = $state('')
  let seq = 0

  async function load(more = false) {
    const my = ++seq
    loading = true
    error = ''
    try {
      const r = await api.sklandGuides({
        section: st.section,
        keyword: st.q,
        ...(isAll && { sort: st.sort === 'latest' ? 'latest' : 'recommended', core: st.core, extras: st.extras, strategyId: st.strategy }),
        ...(more && token && { pageToken: token }),
      })
      if (my !== seq) return
      const seen = new Set(more ? items.map((x) => x.id) : [])
      items = [...(more ? items : []), ...r.items.filter((x) => !seen.has(x.id))]
      token = r.pageToken
      loginRequired = r.loginRequired
    } catch (e) {
      if (my === seq) error = (e as Error).message
    } finally {
      if (my === seq) loading = false
    }
  }
  // 「本局」和「推荐」取的是同一份数据，切换不重新读
  $effect(() => {
    void [st.section, st.sort === 'latest', st.q, st.core, st.extras, st.strategy]
    untrack(() => load())
  })

  let logging = $state(false)
  async function login() {
    logging = true
    try {
      if (await api.sklandLogin()) load()
    } catch (e) {
      app.showToast((e as Error).message, true)
    }
    logging = false
  }

  // 搜索：打字停 0.3 秒再查（森空岛按标题文字匹配，不是干员搜索）
  let typed = $state(st.q)
  let timer: ReturnType<typeof setTimeout> | undefined
  const typing = (v: string) => {
    typed = v
    clearTimeout(timer)
    timer = setTimeout(() => (st.q = v.trim()), 300)
  }
  const clearSearch = () => {
    clearTimeout(timer)
    typed = st.q = ''
  }

  // 筛选（只有攻略大全能筛）：核心盟约单选、附加盟约多选（都要包含）、策略单选
  let panel = $state(false)
  const count = $derived((st.core ? 1 : 0) + st.extras.length + (st.strategy ? 1 : 0))
  const cores = app.catalog.covenants.filter((c) => c.kind === 'core')
  const extras = app.catalog.covenants.filter((c) => c.kind === 'extra')
  const cov = (id: string) => app.catalog.covenants.find((c) => c.id === id)
  const toggleExtra = (id: string) => (st.extras = st.extras.includes(id) ? st.extras.filter((x) => x !== id) : [...st.extras, id])
  // 目标阵容当前阶段的盟约：核心取第一个，附加全带上
  const targetGoals = $derived(app.active?.goals.map((g) => g.id) ?? [])
  const useTarget = () => {
    st.core = targetGoals.find((id) => cov(id)?.kind === 'core') ?? null
    st.extras = targetGoals.filter((id) => cov(id)?.kind === 'extra')
  }
  let sq = $state('')
  let pickingSt = $state(false)
  const pickSt = (id: string) => {
    st.strategy = id === st.strategy ? null : id
    pickingSt = false
    sq = ''
  }
  const strat = $derived(app.catalog.strategies.find((s) => s.id === st.strategy))
  const clearFilters = () => {
    st.core = null
    st.extras = []
    st.strategy = null
  }

  // 列表：自带攻略的换成阵容；「本局」在已读到的里按阵容适配度排（不完整盟约 × 100 + 被禁人数）
  const cards = $derived(items.map((it) => ({ it, sk: it.guide ? guideToLineup(it.guide, app.catalog) : null })))
  const shown = $derived(
    st.sort === 'match' && isAll
      ? [...cards].sort((a, b) => (a.sk ? app.lineupPenalty(a.sk.lineup) : 1e9) - (b.sk ? app.lineupPenalty(b.sk.lineup) : 1e9))
      : cards,
  )
  const num = (n: number) => (n >= 10000 ? `${(n / 10000).toFixed(1)}万` : String(n))
  const sections: { v: Section; label: string }[] = [
    { v: 'all', label: '攻略大全' },
    { v: 'collected', label: '我的收藏' },
    { v: 'published', label: '我的发布' },
  ]
  const sorts: { v: Sort; label: string; tip?: string }[] = [
    { v: 'match', label: '本局', tip: '在已经读到的攻略里，按本局盟约和禁用排' },
    { v: 'recommended', label: '推荐' },
    { v: 'latest', label: '最新' },
  ]
  // 这次打开里导入过的攻略：按钮改成「已导入」，还能再点
  let done = $state<string[]>([])
  const importOne = (it: SklandGuideItem, lineup: Lineup) => {
    app.importLineup(lineup, false, true)
    if (!done.includes(it.id)) done = [...done, it.id]
  }
  const menuFor = (it: SklandGuideItem, lineup: Lineup) => () => ({
    title: it.title,
    sub: `森空岛 · ${it.author}`,
    items: [
      { label: '导入到我的阵容', icon: 'download' as const, run: () => importOne(it, lineup) },
      { label: '导入并设为目标', icon: 'focus' as const, run: () => app.importLineup(lineup, true) },
      'sep' as const,
      { label: '在森空岛打开', icon: 'arrowRight' as const, run: () => api.openUrl(it.url) },
    ],
  })
</script>

<div class="secs" role="tablist">
  {#each sections as s (s.v)}
    <button role="tab" class:on={st.section === s.v} aria-selected={st.section === s.v} onclick={() => (st.section = s.v)}>{s.label}</button>
  {/each}
  <button class="web" onclick={() => api.openUrl('https://game.skland.com/arknights/stronghold-guide')} title="在浏览器里打开森空岛攻略页">森空岛<Icon name="arrowRight" size={12} /></button>
</div>

<div class="search">
  <Icon name="search" size={14} />
  <input type="text" placeholder="搜攻略标题" value={typed} oninput={(e) => typing(e.currentTarget.value)} onkeydown={(e) => e.key === 'Escape' && typed && (e.stopPropagation(), clearSearch())} />
  {#if typed}<button class="x" title="清空搜索" aria-label="清空搜索" onclick={clearSearch}><Icon name="close" size={13} /></button>{/if}
</div>

{#if isAll}
  <div class="bar">
    <div class="sorts">
      {#each sorts as s (s.v)}
        <button class:on={st.sort === s.v} disabled={s.v === 'match' && !hasMatch} title={s.v === 'match' ? (hasMatch ? s.tip : '还没识别本局，识别后按本局排') : ''} onclick={() => (st.sort = s.v)}>{s.label}</button>
      {/each}
    </div>
    <button class="fbtn" class:on={panel} class:has={count > 0} aria-expanded={panel} onclick={() => (panel = !panel)}>
      筛选{#if count}<em>{count}</em>{/if}<span class="chev"><Icon name="arrowDown" size={12} /></span>
    </button>
  </div>

  {#if panel}
    <div class="fpanel">
      <div class="fh">
        <span>核心盟约<em>选一个</em></span>
        {#if targetGoals.length}<button class="link" onclick={useTarget}>用目标阵容的</button>{/if}
      </div>
      <div class="bonds">
        {#each cores as c (c.id)}
          <button class="bond" class:on={st.core === c.id} title={c.name} onclick={() => (st.core = st.core === c.id ? null : c.id)}>
            <BondIcon id={c.id} size={22} on={st.core === c.id} /><span>{c.name}</span>
          </button>
        {/each}
      </div>
      <div class="fh"><span>附加盟约<em>选多个时要全部包含</em></span></div>
      <div class="bonds">
        {#each extras as c (c.id)}
          <button class="bond" class:on={st.extras.includes(c.id)} title={c.name} onclick={() => toggleExtra(c.id)}>
            <BondIcon id={c.id} size={22} on={st.extras.includes(c.id)} /><span>{c.name}</span>
          </button>
        {/each}
      </div>
      <div class="fh"><span>策略</span></div>
      {#if pickingSt}
        <div class="sph">
          <input bind:value={sq} placeholder="搜名字、发起者、效果" />
          <button class="link" onclick={() => ((pickingSt = false), (sq = ''))}>收起</button>
        </div>
        <StrategyGrid selected={st.strategy} q={sq} onpick={pickSt} />
      {:else}
        <div class="chips">
          <button class="stbtn" class:on={!!strat} onclick={() => (pickingSt = true)} title="点一下选策略">
            {#if strat}<img src={asset.strategy(strat.id)} alt="" /><b>{strat.name}</b><span>{strat.initiator}</span>{:else}<span>全部策略</span>{/if}
            <span class="chev"><Icon name="arrowDown" size={12} /></span>
          </button>
          {#if strat}<button class="link" onclick={() => (st.strategy = null)}>不限策略</button>{/if}
        </div>
      {/if}
      {#if count}<button class="link clear" onclick={clearFilters}>清除筛选</button>{/if}
    </div>
  {:else if count}
    <div class="chips active">
      {#if st.core}<button class="chip" title="去掉这个盟约" onclick={() => (st.core = null)}><BondIcon id={st.core} size={16} on />{cov(st.core)?.name}<Icon name="close" size={11} /></button>{/if}
      {#each st.extras as id (id)}
        <button class="chip" title="去掉这个盟约" onclick={() => toggleExtra(id)}><BondIcon {id} size={16} on />{cov(id)?.name}<Icon name="close" size={11} /></button>
      {/each}
      {#if strat}<button class="chip" title="取消策略筛选" onclick={() => (st.strategy = null)}><img src={asset.strategy(strat.id)} alt="" />{strat.name}<Icon name="close" size={11} /></button>{/if}
      <button class="link" onclick={clearFilters}>清除</button>
    </div>
  {/if}
{/if}

{#if error}
  <div class="state"><p>{error}</p><button class="retry" onclick={() => load()}>重试</button></div>
{:else if !items.length && !loading}
  <div class="state">
    {#if loginRequired}
      <p>{st.section === 'collected' ? '「我的收藏」要先登录森空岛。' : st.section === 'published' ? '「我的发布」要先登录森空岛。' : '「最新」要先登录森空岛。'}</p>
      <button class="retry" disabled={logging} onclick={login}>{logging ? '等待登录…' : '登录森空岛'}</button>
    {:else}
      <p>{st.q || (isAll && count) ? '没有符合条件的攻略，换个关键词或筛选试试。' : st.section === 'collected' ? '还没有收藏。在森空岛 App 里收藏攻略，这里就能直接导入。' : st.section === 'published' ? '还没有发布过攻略。' : '没有攻略。'}</p>
    {/if}
  </div>
{:else}
  <div class="list">
    {#each shown as { it, sk } (it.id)}
      {#if sk}
        <LineupCard lineup={sk.lineup} menu={menuFor(it, sk.lineup)} ontitle={() => api.openUrl(it.url)}>
          {#snippet sub()}
            <span class="by">{it.author}</span>
            <span class="num" title="点赞"><Icon name="heart" size={11} />{num(it.stats.likes)}</span>
            <span class="num" title="收藏"><Icon name={it.collected ? 'starFill' : 'star'} size={11} />{num(it.stats.collections)}</span>
            {#if sk.unknown.operators + sk.unknown.equipment}<span class="warn" title="助手的数据里没有，可能是新赛季或旧攻略">{sk.unknown.operators + sk.unknown.equipment} 个认不出</span>{/if}
          {/snippet}
          {#snippet actions()}
            <button class="open" class:done={done.includes(it.id)} onclick={() => importOne(it, sk.lineup)} title="导入到我的阵容；右键还能导入并设为目标">{done.includes(it.id) ? '已导入' : '导入'}</button>
          {/snippet}
        </LineupCard>
      {:else}
        <!-- 列表里没带完整攻略（数据不全或已失效）：只给标题，去森空岛看 -->
        <div class="bare">
          <b>{it.title}</b>
          <span class="by">{it.author}</span>
          <button class="link" onclick={() => api.openUrl(it.url)}>在森空岛打开</button>
        </div>
      {/if}
    {/each}
  </div>
  {#if token}
    <button class="more" disabled={loading} onclick={() => load(true)}>{loading ? '加载中…' : '加载更多'}</button>
  {:else if loginRequired && isAll}
    <div class="state small">
      <p>登录森空岛后能往下翻、看「最新」和收藏。</p>
      <button class="retry" disabled={logging} onclick={login}>{logging ? '等待登录…' : '登录森空岛'}</button>
    </div>
  {/if}
  {#if st.sort === 'match' && isAll}<p class="note">「本局」只在已经读到的 {items.length} 篇里排，按本局盟约和禁用；往下加载会一起排。</p>{/if}
{/if}
{#if loading && !items.length}<p class="state muted">正在读森空岛…</p>{/if}

<style>
  .secs {
    display: flex;
    align-items: center;
    gap: 14px;
    margin-top: 10px;
    border-bottom: 1px solid var(--line);
  }
  .secs button[role='tab'] {
    height: 30px;
    padding: 0 2px;
    font-size: var(--fs-base);
    color: var(--fg-3);
    box-shadow: inset 0 -2px 0 transparent;
  }
  .secs button[role='tab']:hover {
    color: var(--fg-1);
  }
  .secs button[role='tab'].on {
    color: var(--fg-0);
    font-weight: var(--fw-bold);
    box-shadow: inset 0 -2px 0 var(--mint);
  }
  .web {
    margin-left: auto;
    display: inline-flex;
    align-items: center;
    gap: 2px;
    font-size: var(--fs-base);
    color: var(--mint);
  }
  .search {
    display: flex;
    align-items: center;
    gap: 6px;
    height: 32px;
    margin-top: 10px;
    padding: 0 6px 0 10px;
    color: var(--fg-4);
    background: var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .search:focus-within {
    color: var(--mint);
    box-shadow: inset 0 0 0 1px var(--mint-lo);
  }
  .search input {
    flex: 1;
    min-width: 0;
    height: 100%;
    font-size: var(--fs-base);
    color: var(--fg-1);
    background: none;
    border: 0;
    outline: none;
    box-shadow: none;
  }
  .search input::placeholder,
  .sph input::placeholder {
    color: var(--fg-4);
  }
  .x {
    display: grid;
    place-items: center;
    width: 22px;
    height: 22px;
    color: var(--fg-3);
  }
  .x:hover {
    color: var(--fg-1);
    background: var(--bg-3);
  }
  .bar {
    display: flex;
    align-items: center;
    gap: 8px;
    margin: 10px 0 8px;
  }
  .sorts {
    display: flex;
    gap: 2px;
  }
  .sorts button {
    height: 26px;
    padding: 0 10px;
    font-size: var(--fs-base);
    color: var(--fg-2);
    background: var(--bg-2);
  }
  .sorts button:hover:not(:disabled) {
    color: var(--fg-1);
  }
  .sorts button.on {
    color: var(--mint-ink);
    background: var(--mint);
    font-weight: var(--fw-bold);
  }
  .sorts button:disabled {
    color: var(--fg-4);
    cursor: default;
  }
  .fbtn {
    display: inline-flex;
    align-items: center;
    gap: 3px;
    height: 26px;
    padding: 0 6px 0 10px;
    font-size: var(--fs-base);
    color: var(--fg-2);
    background: var(--bg-2);
  }
  .fbtn:hover,
  .fbtn.on {
    color: var(--fg-1);
    background: var(--bg-3);
  }
  .fbtn.has {
    color: var(--mint);
  }
  .fbtn em {
    font-style: normal;
    font-family: var(--font-term);
    min-width: 16px;
    padding: 0 3px;
    color: var(--mint-ink);
    background: var(--mint);
  }
  .fbtn .chev,
  .stbtn .chev {
    display: grid;
    transition: transform var(--t-mid) var(--ease-out);
  }
  .fbtn.on .chev {
    transform: rotate(180deg);
  }
  .fpanel {
    display: flex;
    flex-direction: column;
    gap: 6px;
    margin-bottom: 10px;
    padding: 8px 10px 10px;
    background: var(--bg-1);
    box-shadow: inset 2px 0 0 var(--mint-lo);
  }
  .fh {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    margin-top: 2px;
    font-size: var(--fs-sm);
    font-weight: var(--fw-bold);
    color: var(--fg-2);
  }
  .fh em {
    margin-left: 6px;
    font-style: normal;
    font-weight: var(--fw-base);
    color: var(--fg-4);
  }
  .bonds {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(84px, 1fr));
    gap: 2px;
  }
  .bond {
    display: flex;
    align-items: center;
    gap: 4px;
    min-width: 0;
    height: 28px;
    padding: 0 4px;
    font-size: var(--fs-sm);
    color: var(--fg-3);
  }
  .bond span {
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .bond:hover {
    color: var(--fg-1);
    background: var(--bg-2);
  }
  .bond.on {
    color: var(--fg-0);
    background: var(--mint-tint);
    box-shadow: inset 0 0 0 1px var(--mint-lo);
  }
  .sph {
    display: flex;
    align-items: center;
    gap: 8px;
  }
  .sph input {
    flex: 1;
    min-width: 0;
    height: 28px;
    padding: 0 8px;
    font-size: var(--fs-base);
    color: var(--fg-1);
    background: var(--bg-0);
    border: 0;
    outline: none;
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .sph input:focus {
    box-shadow: inset 0 0 0 1px var(--mint-lo);
  }
  .stbtn {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    height: 30px;
    padding: 0 6px 0 8px;
    font-size: var(--fs-base);
    color: var(--fg-2);
    background: var(--bg-2);
  }
  .stbtn:hover {
    color: var(--fg-0);
    background: var(--bg-3);
  }
  .stbtn.on {
    padding-left: 3px;
    background: var(--mint-tint);
    box-shadow: inset 0 0 0 1px var(--mint-lo);
  }
  .stbtn img {
    width: 24px;
    height: 24px;
    object-fit: cover;
  }
  .stbtn b {
    color: var(--fg-0);
  }
  .stbtn span {
    font-size: var(--fs-sm);
    color: var(--fg-3);
  }
  .chips {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 4px;
  }
  .chips.active {
    margin: -2px 0 10px;
  }
  .chip {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    height: 24px;
    padding: 0 6px 0 3px;
    font-size: var(--fs-sm);
    color: var(--fg-1);
    background: var(--mint-tint);
    box-shadow: inset 0 0 0 1px var(--mint-lo);
  }
  .chip img {
    width: 18px;
    height: 18px;
    object-fit: cover;
  }
  .chip :global(svg) {
    color: var(--fg-3);
  }
  .chip:hover :global(svg) {
    color: var(--fg-0);
  }
  .link {
    font-size: var(--fs-sm);
    color: var(--mint);
  }
  .link:hover {
    text-decoration: underline;
    text-underline-offset: 2px;
  }
  .clear {
    align-self: flex-start;
    margin-top: 2px;
  }
  .list {
    display: flex;
    flex-direction: column;
    gap: 8px;
    margin-top: 10px;
  }
  .bar + .list,
  .chips.active + .list,
  .fpanel + .list {
    margin-top: 0;
  }
  .by {
    font-size: var(--fs-sm);
    color: var(--fg-3);
  }
  .num {
    display: inline-flex;
    align-items: center;
    gap: 2px;
    font-family: var(--font-term);
    font-size: var(--fs-sm);
    color: var(--fg-4);
  }
  .warn {
    font-size: var(--fs-xs);
    padding: 0 4px;
    color: var(--gold);
    box-shadow: inset 0 0 0 1px rgb(var(--gold-rgb) / 0.4);
  }
  .open {
    flex: none;
    align-self: flex-start;
    height: 24px;
    padding: 0 10px;
    font-size: var(--fs-base);
    color: var(--mint);
    box-shadow: inset 0 0 0 1px var(--mint-lo);
  }
  .open:hover {
    background: var(--mint-tint);
  }
  .open.done {
    color: var(--fg-3);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .bare {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 10px;
    background: var(--bg-2);
    box-shadow: inset 0 0 0 1px var(--line-soft);
  }
  .bare b {
    flex: 1;
    min-width: 0;
    overflow: hidden;
    white-space: nowrap;
    text-overflow: ellipsis;
  }
  .state {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 8px;
    padding: 32px 12px;
    text-align: center;
    font-size: var(--fs-base);
    color: var(--fg-3);
  }
  .state.small {
    padding: 14px 12px 4px;
  }
  .state p {
    margin: 0;
    line-height: 1.6;
  }
  .muted {
    color: var(--fg-4);
  }
  .retry,
  .more {
    height: 28px;
    padding: 0 12px;
    font-size: var(--fs-base);
    color: var(--fg-2);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .more {
    display: block;
    margin: 10px auto 0;
  }
  .note {
    margin: 10px 0 0;
    font-size: var(--fs-sm);
    color: var(--fg-4);
  }
  /* 宽（工作台）：卡片网格 */
  @container (min-width: 720px) {
    .list {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(340px, 1fr));
      align-items: stretch;
    }
  }
</style>
