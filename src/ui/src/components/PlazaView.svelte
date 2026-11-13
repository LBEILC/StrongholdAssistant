<script lang="ts">
  import { app } from '../lib/state.svelte'
  import { api } from '../lib/api'
  import { plaza, postToLineup, PLAZA, type Post } from '../lib/plaza.svelte'
  import { plazaPostMenu } from '../lib/menu.svelte'
  import LineupCard from './LineupCard.svelte'
  import Icon from './Icon.svelte'
  import BondIcon from './BondIcon.svelte'
  import StrategyGrid from './StrategyGrid.svelte'
  import { asset } from '../lib/assets'

  // 阵容页的「广场」分段：网站上的阵容。默认按本局排（识别过本局时）：目标盟约阵容不完整的、名单被禁多的往后；
  // 也能切热门 / 最新 / 精选 / 收藏（v0.7.0，要登录）。点标题看详情、导入
  type Sort = 'match' | 'hot' | 'new' | 'featured' | 'favorite'
  const hasMatch = $derived(app.match.source !== 'none')
  let sort = $state<Sort>(app.match.source !== 'none' ? 'match' : 'hot')
  let posts = $state<Post[]>([])
  let cursor = $state<string | null>(null)
  let loading = $state(false)
  let error = $state('')
  let seq = 0

  // 搜索和筛选（v0.9.1）：搜索框打字停 0.3 秒再查；筛选面板里点盟约（多选要全部包含）、选策略
  const f = plaza.filter
  let typed = $state(f.q)
  let timer: ReturnType<typeof setTimeout> | undefined
  const typing = (v: string) => {
    typed = v
    clearTimeout(timer)
    timer = setTimeout(() => (f.q = v.trim()), 300)
  }
  const clearSearch = () => {
    clearTimeout(timer)
    typed = f.q = ''
  }
  let panel = $state(false)
  const count = $derived(f.covenants.length + (f.strategy ? 1 : 0))
  const cov = (id: string) => app.catalog.covenants.find((c) => c.id === id)
  const toggleCov = (id: string) => (f.covenants = f.covenants.includes(id) ? f.covenants.filter((x) => x !== id) : [...f.covenants, id])
  const cores = app.catalog.covenants.filter((c) => c.kind === 'core')
  const extras = app.catalog.covenants.filter((c) => c.kind === 'extra')
  // 目标阵容当前阶段的目标盟约：一键填进筛选
  const targetCovs = $derived(app.active?.goals.map((g) => g.id) ?? [])
  const sameAsTarget = $derived(targetCovs.length > 0 && targetCovs.length === f.covenants.length && targetCovs.every((id) => f.covenants.includes(id)))
  let sq = $state('')
  let pickingSt = $state(false)
  const pickSt = (id: string) => {
    f.strategy = id === f.strategy ? null : id
    pickingSt = false
    sq = ''
  }
  const strat = $derived(app.catalog.strategies.find((s) => s.id === f.strategy))
  const clearFilters = () => {
    f.covenants = []
    f.strategy = null
  }

  async function load(more = false) {
    const my = ++seq
    // 收藏要登录：没登录时不请求，列表里给登录按钮
    if (sort === 'favorite' && !plaza.me) {
      posts = []
      cursor = null
      return
    }
    loading = true
    error = ''
    try {
      const r = await plaza.list({ sort: sort === 'match' ? 'hot' : sort, q: f.q, covenant: f.covenants, strategy: f.strategy, cursor: more ? cursor : null })
      if (my !== seq) return
      posts = more ? [...posts, ...r.posts] : r.posts
      cursor = r.cursor
    } catch (e) {
      if (my === seq) error = (e as Error).message
    } finally {
      if (my === seq) loading = false
    }
  }
  $effect(() => {
    void sort
    void plaza.me
    void [f.q, f.covenants, f.strategy]
    load()
  })
  $effect(() => {
    plaza.check()
  })

  // 本局排序在本地算：只看当期能导入的，按阵容适配度（不完整盟约 × 100 + 被禁人数）排，热度次之
  const shown = $derived(
    sort === 'match'
      ? posts
          .filter((p) => p.current)
          .map((p) => ({ p, pen: app.lineupPenalty(postToLineup(p)) }))
          .sort((a, b) => a.pen - b.pen)
          .map((x) => x.p)
      : posts,
  )
  const sorts: { v: Sort; label: string }[] = [
    { v: 'match', label: '本局' },
    { v: 'hot', label: '热门' },
    { v: 'new', label: '最新' },
    { v: 'featured', label: '精选' },
    { v: 'favorite', label: '收藏' },
  ]
</script>

<div class="search">
  <Icon name="search" size={14} />
  <input type="text" placeholder="搜阵容名、干员、装备、作者" value={typed} oninput={(e) => typing(e.currentTarget.value)} onkeydown={(e) => e.key === 'Escape' && typed && (e.stopPropagation(), clearSearch())} />
  {#if typed}<button class="x" title="清空搜索" aria-label="清空搜索" onclick={clearSearch}><Icon name="close" size={13} /></button>{/if}
</div>
<div class="bar">
  <div class="sorts">
    {#each sorts as s (s.v)}
      <button class:on={sort === s.v} disabled={s.v === 'match' && !hasMatch} title={s.v === 'match' && !hasMatch ? '还没识别本局，识别后按本局可用排序' : ''} onclick={() => (sort = s.v)}>{s.label}</button>
    {/each}
  </div>
  <button class="fbtn" class:on={panel} class:has={count > 0} aria-expanded={panel} onclick={() => (panel = !panel)}>
    筛选{#if count}<em>{count}</em>{/if}<span class="chev"><Icon name="arrowDown" size={12} /></span>
  </button>
  <button class="web" onclick={() => api.openUrl(PLAZA)} title="在浏览器里打开阵容广场">网站<Icon name="arrowRight" size={12} /></button>
</div>

{#if panel}
  <div class="fpanel">
    <div class="fh">
      <span>盟约<em>选多个时要全部包含</em></span>
      {#if targetCovs.length && !sameAsTarget}<button class="link" onclick={() => (f.covenants = [...targetCovs])}>用目标阵容的</button>{/if}
    </div>
    {#each [cores, extras] as group, gi (gi)}
      <div class="bonds">
        {#each group as c (c.id)}
          <button class="bond" class:on={f.covenants.includes(c.id)} title={c.name} onclick={() => toggleCov(c.id)}>
            <BondIcon id={c.id} size={22} on={f.covenants.includes(c.id)} />
            <span>{c.name}</span>
          </button>
        {/each}
      </div>
    {/each}
    <!-- 策略：和本局页选策略一样，点开是策略网格（可搜），点头像选上并收起；再点已选的那个取消 -->
    <div class="fh"><span>策略</span></div>
    {#if pickingSt}
      <div class="sph">
        <input bind:value={sq} placeholder="搜名字、发起者、效果" />
        <button class="link" onclick={() => ((pickingSt = false), (sq = ''))}>收起</button>
      </div>
      <StrategyGrid selected={f.strategy} q={sq} onpick={pickSt} />
    {:else}
      <div class="chips">
        <button class="stbtn" class:on={!!strat} onclick={() => (pickingSt = true)} title="点一下选策略">
          {#if strat}<img src={asset.strategy(strat.id)} alt="" /><b>{strat.name}</b><span>{strat.initiator}</span>{:else}<span>全部策略</span>{/if}
          <span class="chev"><Icon name="arrowDown" size={12} /></span>
        </button>
        {#if strat}<button class="link" onclick={() => (f.strategy = null)}>不限策略</button>{/if}
      </div>
    {/if}
    {#if count}<button class="link clear" onclick={clearFilters}>清除筛选</button>{/if}
  </div>
{:else if count}
  <!-- 面板收起时，已选的筛选排成一行，点 × 去掉 -->
  <div class="chips active">
    {#each f.covenants as id (id)}
      <button class="chip" title="去掉这个盟约" onclick={() => toggleCov(id)}><BondIcon {id} size={16} on />{cov(id)?.name}<Icon name="close" size={11} /></button>
    {/each}
    {#if strat}
      <button class="chip" title="取消策略筛选" onclick={() => (f.strategy = null)}><img src={asset.strategy(strat.id)} alt="" />{strat.name}<Icon name="close" size={11} /></button>
    {/if}
    <button class="link" onclick={clearFilters}>清除</button>
  </div>
{/if}
<p class="acct">
  {#if plaza.me}
    已登录：<b>{plaza.me.nickname ?? '（还没起昵称）'}</b>。在「我的阵容」里右键阵容卡「发布到广场」。
  {:else}
    浏览不用登录；发布、点赞要登录，在设置里或发布时登录。
  {/if}
</p>

{#if sort === 'favorite' && !plaza.me}
  <div class="state"><p>收藏跟着广场账号走，登录后在这里看收藏的阵容。</p><button class="retry" disabled={plaza.loggingIn} onclick={() => plaza.login().catch((e) => app.showToast(e.message, true))}>{plaza.loggingIn ? '等待登录…' : '登录'}</button></div>
{:else if error}
  <div class="state"><p>{error}</p><button class="retry" onclick={() => load()}>重试</button></div>
{:else if !shown.length && !loading}
  <div class="state"><p>{f.q || count ? '没有符合条件的阵容，换个关键词或筛选试试。' : sort === 'favorite' ? '还没有收藏。打开广场里的阵容，点「收藏」就会出现在这里。' : sort === 'featured' ? '还没有精选阵容。' : '广场里还没有阵容。在「我的阵容」里右键阵容卡「发布到广场」，做第一个分享的人。'}</p></div>
{:else}
  <div class="list">
    {#each shown as p (p.id)}
      <LineupCard lineup={postToLineup(p)} menu={() => plazaPostMenu(p)} ontitle={() => (plaza.viewing = p)}>
        {#snippet sub()}
          <span class="by">{p.author.nickname}</span>
          <span class="num"><Icon name={p.liked ? 'heartFill' : 'heart'} size={11} />{p.likes}</span>
          <span class="num"><Icon name="download" size={11} />{p.imports}</span>
          <span class="num"><Icon name="chat" size={11} />{p.comments}</span>
          {#if p.featured}<span class="feat">精选</span>{/if}
        {/snippet}
        {#snippet actions()}
          <button class="open" onclick={() => (plaza.viewing = p)}>查看</button>
        {/snippet}
      </LineupCard>
    {/each}
  </div>
  {#if cursor && sort !== 'match'}
    <button class="more" disabled={loading} onclick={() => load(true)}>{loading ? '加载中…' : '加载更多'}</button>
  {/if}
  {#if sort === 'match'}<p class="note">本局排序取热门前 50 套{f.q || count ? '符合条件的' : ''}里当期能用的，按本局盟约和禁用排。</p>{/if}
{/if}
{#if loading && !posts.length}<p class="state muted">加载中…</p>{/if}

<style>
  .bar {
    display: flex;
    align-items: center;
    gap: 8px;
    margin: 10px 0 6px;
  }
  .sorts {
    display: flex;
    gap: 2px;
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
  .search input::placeholder {
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
  .fbtn .chev {
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
    font-weight: 700;
    color: var(--fg-2);
  }
  .fh em {
    margin-left: 6px;
    font-style: normal;
    font-weight: 400;
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
  .sph input::placeholder {
    color: var(--fg-4);
  }
  /* 策略按钮：没选时写「全部策略」，选了显示头像、名字、发起者；点开是策略网格 */
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
  .stbtn .chev {
    display: grid;
    color: var(--fg-4);
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
    font-weight: 700;
  }
  .sorts button:disabled {
    color: var(--fg-4);
    cursor: default;
  }
  .web {
    margin-left: auto;
    display: inline-flex;
    align-items: center;
    gap: 2px;
    font-size: var(--fs-base);
    color: var(--mint);
  }
  .acct {
    margin: 0 0 10px;
    font-size: var(--fs-sm);
    color: var(--fg-4);
  }
  .acct b {
    color: var(--fg-2);
  }
  .list {
    display: flex;
    flex-direction: column;
    gap: 8px;
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
  .feat {
    font-size: var(--fs-xs);
    padding: 0 4px;
    color: var(--gold-ink);
    background: var(--gold);
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
  .state p {
    margin: 0;
    line-height: 1.6;
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
  /* 宽（工作台）：帖子卡网格 */
  @container (min-width: 720px) {
    .list {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(340px, 1fr));
      /* 同一行的卡片一样高 */
      align-items: stretch;
      gap: 8px;
    }
  }
</style>
