<script lang="ts">
  import { app } from '../lib/state.svelte'
  import { api } from '../lib/api'
  import { plaza, postToLineup, PLAZA, type Post } from '../lib/plaza.svelte'
  import LineupCard from './LineupCard.svelte'
  import Icon from './Icon.svelte'

  // 阵容页的「广场」分段：网站上的阵容。默认按本局排（识别过本局时）：目标盟约阵容不完整的、名单被禁多的往后；
  // 也能切热门 / 最新 / 精选。点标题看详情、导入
  type Sort = 'match' | 'hot' | 'new' | 'featured'
  const hasMatch = $derived(app.match.source !== 'none')
  let sort = $state<Sort>(app.match.source !== 'none' ? 'match' : 'hot')
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
      const r = await plaza.list({ sort: sort === 'match' ? 'hot' : sort, cursor: more ? cursor : null })
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
  ]
</script>

<div class="bar">
  <div class="sorts">
    {#each sorts as s (s.v)}
      <button class:on={sort === s.v} disabled={s.v === 'match' && !hasMatch} title={s.v === 'match' && !hasMatch ? '还没识别本局，识别后按本局可用排序' : ''} onclick={() => (sort = s.v)}>{s.label}</button>
    {/each}
  </div>
  <button class="web" onclick={() => api.openUrl(PLAZA)} title="在浏览器里打开阵容广场">网站<Icon name="arrowRight" size={12} /></button>
</div>
<p class="acct">
  {#if plaza.me}
    已登录：<b>{plaza.me.nickname ?? '（还没起昵称）'}</b>。在「我的阵容」里右键阵容卡「发布到广场」。
  {:else}
    浏览不用登录；发布、点赞要登录，在设置里或发布时登录。
  {/if}
</p>

{#if error}
  <div class="state"><p>{error}</p><button class="retry" onclick={() => load()}>重试</button></div>
{:else if !shown.length && !loading}
  <div class="state"><p>{sort === 'featured' ? '还没有精选阵容。' : '广场里还没有阵容。在「我的阵容」里右键阵容卡「发布到广场」，做第一个分享的人。'}</p></div>
{:else}
  <div class="list">
    {#each shown as p (p.id)}
      <LineupCard lineup={postToLineup(p)} menu={false} ontitle={() => (plaza.viewing = p)}>
        {#snippet sub()}
          <span class="by">{p.author.nickname}</span>
          <span class="num"><Icon name={p.liked ? 'heartFill' : 'heart'} size={11} />{p.likes}</span>
          <span class="num"><Icon name="download" size={11} />{p.imports}</span>
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
  {#if sort === 'match'}<p class="note">本局排序取热门前 50 套里当期能用的，按本局盟约和禁用排。</p>{/if}
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
  .sorts button {
    height: 26px;
    padding: 0 10px;
    font-size: 12px;
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
    font-size: 12px;
    color: var(--mint);
  }
  .acct {
    margin: 0 0 10px;
    font-size: 11px;
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
    font-size: 11px;
    color: var(--fg-3);
  }
  .num {
    display: inline-flex;
    align-items: center;
    gap: 2px;
    font-family: var(--font-term);
    font-size: 11px;
    color: var(--fg-4);
  }
  .feat {
    font-size: 10px;
    padding: 0 4px;
    color: var(--gold-ink);
    background: var(--gold);
  }
  .open {
    flex: none;
    align-self: flex-start;
    height: 24px;
    padding: 0 10px;
    font-size: 12px;
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
    font-size: 12px;
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
    font-size: 12px;
    color: var(--fg-2);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .more {
    display: block;
    margin: 10px auto 0;
  }
  .note {
    margin: 10px 0 0;
    font-size: 11px;
    color: var(--fg-4);
  }
</style>
