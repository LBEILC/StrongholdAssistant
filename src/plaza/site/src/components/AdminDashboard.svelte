<script lang="ts">
  import { api, type AdminStats } from '../api'
  import type { DailyCount } from '../../../shared/api'
  import { link } from '../router.svelte'

  // 后台看板（v0.9.3）：累计数字、近 30 天每天新增（每项一张小柱状图）、近 7 天排行。
  // 本地开发加 ?demo 用示例数据
  let { onreports }: { onreports: () => void } = $props()

  let stats = $state<AdminStats | null>(null)
  let error = $state('')
  const demo = import.meta.env.DEV && new URLSearchParams(location.search).has('demo')
  async function load() {
    error = ''
    try {
      stats = demo ? demoStats() : await api.admin.stats()
    } catch (e) {
      error = (e as Error).message
    }
  }
  load()

  const fmt = (n: number) => n.toLocaleString('zh-CN')
  const last = (d: DailyCount[]) => d[d.length - 1]?.count ?? null
  const sum = (d: DailyCount[]) => d.reduce((a, x) => a + (x.count ?? 0), 0)
  const recorded = (d: DailyCount[]) => d.filter((x) => x.count !== null).length

  const tiles = $derived(
    stats && [
      { label: '账号', value: stats.totals.users, today: last(stats.daily.users) },
      { label: '阵容', value: stats.totals.posts, today: last(stats.daily.posts) },
      { label: '导入', value: stats.totals.imports, today: last(stats.daily.imports) },
      { label: '点赞', value: stats.totals.likes, today: last(stats.daily.likes) },
      { label: '评论', value: stats.totals.comments, today: last(stats.daily.comments) },
      { label: '收藏', value: stats.totals.favorites, today: null },
      { label: '打开云同步', value: stats.totals.cloudSync, today: null },
    ],
  )
  const charts = $derived(
    stats && [
      { label: '新账号', d: stats.daily.users },
      { label: '新阵容', d: stats.daily.posts },
      { label: '导入', d: stats.daily.imports },
      { label: '点赞', d: stats.daily.likes },
      { label: '评论', d: stats.daily.comments },
      { label: '资金表上报（局）', d: stats.daily.incomeGames, note: '只统计打开了「帮大家补全资金表」的人' },
    ],
  )

  // 柱状图几何：viewBox 宽 300、高 96，底部留 16 给日期
  const W = 300, H = 96, PAD_B = 16, PAD_T = 12
  function niceMax(d: DailyCount[]) {
    const m = Math.max(1, ...d.map((x) => x.count ?? 0))
    const step = 10 ** Math.floor(Math.log10(m))
    return Math.ceil(m / step) * step
  }
  let hover = $state<{ chart: number; i: number } | null>(null)
  const md = (date: string) => date.slice(5).replace('-', '/')

  function demoStats(): AdminStats {
    const days = Array.from({ length: 30 }, (_, i) => new Date(Date.now() - (29 - i) * 864e5 + 8 * 36e5).toISOString().slice(0, 10))
    const rnd = (k: number, from = 0) => days.map((date, i) => ({ date, count: i < from ? null : Math.round(Math.max(0, k * (0.4 + Math.sin(i / 3) * 0.3 + i / 40) + Math.random() * k * 0.5)) }))
    const rank = (k: number) => Array.from({ length: 10 }, (_, i) => ({ post: { id: 'demo-' + i, name: ['叙拉古速攻', '莱塔尼亚法术流', '卡西米尔骑士', '萨尔贡续航', '雷姆必拓爆破', '维多利亚阵地', '乌萨斯铁卫', '东国快攻', '炎国均衡', '拉特兰射手'][i], author: ['能天使', '史尔特尔', '临光', '星熊', '推进之王', '陈', '凯尔希', '银灰', '阿米娅', '德克萨斯'][i] }, count: Math.round(k / (i + 1)) }))
    return {
      totals: { users: 1286, posts: 342, imports: 9821, likes: 2210, comments: 486, favorites: 731, cloudSync: 214, openReports: 3 },
      daily: { users: rnd(12), posts: rnd(5), imports: rnd(80, 29), likes: rnd(30, 29), comments: rnd(8), incomeGames: rnd(60) },
      topImported: rank(120),
      topLiked: rank(40),
      at: new Date().toISOString(),
    }
  }
</script>

{#if error && !stats}
  <p class="err">{error} <button class="link" onclick={load}>重试</button></p>
{:else if !stats}
  <p class="muted">加载中…</p>
{:else}
  <div class="tiles">
    {#each tiles! as t (t.label)}
      <div class="tile">
        <span class="tl">{t.label}</span>
        <b class="tv term">{fmt(t.value)}</b>
        {#if t.today !== null}<span class="td">今天 +{fmt(t.today)}</span>{/if}
      </div>
    {/each}
    <button class="tile rep" class:has={stats.totals.openReports > 0} onclick={onreports}>
      <span class="tl">待处理举报</span>
      <b class="tv term">{fmt(stats.totals.openReports)}</b>
      <span class="td">{stats.totals.openReports ? '去处理 →' : '没有'}</span>
    </button>
  </div>

  <h2 class="sec">近 30 天每天新增<small>北京时间 · 鼠标移到柱子上看当天的数</small></h2>
  <div class="charts">
    {#each charts! as c, ci (c.label)}
      {@const max = niceMax(c.d)}
      {@const bw = W / c.d.length}
      {@const firstRec = c.d.findIndex((x) => x.count !== null)}
      <figure class="chart">
        <figcaption>
          <span>{c.label}</span>
          <em>{#if recorded(c.d) === 0}还没有记录{:else}30 天 {fmt(sum(c.d))}{#if recorded(c.d) < c.d.length}（只有最近 {recorded(c.d)} 天的记录）{/if}{/if}</em>
        </figcaption>
        <div class="plot" role="img" aria-label="{c.label}近 30 天每天的数">
          <svg viewBox="0 0 {W} {H}" preserveAspectRatio="none" onmouseleave={() => (hover = null)}>
            <!-- 顶部一条参考线，标最大刻度 -->
            <line class="grid" x1="0" x2={W} y1={PAD_T} y2={PAD_T} />
            <line class="base" x1="0" x2={W} y1={H - PAD_B} y2={H - PAD_B} />
            {#if firstRec !== 0}<rect class="none" x="0" y={PAD_T} width={(firstRec < 0 ? c.d.length : firstRec) * bw} height={H - PAD_B - PAD_T} />{/if}
            {#each c.d as x, i (x.date)}
              {@const h = x.count ? ((H - PAD_B - PAD_T) * x.count) / max : 0}
              {#if h > 0}
                <path class="bar" class:on={hover?.chart === ci && hover.i === i} d="M{i * bw + 1},{H - PAD_B} v{-(h - Math.min(2, h))} q0,-{Math.min(2, h)} {Math.min(2, h)},-{Math.min(2, h)} h{bw - 2 - 2 * Math.min(2, h)} q{Math.min(2, h)},0 {Math.min(2, h)},{Math.min(2, h)} v{h - Math.min(2, h)} z" />
              {/if}
              <!-- 命中区比柱子大：整列都能悬停 -->
              <rect class="hit" x={i * bw} y="0" width={bw} height={H - PAD_B} onmouseenter={() => (hover = { chart: ci, i })} role="presentation" />
            {/each}
          </svg>
          <span class="ymax term">{fmt(max)}</span>
          {#if firstRec > 0}<span class="since" style:width="{(firstRec / c.d.length) * 100}%">{md(c.d[firstRec].date)} 起才开始记录</span>{/if}
          {#if firstRec < 0}<span class="since" style:width="100%">还没开始记录</span>{/if}
          <div class="xl"><span>{md(c.d[0].date)}</span><span>{md(c.d[c.d.length - 1].date)}</span></div>
          {#if hover?.chart === ci}
            {@const x = c.d[hover.i]}
            <div class="tip" style:left="{((hover.i + 0.5) / c.d.length) * 100}%" class:right={hover.i > c.d.length * 0.7} class:left={hover.i < c.d.length * 0.3}>
              <span>{x.date}</span><b>{x.count === null ? '还没开始记录' : fmt(x.count)}</b>
            </div>
          {/if}
        </div>
        {#if c.note}<p class="note">{c.note}</p>{/if}
      </figure>
    {/each}
  </div>

  <div class="ranks">
    {#each [{ t: '近 7 天导入最多', r: stats.topImported, unit: '次导入' }, { t: '近 7 天点赞最多', r: stats.topLiked, unit: '个赞' }] as g (g.t)}
      {@const top = Math.max(1, ...g.r.map((x) => x.count))}
      <section class="rank">
        <h2 class="sec">{g.t}</h2>
        {#if !g.r.length}
          <p class="muted small">近 7 天还没有记录。</p>
        {:else}
          <ol>
            {#each g.r as x (x.post.id)}
              <li>
                <a href="/p/{encodeURIComponent(x.post.id)}" use:link class="rn"><b>{x.post.name}</b><span>{x.post.author}</span></a>
                <span class="rb"><i style:width="{(x.count / top) * 100}%"></i></span>
                <span class="rc term" title="{fmt(x.count)} {g.unit}">{fmt(x.count)}</span>
              </li>
            {/each}
          </ol>
        {/if}
      </section>
    {/each}
  </div>

  <p class="foot muted small">
    网站访问量和流量在
    <a href="https://esa.console.aliyun.com/" target="_blank" rel="noopener" class="hl">阿里云 ESA 控制台</a>（国内访问走它）和
    <a href="https://dash.cloudflare.com/" target="_blank" rel="noopener" class="hl">Cloudflare 控制台</a>（后台和 pages.dev）里看。助手没有统计上报，看不到助手的用户数。
    · 数据生成于 {new Date(stats.at).toLocaleString('zh-CN', { hour12: false })}
    <button class="link" onclick={load}>刷新</button>
  </p>
{/if}

<style>
  .tiles {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
    gap: 8px;
    margin-bottom: 24px;
  }
  .tile {
    display: flex;
    flex-direction: column;
    gap: 2px;
    padding: 12px 14px;
    text-align: left;
    background: var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line-soft, var(--line));
  }
  .tl {
    font-size: 12px;
    color: var(--fg-3);
  }
  .tv {
    font-size: 26px;
    line-height: 1.2;
    color: var(--fg-1);
  }
  .td {
    font-size: 12px;
    color: var(--fg-3);
  }
  .rep {
    cursor: pointer;
  }
  .rep:hover {
    background: var(--bg-2);
  }
  .rep.has .tv {
    color: var(--gold);
  }
  .sec {
    display: flex;
    align-items: baseline;
    gap: 10px;
    margin: 0 0 10px;
    font-size: 15px;
    color: var(--fg-1);
  }
  .sec small {
    font-size: 12px;
    font-weight: 400;
    color: var(--fg-4);
  }
  .charts {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
    gap: 12px;
    margin-bottom: 28px;
  }
  .chart {
    margin: 0;
    padding: 12px 14px 10px;
    background: var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line-soft, var(--line));
  }
  figcaption {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    gap: 8px;
    margin-bottom: 8px;
    font-size: 13px;
    color: var(--fg-2);
  }
  figcaption em {
    font-style: normal;
    font-size: 12px;
    color: var(--fg-4);
  }
  .plot {
    position: relative;
  }
  svg {
    display: block;
    width: 100%;
    height: 110px;
  }
  .grid {
    stroke: var(--line);
    stroke-width: 1;
    stroke-dasharray: 2 3;
    vector-effect: non-scaling-stroke;
  }
  .base {
    stroke: var(--line);
    stroke-width: 1;
    vector-effect: non-scaling-stroke;
  }
  .none {
    fill: var(--bg-2);
    opacity: 0.6;
  }
  .bar {
    fill: var(--mint-lo);
  }
  .bar.on {
    fill: var(--mint);
  }
  .hit {
    fill: transparent;
  }
  .ymax {
    position: absolute;
    top: 0;
    left: 0;
    font-size: 11px;
    color: var(--fg-4);
    transform: translateY(-2px);
  }
  .since {
    position: absolute;
    left: 0;
    top: 12px;
    height: 76px;
    display: grid;
    place-items: center;
    font-size: 12px;
    color: var(--fg-4);
    pointer-events: none;
  }
  .xl {
    display: flex;
    justify-content: space-between;
    margin-top: -14px;
    font-size: 11px;
    color: var(--fg-4);
    pointer-events: none;
  }
  .tip {
    position: absolute;
    top: -6px;
    transform: translate(-50%, -100%);
    display: flex;
    flex-direction: column;
    align-items: center;
    padding: 4px 8px;
    font-size: 12px;
    white-space: nowrap;
    color: var(--fg-2);
    background: var(--bg-3, var(--bg-2));
    box-shadow: 0 0 0 1px var(--line), 0 4px 12px rgb(0 0 0 / 0.4);
    pointer-events: none;
    z-index: 2;
  }
  .tip.right {
    transform: translate(-100%, -100%);
  }
  .tip.left {
    transform: translate(0, -100%);
  }
  .tip b {
    font-size: 14px;
    color: var(--fg-1);
  }
  .note {
    margin: 6px 0 0;
    font-size: 11px;
    color: var(--fg-4);
  }
  .ranks {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
    gap: 24px;
    margin-bottom: 20px;
  }
  ol {
    display: flex;
    flex-direction: column;
    gap: 2px;
    margin: 0;
    padding: 0;
    list-style: none;
    counter-reset: r;
  }
  li {
    display: grid;
    grid-template-columns: minmax(0, 1.4fr) minmax(0, 1fr) auto;
    align-items: center;
    gap: 10px;
    padding: 6px 8px;
    font-size: 13px;
    counter-increment: r;
  }
  li:hover {
    background: var(--bg-1);
  }
  .rn {
    display: flex;
    align-items: baseline;
    gap: 6px;
    min-width: 0;
    color: inherit;
    text-decoration: none;
  }
  .rn::before {
    content: counter(r);
    width: 1.4em;
    flex: none;
    font-size: 12px;
    color: var(--fg-4);
  }
  .rn b {
    overflow: hidden;
    white-space: nowrap;
    text-overflow: ellipsis;
    color: var(--fg-1);
    font-weight: 600;
  }
  .rn:hover b {
    color: var(--mint);
  }
  .rn span {
    flex: none;
    font-size: 12px;
    color: var(--fg-4);
  }
  .rb {
    height: 8px;
    background: var(--bg-2);
  }
  .rb i {
    display: block;
    height: 100%;
    background: var(--mint-lo);
    border-radius: 0 2px 2px 0;
  }
  .rc {
    min-width: 3em;
    text-align: right;
    color: var(--fg-2);
  }
  .foot {
    line-height: 1.8;
  }
</style>
