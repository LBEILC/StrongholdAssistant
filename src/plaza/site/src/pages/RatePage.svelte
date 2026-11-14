<script lang="ts">
  import { untrack } from 'svelte'
  import { api, type RatingComment, type RatingDetail, type RatingKind, type RatingSummary } from '../api'
  import { ago, asset, eq, op, ROMAN } from '../data'
  import raw from '../../../../ui/src/lib/mock/catalog.json'
  import { link, router } from '../router.svelte'
  import { session, toast } from '../session.svelte'
  import Icon from '../components/Icon.svelte'
  import OpAvatar from '../components/OpAvatar.svelte'
  import ReportDialog from '../components/ReportDialog.svelte'
  import Stars from '../components/Stars.svelte'

  // 干员和装备评分（任务 131）：社区第一步。列表看大家的平均分，点开打分、写一句短评、看别人怎么说。
  // 本地开发加 ?demo 用示例数据
  let { kind, name }: { kind: RatingKind; name: string | null } = $props()
  const demo = import.meta.env.DEV && new URLSearchParams(location.search).has('demo')

  const cat = raw as unknown as { operators: { name: string; tier: number }[]; equipment: { name: string; tier: number }[] }
  const targets = $derived(kind === 'operator' ? cat.operators : cat.equipment)

  // ── 列表 ──
  let list = $state<Map<string, RatingSummary>>(new Map())
  let season = $state('')
  let mine = $state<Map<string, number>>(new Map())
  let loadErr = $state('')
  async function loadList() {
    loadErr = ''
    try {
      const r = demo ? demoList(kind) : await api.ratings(kind)
      season = r.season
      list = new Map(r.items.map((x) => [x.name, x]))
    } catch (e) {
      loadErr = (e as Error).message
    }
  }
  async function loadMine() {
    if (demo || !session.me) return void (mine = new Map())
    const r = await api.myRatings().catch(() => null)
    mine = new Map((r?.items ?? []).filter((x) => x.kind === kind).map((x) => [x.name, x.score]))
  }
  // 加载函数里读写同一份状态，放进 untrack，只让 kind、登录状态、name 触发重新加载
  $effect(() => {
    void kind
    untrack(loadList)
  })
  $effect(() => {
    void kind
    void session.me
    if (session.ready) untrack(loadMine)
  })

  type Sort = 'avg' | 'count' | 'tier'
  let sort = $state<Sort>('avg')
  let q = $state('')
  let onlyMine = $state(false)
  const rows = $derived.by(() => {
    const key = q.trim()
    const all = targets
      .filter((t) => !key || t.name.includes(key))
      .filter((t) => !onlyMine || mine.has(t.name))
      .map((t) => ({ ...t, s: list.get(t.name) }))
    const avg = (r: (typeof all)[number]) => r.s?.avg ?? -1
    const cnt = (r: (typeof all)[number]) => r.s?.count ?? 0
    return all.sort((a, b) =>
      sort === 'avg' ? avg(b) - avg(a) || cnt(b) - cnt(a) || b.tier - a.tier
      : sort === 'count' ? cnt(b) - cnt(a) || avg(b) - avg(a)
      : b.tier - a.tier || avg(b) - avg(a),
    )
  })
  const rated = $derived([...list.values()].reduce((a, x) => a + x.count, 0))

  function go(k: RatingKind, n: string | null) {
    const p = new URLSearchParams()
    if (k === 'equipment') p.set('kind', k)
    if (n) p.set('name', n)
    if (demo) p.set('demo', '')
    router.go('/rate' + (p.size ? '?' + p.toString().replace('demo=', 'demo') : ''))
  }

  // ── 详情 ──
  let detail = $state<RatingDetail | null>(null)
  let detailErr = $state('')
  let more = $state(false)
  async function loadDetail(n: string) {
    detail = null
    detailErr = ''
    try {
      detail = demo ? demoDetail(kind, n) : await api.rating(kind, n, null)
      draft = { score: detail.mine?.score ?? 0, comment: detail.mine?.comment ?? '' }
    } catch (e) {
      detailErr = (e as Error).message
    }
  }
  $effect(() => {
    const n = name
    if (n) untrack(() => loadDetail(n))
  })
  async function loadMore() {
    if (!detail?.nextCursor || !name) return
    more = true
    try {
      const r = await api.rating(kind, name, detail.nextCursor)
      detail = { ...detail, comments: [...detail.comments, ...r.comments], nextCursor: r.nextCursor }
    } finally {
      more = false
    }
  }

  let draft = $state<{ score: number; comment: string }>({ score: 0, comment: '' })
  let saving = $state(false)
  const changed = $derived(!!draft.score && (draft.score !== detail?.mine?.score || draft.comment.trim() !== (detail?.mine?.comment ?? '')))
  async function save() {
    if (!name || !detail || !draft.score) return
    saving = true
    try {
      const comment = draft.comment.trim()
      const s = demo ? detail.summary : await api.rate({ kind, name, score: draft.score as 1 | 2 | 3 | 4 | 5, comment })
      list.set(name, s)
      list = new Map(list)
      mine.set(name, draft.score)
      mine = new Map(mine)
      toast.show(detail.mine ? '评分已更新' : '谢谢你的评分')
      await loadDetail(name)
    } catch (e) {
      toast.show((e as Error).message)
    } finally {
      saving = false
    }
  }
  async function withdraw() {
    if (!name || !detail?.mine || !confirm('撤回你对「' + name + '」的评分？')) return
    saving = true
    try {
      if (!demo) await api.unrate(kind, name)
      mine.delete(name)
      mine = new Map(mine)
      toast.show('已撤回')
      await Promise.all([loadDetail(name), loadList()])
    } catch (e) {
      toast.show((e as Error).message)
    } finally {
      saving = false
    }
  }

  let reporting = $state<{ id: string; what: string } | null>(null)
  const pct = (n: number, s: RatingSummary) => (s.count ? Math.round((n / s.count) * 100) : 0)
  const tierOf = (n: string) => (kind === 'operator' ? op(n).tier : eq(n).tier)

  $effect(() => {
    const onkey = (e: KeyboardEvent) => e.key === 'Escape' && name && !reporting && go(kind, null)
    addEventListener('keydown', onkey)
    return () => removeEventListener('keydown', onkey)
  })

  // ── 示例数据 ──
  function demoSummary(k: RatingKind, n: string, i: number): RatingSummary {
    const count = [0, 3, 8, 26, 128][i % 5]
    const dist = [0, 0, 0, 0, 0].map((_, j) => Math.round((count * [1, 2, 4, 6, 3][(j + i) % 5]) / 16)) as RatingSummary['dist']
    const c = dist.reduce((a, x) => a + x, 0)
    return { kind: k, name: n, count: c, avg: c >= 5 ? Math.round((dist.reduce((a, x, j) => a + x * (j + 1), 0) / c) * 10) / 10 : null, dist }
  }
  function demoList(k: RatingKind) {
    const src = k === 'operator' ? cat.operators : cat.equipment
    return { season: '2026-S1', kind: k, items: src.slice(0, 40).map((t, i) => demoSummary(k, t.name, i)).filter((x) => x.count), at: new Date().toISOString() }
  }
  function demoDetail(k: RatingKind, n: string): RatingDetail {
    const i = (k === 'operator' ? cat.operators : cat.equipment).findIndex((t) => t.name === n)
    const say = ['开局拿到直接起飞，中期也能扛。', '强度在线，但很吃装备。', '一般般，凑盟约可以带。', '后期乏力，不太推荐。', '这期环境下属于第一梯队。']
    const comments: RatingComment[] = say.map((c, j) => ({ id: 'd' + j, author: { id: 'u' + j, nickname: ['Kuzuri', '路北路陈', '一只鸽子', '卫戍新人', '夜莺'][j] }, score: (5 - (j % 3)) as 1 | 2 | 3 | 4 | 5, comment: c, createdAt: new Date(Date.now() - j * 7e6).toISOString(), updatedAt: new Date(Date.now() - j * 7e6).toISOString() }))
    return { season: '2026-S1', summary: demoSummary(k, n, Math.max(0, i) + 4), comments, nextCursor: null, mine: null }
  }
</script>


<section class="hero">
  <div class="in">
    <h1>评分<span class="term">Ratings</span>{#if season}<span class="ss term">赛季 {season}</span>{/if}</h1>
    <p>给干员和装备打 1～5 星，写一句你的看法。按赛季统计，满 5 人显示平均分。{#if rated}这个赛季已经有 <b class="term">{rated.toLocaleString('zh-CN')}</b> 次评分。{/if}</p>
  </div>
</section>

<div class="in">
  <div class="bar">
    <div class="seg">
      <button class:on={kind === 'operator'} onclick={() => go('operator', null)}>干员</button>
      <button class:on={kind === 'equipment'} onclick={() => go('equipment', null)}>装备</button>
    </div>
    <label class="search"><Icon name="search" size={15} /><input bind:value={q} placeholder={kind === 'operator' ? '搜干员名字' : '搜装备名字'} /></label>
    <span class="sp"></span>
    <div class="sorts">
      <span class="fl">排序</span>
      {#each [['avg', '评分'], ['count', '人数'], ['tier', '等阶']] as [v, l] (v)}
        <button class="chip" class:on={sort === v} onclick={() => (sort = v as Sort)}>{l}</button>
      {/each}
      {#if session.me && mine.size}<button class="chip" class:on={onlyMine} onclick={() => (onlyMine = !onlyMine)}>我评过的 {mine.size}</button>{/if}
    </div>
  </div>

  {#if loadErr}
    <p class="err">{loadErr} <button class="link" onclick={loadList}>重试</button></p>
  {/if}

  <ul class="grid">
    {#each rows as r (r.name)}
      <li>
        <a class="card" href="/rate?{kind === 'equipment' ? 'kind=equipment&' : ''}name={encodeURIComponent(r.name)}" onclick={(e) => { e.preventDefault(); go(kind, r.name) }} style:--c="var(--tier-{r.tier})">
          {#if kind === 'operator'}
            <OpAvatar name={r.name} size={52} />
          {:else}
            <span class="eqi"><img src={asset.equipment(r.name)} alt="" loading="lazy" /></span>
          {/if}
          <span class="cb">
            <span class="cn">{r.name}<em class="term">{ROMAN[r.tier]}</em></span>
            {#if r.s?.avg != null}
              <span class="cs"><b class="term">{r.s.avg.toFixed(1)}</b><Stars value={r.s.avg} size={13} /></span>
              <span class="cc">{r.s.count} 人评分</span>
            {:else}
              <span class="cs none">{r.s?.count ? `${r.s.count} 人评分，满 5 人显示` : '还没有人评分'}</span>
            {/if}
          </span>
          {#if mine.has(r.name)}<span class="my term" title="你给了 {mine.get(r.name)} 星">{mine.get(r.name)}<Icon name="star" size={11} /></span>{/if}
        </a>
      </li>
    {/each}
  </ul>
  {#if !rows.length}<p class="muted empty">{onlyMine ? '你这个赛季还没评过' : '没有找到「' + q.trim() + '」'}</p>{/if}
</div>

{#if name}
  <div class="scrim" onclick={() => go(kind, null)} aria-hidden="true"></div>
  <aside class="sheet" aria-label="{name}的评分">
    <header>
      {#if kind === 'operator'}<OpAvatar name={name} size={56} />{:else}<span class="eqi big" style:--c="var(--tier-{tierOf(name)})"><img src={asset.equipment(name)} alt="" /></span>{/if}
      <div class="ht">
        <b>{name}</b>
        <span class="muted">{kind === 'operator' ? '干员' : '装备'} · {ROMAN[tierOf(name)]} 阶{#if detail} · 赛季 {detail.season}{/if}</span>
      </div>
      <button class="x" onclick={() => go(kind, null)} aria-label="关闭"><Icon name="close" size={20} /></button>
    </header>

    {#if detailErr}
      <p class="err">{detailErr}</p>
    {:else if !detail}
      <p class="muted pad">加载中…</p>
    {:else}
      {@const s = detail.summary}
      <section class="sum">
        <div class="big">
          <b class="term">{s.avg != null ? s.avg.toFixed(1) : '—'}</b>
          <Stars value={s.avg ?? 0} size={16} />
          <span class="muted">{s.avg != null ? `${s.count} 人评分` : `${s.count}/5 人，满 5 人显示平均分`}</span>
        </div>
        <ol class="dist">
          {#each [4, 3, 2, 1, 0] as i (i)}
            <li><span class="term">{i + 1}<Icon name="star" size={10} /></span><span class="db"><i style:width="{pct(s.dist[i], s)}%"></i></span><span class="dn term">{s.dist[i]}</span></li>
          {/each}
        </ol>
      </section>

      <section class="mine">
        <h3>{detail.mine ? '你的评分' : '你觉得怎么样？'}</h3>
        {#if !session.ready}
          <p class="muted">…</p>
        {:else if !session.me && !demo}
          <a class="btn primary" href="/login?next={encodeURIComponent(location.pathname + location.search)}" use:link>登录后评分</a>
        {:else if session.me && !session.me.nickname}
          <p class="gate">先到<a href="/me" use:link>「我的」</a>起个昵称，再来评分。</p>
        {:else}
          <Stars value={draft.score} size={26} onpick={(n) => (draft.score = n)} />
          <input class="cm" bind:value={draft.comment} maxlength="100" placeholder="一句话说说理由（选填，100 字以内）" onkeydown={(e) => e.key === 'Enter' && changed && save()} />
          <div class="acts">
            <button class="btn primary" disabled={!changed || saving} onclick={save}>{detail.mine ? '更新评分' : '提交评分'}</button>
            {#if detail.mine}<button class="btn ghost" disabled={saving} onclick={withdraw}>撤回</button><span class="muted small">{ago(detail.mine.updatedAt)}评的</span>{/if}
            <span class="sp"></span>
            <span class="muted small term">{draft.comment.trim().length}/100</span>
          </div>
        {/if}
      </section>

      <section class="cms">
        <h3>大家怎么说<span class="muted">{detail.comments.length ? '' : ' · 还没有短评'}</span></h3>
        <ul>
          {#each detail.comments as c (c.id)}
            <li>
              <div class="ch">
                <Stars value={c.score} size={12} />
                <b class:me={c.author.id === session.me?.id}>{c.author.nickname}</b>
                <span class="muted small">{ago(c.updatedAt)}</span>
                <span class="sp"></span>
                {#if c.author.id !== session.me?.id}<button class="rep" title="举报这条短评" onclick={() => (reporting = { id: c.id, what: `${name} · ${c.author.nickname}` })}><Icon name="flag" size={13} /></button>{/if}
              </div>
              <p>{c.comment}</p>
            </li>
          {/each}
        </ul>
        {#if detail.nextCursor}<button class="btn ghost sm" disabled={more} onclick={loadMore}>{more ? '加载中…' : '更多短评'}</button>{/if}
      </section>
    {/if}
  </aside>
{/if}

{#if reporting}
  <ReportDialog target={{ kind: 'rating', id: reporting.id }} what={reporting.what} onclose={() => (reporting = null)} />
{/if}

<style>
  .in {
    width: min(1200px, 100% - 32px);
    margin: 0 auto;
  }
  .hero {
    padding: 36px 0 20px;
  }
  h1 {
    display: flex;
    align-items: baseline;
    flex-wrap: wrap;
    gap: 12px;
    margin: 0 0 8px;
    font-size: 28px;
    letter-spacing: 0.08em;
  }
  h1 .term {
    font-size: 12px;
    font-weight: 400;
    letter-spacing: 0.04em;
    color: var(--mint);
  }
  h1 .ss {
    padding: 2px 8px;
    color: var(--gold);
    box-shadow: inset 0 0 0 1px color-mix(in srgb, var(--gold) 45%, transparent);
  }
  .hero p {
    max-width: 720px;
    margin: 0;
    color: var(--fg-2);
    line-height: 1.7;
  }
  .hero b {
    color: var(--fg-1);
  }
  .bar {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 10px 14px;
    margin-bottom: 14px;
  }
  .seg {
    display: flex;
    gap: 2px;
  }
  .seg button {
    height: 32px;
    padding: 0 18px;
    font-size: 14px;
    color: var(--fg-2);
    background: var(--bg-2);
  }
  .seg button.on {
    color: var(--mint-ink);
    background: var(--mint);
    font-weight: 700;
  }
  .search {
    display: flex;
    align-items: center;
    gap: 6px;
    width: 220px;
    padding: 0 10px;
    color: var(--fg-4);
    background: var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line-soft, var(--line));
  }
  .search input {
    flex: 1;
    min-width: 0;
    height: 32px;
    padding: 0;
    background: none;
    border: 0;
    box-shadow: none;
  }
  .sp {
    flex: 1;
  }
  .sorts {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 4px;
  }
  .fl {
    margin-right: 4px;
    font-size: 12px;
    color: var(--fg-4);
  }
  .chip {
    height: 28px;
    padding: 0 12px;
    font-size: 13px;
    color: var(--fg-2);
    background: var(--bg-2);
  }
  .chip.on {
    color: var(--mint-ink);
    background: var(--mint);
    font-weight: 700;
  }
  .grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(250px, 1fr));
    gap: 8px;
    margin: 0 0 40px;
    padding: 0;
    list-style: none;
  }
  .card {
    position: relative;
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 10px 12px;
    background: var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line-soft, var(--line)), inset 3px 0 0 var(--c);
    transition: background 120ms, box-shadow 120ms;
  }
  .card:hover {
    background: var(--bg-2);
    box-shadow: inset 0 0 0 1px var(--line), inset 3px 0 0 var(--c);
  }
  .eqi {
    flex: none;
    display: grid;
    place-items: center;
    width: 52px;
    height: 52px;
    background: var(--bg-2);
    box-shadow: inset 0 -3px 0 var(--c);
  }
  .eqi img {
    width: 40px;
    height: 40px;
    object-fit: contain;
  }
  .eqi.big {
    width: 56px;
    height: 56px;
  }
  .cb {
    display: flex;
    flex-direction: column;
    gap: 3px;
    min-width: 0;
  }
  .cn {
    display: flex;
    align-items: baseline;
    gap: 6px;
    overflow: hidden;
    font-size: 15px;
    font-weight: 700;
    color: var(--fg-1);
    white-space: nowrap;
    text-overflow: ellipsis;
  }
  .cn em {
    font-style: normal;
    font-size: 11px;
    font-weight: 400;
    color: var(--c);
  }
  .cs {
    display: flex;
    align-items: center;
    gap: 6px;
  }
  .cs b {
    font-size: 17px;
    line-height: 1;
    color: var(--gold);
  }
  .cs.none,
  .cc {
    font-size: 12px;
    color: var(--fg-4);
  }
  .my {
    position: absolute;
    top: 8px;
    right: 10px;
    display: inline-flex;
    align-items: center;
    gap: 2px;
    padding: 1px 6px;
    font-size: 12px;
    color: var(--gold-ink);
    background: var(--gold);
  }
  .empty {
    padding: 40px 0;
    text-align: center;
  }

  /* 详情：桌面是右侧抽屉，手机是底部抽屉 */
  .scrim {
    position: fixed;
    inset: 0;
    z-index: 40;
    background: rgb(0 0 0 / 0.5);
  }
  .sheet {
    position: fixed;
    top: 0;
    right: 0;
    bottom: 0;
    z-index: 41;
    display: flex;
    flex-direction: column;
    gap: 22px;
    width: min(460px, 100%);
    overflow-y: auto;
    padding: 20px 22px 32px;
    background: var(--bg-0, var(--bg-1));
    box-shadow: -1px 0 0 var(--line), -24px 0 48px rgb(0 0 0 / 0.4);
    animation: in 180ms ease-out;
  }
  @keyframes in {
    from {
      transform: translateX(24px);
      opacity: 0;
    }
  }
  .sheet header {
    display: flex;
    align-items: center;
    gap: 14px;
  }
  .ht {
    display: flex;
    flex-direction: column;
    gap: 2px;
    min-width: 0;
  }
  .ht b {
    font-size: 20px;
    color: var(--fg-1);
  }
  .ht .muted {
    font-size: 13px;
  }
  .x {
    margin-left: auto;
    align-self: flex-start;
    padding: 4px;
    color: var(--fg-3);
    background: none;
  }
  .pad {
    padding: 20px 0;
  }
  .sum {
    display: grid;
    grid-template-columns: auto 1fr;
    gap: 20px;
    align-items: center;
    padding: 16px;
    background: var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line-soft, var(--line));
  }
  .big {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 6px;
    min-width: 108px;
  }
  .big b {
    font-size: 44px;
    line-height: 1;
    color: var(--gold);
  }
  .big .muted {
    font-size: 12px;
    text-align: center;
  }
  .dist {
    display: flex;
    flex-direction: column;
    gap: 5px;
    margin: 0;
    padding: 0;
    list-style: none;
  }
  .dist li {
    display: grid;
    grid-template-columns: 26px 1fr 34px;
    align-items: center;
    gap: 8px;
    font-size: 12px;
    color: var(--fg-3);
  }
  .dist li > span:first-child {
    display: inline-flex;
    align-items: center;
    gap: 2px;
  }
  .db {
    height: 6px;
    background: var(--bg-2);
  }
  .db i {
    display: block;
    height: 100%;
    background: var(--gold);
  }
  .dn {
    text-align: right;
  }
  h3 {
    margin: 0 0 10px;
    font-size: 14px;
    color: var(--fg-1);
  }
  h3 .muted {
    font-weight: 400;
  }
  .mine {
    display: flex;
    flex-direction: column;
    align-items: flex-start;
    gap: 10px;
  }
  .mine h3 {
    margin: 0;
  }
  .cm {
    width: 100%;
  }
  .acts {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 8px;
    width: 100%;
  }
  .gate a {
    color: var(--mint);
  }
  .small {
    font-size: 12px;
  }
  .cms ul {
    display: flex;
    flex-direction: column;
    margin: 0 0 12px;
    padding: 0;
    list-style: none;
  }
  .cms li {
    padding: 12px 0;
    border-top: 1px solid var(--line-soft, var(--line));
  }
  .ch {
    display: flex;
    align-items: center;
    gap: 8px;
  }
  .ch b {
    font-size: 13px;
    color: var(--fg-2);
  }
  .ch b.me {
    color: var(--mint);
  }
  .cms p {
    margin: 6px 0 0;
    color: var(--fg-1);
    line-height: 1.6;
    word-break: break-word;
  }
  .rep {
    padding: 2px;
    color: var(--fg-4);
    background: none;
  }
  .rep:hover {
    color: #f2b8ba;
  }
  @media (max-width: 640px) {
    .search {
      flex: 1;
      width: auto;
    }
    .sheet {
      top: auto;
      max-height: 88vh;
      width: 100%;
      padding: 18px 16px 28px;
      box-shadow: 0 -1px 0 var(--line), 0 -24px 48px rgb(0 0 0 / 0.4);
      animation-name: up;
    }
    @keyframes up {
      from {
        transform: translateY(24px);
        opacity: 0;
      }
    }
    .sum {
      grid-template-columns: 1fr;
      gap: 14px;
    }
  }
</style>
