<script lang="ts">
  import { api, type AdminTrafficStats } from '../api'
  import type { AdminTrafficHost } from '../../../shared/api'
  import MiniBars from './MiniBars.svelte'

  // 后台看板的流量区块（任务 130）：经香港服务器的国内访问，每个网站每天的流量、访客和按精品流量单价估的费用。
  // 本地开发加 ?demo 用示例数据
  const demo = import.meta.env.DEV && new URLSearchParams(location.search).has('demo')

  const now = new Date(Date.now() + 8 * 36e5)
  const thisMonth = now.toISOString().slice(0, 7)
  const lastMonth = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() - 1, 1)).toISOString().slice(0, 7)
  let month = $state(thisMonth)

  let stats = $state<AdminTrafficStats | null>(null)
  let error = $state('')
  let loading = $state(false)
  async function load() {
    loading = true
    error = ''
    try {
      stats = demo ? demoStats(month) : await api.admin.traffic(month)
    } catch (e) {
      error = (e as Error).message
    } finally {
      loading = false
    }
  }
  $effect(() => {
    void month
    load()
  })

  // 1 GB = 1e9 字节，和计费口径一致
  const size = (n: number) => (n >= 1e9 ? (n / 1e9).toFixed(2) + ' GB' : n >= 1e6 ? (n / 1e6).toFixed(1) + ' MB' : Math.round(n / 1e3) + ' KB')
  const yuan = (n: number) => '¥' + n.toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
  const int = (n: number) => n.toLocaleString('zh-CN')
  const cost = (bytes: number) => (bytes / 1e9) * (stats?.pricePerGB ?? 3)
  const pct = (r: number | null) => (r === null ? '—' : Math.round(r * 100) + '%')

  // 上报每 15 分钟一次，超过 45 分钟没收到就提示
  const staleMin = $derived(stats && month === thisMonth ? (stats.lastReportAt ? Math.floor((Date.now() - Date.parse(stats.lastReportAt)) / 6e4) : Infinity) : 0)

  function demoStats(m: string): AdminTrafficStats {
    const days = Array.from({ length: 14 }, (_, i) => `${m}-${String(i + 1).padStart(2, '0')}`)
    const host = (name: string, k: number): AdminTrafficHost => {
      const daily = days.map((date, i) => {
        const on = i >= 12
        const visitors = on ? Math.round(k * 300 * (i - 11)) : 0
        return { date, visitors, requests: visitors * 40, bytes: visitors * 1.2e6 }
      })
      const bytes = daily.reduce((a, x) => a + x.bytes, 0)
      return {
        host: name, bytes, requests: daily.reduce((a, x) => a + x.requests, 0), daily,
        cacheHitRate: k ? 0.82 : null, status4xx: Math.round(k * 120), status5xx: k > 1 ? 3 : 0,
        topPaths: k ? [
          { path: '/assets/misans-gb2312-01-_RRyqU7c.woff2', requests: 900, bytes: bytes * 0.31 },
          { path: '/assets/index-DSq0DRkt.js', requests: 1100, bytes: bytes * 0.22 },
          { path: '/api/public/posts', requests: 5200, bytes: bytes * 0.12 },
          { path: '/assets/game/avatar/char_2025.png', requests: 2400, bytes: bytes * 0.05 },
          { path: '/', requests: 1300, bytes: bytes * 0.02 },
        ] : [],
      }
    }
    const hosts = [host('sa.lubeiluchen.com', 2), host('hk-test.lubeiluchen.com', 0.02), host('其他', 0)]
    const bytes = hosts.reduce((a, h) => a + h.bytes, 0)
    return { month: m, hosts, bytes, pricePerGB: 3, forecastBytes: bytes * 2.2, lastReportAt: new Date(Date.now() - 6 * 6e4).toISOString(), at: new Date().toISOString() }
  }
</script>

<section class="traffic">
  <div class="mh">
    <h2 class="sec">流量<small>经香港服务器的国内访问；费用按精品流量单价估算，以阿里云账单为准</small></h2>
    <span class="sp"></span>
    <button class="chip" class:on={month === thisMonth} onclick={() => (month = thisMonth)}>本月</button>
    <button class="chip" class:on={month === lastMonth} onclick={() => (month = lastMonth)}>上月</button>
    <a class="hl small" href="https://usercenter2.aliyun.com/finance/expense-report/expense-detail" target="_blank" rel="noopener">阿里云账单</a>
  </div>

  {#if error && !stats}
    <p class="err">{error} <button class="link" onclick={load}>重试</button></p>
  {:else if !stats}
    <p class="muted">加载中…</p>
  {:else}
    {#if staleMin > 45}
      <p class="warn small">{staleMin === Infinity ? '香港服务器还没上报过流量' : `香港服务器 ${staleMin >= 120 ? Math.floor(staleMin / 60) + ' 小时' : staleMin + ' 分钟'}没上报了，下面的数可能不全`}</p>
    {/if}
    <div class="tiles" class:dim={loading}>
      <div class="tile"><span class="tl">{month.replace('-', ' 年 ')} 月流量</span><b class="tv term">{size(stats.bytes)}</b><span class="td">{stats.hosts.length} 个网站合计</span></div>
      <div class="tile hi"><span class="tl">流量费</span><b class="tv term">{yuan(cost(stats.bytes))}</b><span class="td">{stats.pricePerGB} 元/GB</span></div>
      {#if month === thisMonth}
        <div class="tile"><span class="tl">预计全月</span><b class="tv term">{yuan(cost(stats.forecastBytes))}</b><span class="td">{size(stats.forecastBytes)} · 按已过天数估算</span></div>
      {/if}
    </div>

    <div class="hosts" class:dim={loading}>
      {#each stats.hosts as h (h.host)}
        <article class="host">
          <header>
            <b>{h.host}</b>
            <span class="hn">
              <em class="term">{size(h.bytes)}</em> · <em class="term hi">{yuan(cost(h.bytes))}</em> · 请求 <em class="term">{int(h.requests)}</em> · 缓存命中 <em class="term">{pct(h.cacheHitRate)}</em>
              {#if h.status5xx}<span class="bad"> · 5xx {int(h.status5xx)}</span>{/if}
            </span>
          </header>
          <div class="charts">
            <MiniBars data={h.daily.map((x) => ({ date: x.date, count: x.bytes }))} label="每天流量" summary={size(h.bytes)} fmt={size} />
            <MiniBars data={h.daily.map((x) => ({ date: x.date, count: x.visitors }))} label="每天访客" summary="按 IP 去重" />
            {#if h.topPaths.length}
              <figure class="top">
                <figcaption><span>流量最大的路径</span><em>本月前 {h.topPaths.length}</em></figcaption>
                <ol>
                  {#each h.topPaths as p (p.path)}
                    <li title="{p.path}&#10;{int(p.requests)} 次请求">
                      <span class="pp"><bdi>{p.path}</bdi></span>
                      <span class="pb"><i style:width="{(p.bytes / h.topPaths[0].bytes) * 100}%"></i></span>
                      <span class="pv term">{size(p.bytes)}</span>
                    </li>
                  {/each}
                </ol>
              </figure>
            {/if}
          </div>
        </article>
      {/each}
    </div>
    <p class="foot muted small">香港服务器每 15 分钟上报一次 · 生成于 {new Date(stats.at).toLocaleString('zh-CN', { hour12: false })} <button class="link" onclick={load}>刷新</button></p>
  {/if}
</section>

<style>
  .traffic {
    margin: 8px 0 28px;
    padding-top: 20px;
    border-top: 1px solid var(--line-soft, var(--line));
  }
  .mh {
    display: flex;
    align-items: baseline;
    gap: 6px;
    flex-wrap: wrap;
    margin-bottom: 10px;
  }
  .sec {
    display: flex;
    align-items: baseline;
    gap: 10px;
    margin: 0;
    font-size: 15px;
    color: var(--fg-1);
  }
  .sec small {
    font-size: 12px;
    font-weight: 400;
    color: var(--fg-4);
  }
  .sp {
    flex: 1;
  }
  .chip {
    height: 26px;
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
  .mh .hl {
    margin-left: 6px;
  }
  .warn {
    margin: 0 0 10px;
    padding: 8px 12px;
    color: var(--gold);
    background: var(--bg-1);
    box-shadow: inset 2px 0 0 var(--gold);
  }
  .dim {
    opacity: 0.55;
    transition: opacity 160ms;
  }
  .tiles {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
    gap: 8px;
    margin-bottom: 14px;
  }
  .tile {
    display: flex;
    flex-direction: column;
    gap: 2px;
    padding: 12px 14px;
    background: var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line-soft, var(--line));
  }
  .tl,
  .td {
    font-size: 12px;
    color: var(--fg-3);
  }
  .tv {
    font-size: 26px;
    line-height: 1.2;
    color: var(--fg-1);
  }
  .tile.hi .tv,
  em.hi {
    color: var(--gold);
  }
  .hosts {
    display: flex;
    flex-direction: column;
    gap: 14px;
  }
  .host header {
    display: flex;
    align-items: baseline;
    flex-wrap: wrap;
    gap: 4px 12px;
    margin-bottom: 6px;
  }
  .host header b {
    font-size: 14px;
    color: var(--fg-1);
  }
  .hn {
    font-size: 13px;
    color: var(--fg-3);
  }
  .hn em {
    font-style: normal;
    color: var(--fg-1);
  }
  .bad {
    color: var(--red-fg);
  }
  .charts {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
    gap: 12px;
  }
  .top {
    margin: 0;
    padding: 12px 14px 10px;
    background: var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line-soft, var(--line));
  }
  .top figcaption {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    margin-bottom: 8px;
    font-size: 13px;
    color: var(--fg-2);
  }
  .top figcaption em {
    font-style: normal;
    font-size: 12px;
    color: var(--fg-4);
  }
  .top ol {
    display: flex;
    flex-direction: column;
    gap: 6px;
    margin: 0;
    padding: 0;
    list-style: none;
  }
  .top li {
    display: grid;
    grid-template-columns: minmax(0, 1fr) 64px auto;
    align-items: center;
    gap: 8px;
    font-size: 12px;
  }
  .pp {
    overflow: hidden;
    color: var(--fg-2);
    text-overflow: ellipsis;
    white-space: nowrap;
    /* 路径的关键在末尾（文件名）：省略号放左边；bdi 隔开，开头的斜杠不会被挪到末尾 */
    direction: rtl;
    text-align: left;
  }
  .pb {
    height: 6px;
    background: var(--bg-2);
  }
  .pb i {
    display: block;
    height: 100%;
    background: var(--mint-lo);
  }
  .pv {
    min-width: 58px;
    text-align: right;
    color: var(--fg-1);
  }
  .foot {
    margin-top: 10px;
  }
</style>
