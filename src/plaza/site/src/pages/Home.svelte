<script lang="ts">
  import { api, type Post } from '../api'
  import { link } from '../router.svelte'
  import { covenants, asset } from '../data'
  import PostCard from '../components/PostCard.svelte'
  import StrategyPicker from '../components/StrategyPicker.svelte'

  // 广场首页：左边筛选（排序、盟约、策略），右边阵容卡。筛选写进地址栏，刷新、分享链接都能还原
  type Sort = 'hot' | 'new' | 'featured'
  const q0 = new URLSearchParams(location.search)
  let sort = $state<Sort>((['hot', 'new', 'featured'] as const).find((s) => s === q0.get('sort')) ?? 'hot')
  let picked = $state<string[]>(q0.getAll('covenant'))
  let strat = $state<string | null>(q0.get('strategy'))
  // 搜索（v0.9.1）：打字停 0.3 秒再查；写进地址栏 ?q=
  let kw = $state(q0.get('q') ?? '')
  let typed = $state(kw)
  let timer: ReturnType<typeof setTimeout> | undefined
  const typing = (v: string) => {
    typed = v
    clearTimeout(timer)
    timer = setTimeout(() => (kw = v.trim()), 300)
  }
  const clearSearch = () => {
    clearTimeout(timer)
    typed = kw = ''
  }
  // 本地开发：?demo 显示示例阵容
  const demo = import.meta.env.DEV && q0.has('demo')

  let posts = $state<Post[]>([])
  let cursor = $state<string | null>(null)
  let loading = $state(false)
  let error = $state('')
  let seq = 0

  async function load(more = false) {
    const my = ++seq
    loading = true
    error = ''
    try {
      const r = demo
        ? { posts: (await import('../demo')).demoPosts.filter((p) => picked.every((c) => p.lineup.goals.some((g) => g.id === c)) && (!strat || p.lineup.strategyId === strat) && (!kw || JSON.stringify([p.lineup, p.author.nickname]).includes(kw))), cursor: null }
        : await api.posts({ sort, q: kw, covenant: picked, strategy: strat, cursor: more ? cursor : null })
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
    const p = new URLSearchParams()
    if (sort !== 'hot') p.set('sort', sort)
    if (kw) p.set('q', kw)
    for (const c of picked) p.append('covenant', c)
    if (strat) p.set('strategy', strat)
    if (demo) p.set('demo', '')
    const qs = p.toString()
    history.replaceState(null, '', qs ? `/?${qs}` : '/')
    load()
  })

  const toggle = (id: string) => (picked = picked.includes(id) ? picked.filter((x) => x !== id) : [...picked, id])
  const sorts: { v: Sort; label: string }[] = [
    { v: 'hot', label: '热门' },
    { v: 'new', label: '最新' },
    { v: 'featured', label: '精选' },
  ]
  const core = covenants.filter((c) => c.kind === 'core')
  const extra = covenants.filter((c) => c.kind === 'extra')
  const filtered = $derived(picked.length + (strat ? 1 : 0))
</script>

<section class="hero">
  <div class="in">
    <h1>阵容广场<span class="term">Lineup Plaza</span></h1>
    <p>看大家的卫戍协议阵容怎么搭。点开阵容复制分享码，到卫戍协议助手「阵容 → 我的阵容 → 导入」里粘贴就能用。想分享自己的阵容，可以在这里<a href="/new" use:link class="hl">新建阵容</a>，也可以在助手里右键阵容卡「发布到广场」。</p>
  </div>
</section>

<div class="in body">
  <aside class="filters">
    <div class="fg">
      <div class="fl">排序</div>
      <div class="seg">
        {#each sorts as s (s.v)}
          <button class:on={sort === s.v} onclick={() => (sort = s.v)}>{s.label}</button>
        {/each}
      </div>
    </div>
    <div class="fg">
      <div class="fl">核心盟约<em>选多个时要全部包含</em></div>
      <div class="bonds">
        {#each core as c (c.id)}
          <button class="bp" class:on={picked.includes(c.id)} title={c.name} onclick={() => toggle(c.id)}>
            <span class="glyph" style:--src="url({asset.bond(c.id)})"></span><span class="bn">{c.name}</span>
          </button>
        {/each}
      </div>
      <div class="fl">附加盟约</div>
      <div class="bonds">
        {#each extra as c (c.id)}
          <button class="bp" class:on={picked.includes(c.id)} title={c.name} onclick={() => toggle(c.id)}>
            <span class="glyph" style:--src="url({asset.bond(c.id)})"></span><span class="bn">{c.name}</span>
          </button>
        {/each}
      </div>
    </div>
    <div class="fg">
      <div class="fl">策略</div>
      <StrategyPicker bind:value={strat} />
    </div>
    {#if filtered}
      <button class="btn ghost sm clear" onclick={() => ((picked = []), (strat = null))}>清除筛选</button>
    {/if}
  </aside>

  <div class="list">
    <div class="search">
      <svg viewBox="0 0 16 16" width="16" height="16" aria-hidden="true"><circle cx="7" cy="7" r="4.5" fill="none" stroke="currentColor" stroke-width="1.6" /><path d="m10.5 10.5 3.5 3.5" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" /></svg>
      <input type="search" placeholder="搜阵容名、干员、装备、作者，空格分开多个词" value={typed} oninput={(e) => typing(e.currentTarget.value)} onkeydown={(e) => e.key === 'Escape' && clearSearch()} aria-label="搜索阵容" />
      {#if typed}<button class="x" onclick={clearSearch} aria-label="清空搜索">×</button>{/if}
    </div>
    {#if error}
      <div class="state"><p class="err">{error}</p><button class="btn ghost sm" onclick={() => load()}>重试</button></div>
    {:else if !posts.length && !loading}
      <div class="state">
        <p>{filtered || kw ? '没有符合条件的阵容，换个关键词或筛选试试。' : sort === 'featured' ? '还没有精选阵容。' : '广场里还没有阵容。点上面的「新建阵容」，或者在卫戍协议助手里右键阵容卡「发布到广场」。'}</p>
      </div>
    {:else}
      <div class="grid">
        {#each posts as p (p.id)}<PostCard post={p} />{/each}
      </div>
      {#if cursor}
        <div class="moreb"><button class="btn ghost" disabled={loading} onclick={() => load(true)}>{loading ? '加载中…' : '加载更多'}</button></div>
      {/if}
    {/if}
    {#if loading && !posts.length}<div class="state muted">加载中…</div>{/if}
  </div>
</div>

<style>
  .in {
    width: min(1200px, 100% - 32px);
    margin: 0 auto;
  }
  .hero {
    padding: 36px 0 24px;
  }
  h1 {
    display: flex;
    align-items: baseline;
    gap: 12px;
    margin: 0 0 8px;
    font-size: 28px;
    letter-spacing: 0.08em;
  }
  .hl {
    color: var(--mint);
    text-decoration: underline;
    text-underline-offset: 3px;
  }
  h1 .term {
    font-size: 12px;
    font-weight: 400;
    color: var(--mint);
  }
  .hero p {
    max-width: 720px;
    margin: 0;
    color: var(--fg-2);
    line-height: 1.7;
  }
  .body {
    display: grid;
    grid-template-columns: 260px 1fr;
    gap: 24px;
    align-items: start;
  }
  .filters {
    position: sticky;
    top: 72px;
    display: flex;
    flex-direction: column;
    gap: 18px;
    padding: 14px;
    background: var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line-soft);
  }
  .fl {
    display: flex;
    align-items: baseline;
    gap: 8px;
    margin: 0 0 6px;
    font-size: 12px;
    font-weight: 700;
    color: var(--fg-2);
  }
  .fl em {
    font-style: normal;
    font-weight: 400;
    font-size: 11px;
    color: var(--fg-4);
  }
  .bonds + .fl {
    margin-top: 12px;
  }
  .seg {
    display: grid;
    grid-template-columns: repeat(3, 1fr);
    gap: 2px;
  }
  .seg button {
    height: 30px;
    font-size: 13px;
    color: var(--fg-2);
    background: var(--bg-2);
  }
  .seg button:hover {
    color: var(--fg-1);
  }
  .seg button.on {
    color: var(--mint-ink);
    background: var(--mint);
    font-weight: 700;
  }
  .bonds {
    display: grid;
    grid-template-columns: repeat(2, 1fr);
    gap: 3px;
  }
  .bp {
    display: flex;
    align-items: center;
    gap: 6px;
    height: 28px;
    padding: 0 6px;
    font-size: 12.5px;
    color: var(--fg-3);
    background: var(--bg-2);
    text-align: left;
  }
  .bp .glyph {
    flex: none;
    width: 18px;
    height: 18px;
  }
  .bp:hover {
    color: var(--fg-1);
  }
  .bp.on {
    color: var(--mint);
    background: var(--mint-tint);
    box-shadow: inset 0 0 0 1px var(--mint-lo);
  }
  .bn {
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .clear {
    align-self: flex-start;
  }
  .search {
    display: flex;
    align-items: center;
    gap: 8px;
    height: 40px;
    margin-bottom: 14px;
    padding: 0 8px 0 12px;
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
    font: inherit;
    font-size: 15px;
    color: var(--fg-1);
    background: none;
    border: 0;
    outline: none;
  }
  .search input::placeholder {
    color: var(--fg-4);
  }
  .search input::-webkit-search-cancel-button {
    display: none;
  }
  .x {
    width: 26px;
    height: 26px;
    font-size: 18px;
    line-height: 1;
    color: var(--fg-3);
  }
  .x:hover {
    color: var(--fg-1);
    background: var(--bg-3);
  }
  .grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(330px, 1fr));
    gap: 14px;
  }
  .state {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 10px;
    padding: 60px 16px;
    text-align: center;
    color: var(--fg-3);
    background: var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line-soft);
  }
  .state p {
    max-width: 420px;
    margin: 0;
    line-height: 1.7;
  }
  .moreb {
    display: flex;
    justify-content: center;
    margin-top: 18px;
  }
  @media (max-width: 860px) {
    .body {
      grid-template-columns: 1fr;
    }
    .filters {
      position: static;
    }
    /* 窄屏筛选区在上面一长段：搜索框提到最前 */
    .list {
      display: contents;
    }
    .search {
      order: -1;
      margin-bottom: 0;
    }
    .bonds {
      grid-template-columns: repeat(auto-fill, minmax(96px, 1fr));
    }
    .grid {
      grid-template-columns: 1fr;
    }
  }
</style>
