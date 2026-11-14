<script lang="ts">
  import { api, type AdminMirrorStats } from '../api'
  import type { AdminMirrorProject } from '../../../shared/api'
  import MiniBars from './MiniBars.svelte'

  // 后台看板的 Mirror酱 区块（任务 124）：几个项目本月的销售、预计收益和每天的使用量。
  // key 在服务端，这里只拿汇总好的数。本地开发加 ?demo 用示例数据
  const demo = import.meta.env.DEV && new URLSearchParams(location.search).has('demo')

  // 北京时间的本月和上月，'YYYY-MM'
  const now = new Date(Date.now() + 8 * 36e5)
  const thisMonth = now.toISOString().slice(0, 7)
  const lastMonth = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() - 1, 1)).toISOString().slice(0, 7)
  let month = $state(thisMonth)

  let stats = $state<AdminMirrorStats | null>(null)
  let error = $state('')
  let loading = $state(false)
  async function load() {
    loading = true
    error = ''
    try {
      stats = demo ? demoStats(month) : await api.admin.mirror(month)
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

  const yuan = (n: number) => '¥' + n.toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
  const int = (n: number) => n.toLocaleString('zh-CN')
  const total = $derived.by(() => {
    const ok = stats?.projects.filter((p) => !p.error) ?? []
    const add = (k: 'orders' | 'amount' | 'income' | 'refundedOrders' | 'refundedAmount') => ok.reduce((a, p) => a + p[k], 0)
    return { orders: add('orders'), amount: add('amount'), income: add('income'), refundedOrders: add('refundedOrders'), refundedAmount: add('refundedAmount') }
  })
  const parts = (list: { name: string; orders: number }[]) => list.map((x) => `${x.name} ${x.orders} 份`).join(' · ')
  const platformName: Record<string, string> = { weixin: '微信', alipay: '支付宝', afdian: '爱发电', yimapay: '易码付' }
  const usageSum = (p: AdminMirrorProject, k: 'users' | 'downloads') => p.usage.reduce((a, x) => a + x[k], 0)

  function demoStats(m: string): AdminMirrorStats {
    const days = Array.from({ length: 14 }, (_, i) => `${m}-${String(i + 1).padStart(2, '0')}`)
    const usageDays = Array.from({ length: 30 }, (_, i) => new Date(Date.now() - (29 - i) * 864e5 + 8 * 36e5).toISOString().slice(0, 10))
    const project = (rid: string, k: number): AdminMirrorProject => {
      const daily = days.map((date, i) => {
        const orders = i < 12 ? 0 : Math.round(k * (i - 11) * 4)
        return { date, orders, amount: +(orders * 16.4).toFixed(2) }
      })
      const orders = daily.reduce((a, x) => a + x.orders, 0)
      const amount = daily.reduce((a, x) => a + x.amount, 0)
      return {
        rid, orders, amount, income: +(amount * 0.495).toFixed(2), refundedOrders: 0, refundedAmount: 0, daily,
        byPlan: orders ? [{ name: 'Mirror酱月卡', orders: Math.ceil(orders * 0.6), amount: amount * 0.4 }, { name: 'Mirror酱年卡', orders: Math.floor(orders * 0.4), amount: amount * 0.6 }] : [],
        byPlatform: orders ? [{ name: 'weixin', orders: Math.ceil(orders * 0.6), amount: amount * 0.6 }, { name: 'alipay', orders: Math.floor(orders * 0.4), amount: amount * 0.4 }] : [],
        usage: usageDays.map((date, i) => ({ date, users: i < 27 ? 0 : Math.round(k * 120 * (i - 26)), downloads: i < 27 ? 0 : Math.round(k * 90 * (i - 26)) })),
      }
    }
    return { month: m, projects: [project('StrongholdAssistant', 2), project('NovelAIPromptStudio', 0), { ...project('Pane', 0), error: '示例：这个项目查询失败' }], at: new Date().toISOString() }
  }
</script>

<section class="mirror">
  <div class="mh">
    <h2 class="sec">Mirror酱<small>扣支付平台费率后五五分成的预计收益，以 Mirror酱 结算账单为准</small></h2>
    <span class="sp"></span>
    <button class="chip" class:on={month === thisMonth} onclick={() => (month = thisMonth)}>本月</button>
    <button class="chip" class:on={month === lastMonth} onclick={() => (month = lastMonth)}>上月</button>
    <a class="hl small" href="https://mirrorchyan.com/zh/dashboard" target="_blank" rel="noopener">Mirror酱 面板</a>
  </div>

  {#if error && !stats}
    <p class="err">{error} <button class="link" onclick={load}>重试</button></p>
  {:else if !stats}
    <p class="muted">加载中…</p>
  {:else}
    <div class="tiles" class:dim={loading}>
      <div class="tile"><span class="tl">{month.replace('-', ' 年 ')} 月销售额</span><b class="tv term">{yuan(total.amount)}</b><span class="td">{int(total.orders)} 份</span></div>
      <div class="tile hi"><span class="tl">预计收益</span><b class="tv term">{yuan(total.income)}</b><span class="td">{stats.projects.length} 个项目合计</span></div>
      <div class="tile"><span class="tl">退款</span><b class="tv term">{yuan(total.refundedAmount)}</b><span class="td">{int(total.refundedOrders)} 份</span></div>
    </div>

    <div class="projects" class:dim={loading}>
      {#each stats.projects as p (p.rid)}
        <article class="proj">
          <header>
            <b>{p.rid}</b>
            {#if !p.error}
              <span class="pn"><em class="term">{yuan(p.amount)}</em> 销售额 · <em class="term">{int(p.orders)}</em> 份 · 预计收益 <em class="term hi">{yuan(p.income)}</em>{#if p.refundedOrders}<span class="rf"> · 退款 {int(p.refundedOrders)} 份</span>{/if}</span>
            {/if}
          </header>
          {#if p.error}
            <p class="err small">{p.error}</p>
          {:else}
            {#if p.orders}
              <p class="mix muted small">{parts(p.byPlan)}<br />{parts(p.byPlatform.map((x) => ({ ...x, name: platformName[x.name] ?? x.name })))}</p>
            {/if}
            <div class="charts">
              {#if p.orders}
                <MiniBars data={p.daily.map((x) => ({ date: x.date, count: x.amount }))} label="每天销售额" summary={`${int(p.orders)} 份`} fmt={yuan} />
              {:else}
                <div class="empty muted small">{month === thisMonth ? '本月' : '这个月'}还没有销售</div>
              {/if}
              <MiniBars data={p.usage.map((x) => ({ date: x.date, count: x.users }))} label="每天日活" summary="按 IP 估算 · 近 30 天" />
              <MiniBars data={p.usage.map((x) => ({ date: x.date, count: x.downloads }))} label="每天下载量" summary={`近 30 天 ${int(usageSum(p, 'downloads'))}`} />
            </div>
          {/if}
        </article>
      {/each}
    </div>
    <p class="foot muted small">数据来自 Mirror酱，5 分钟更新一次 · 生成于 {new Date(stats.at).toLocaleString('zh-CN', { hour12: false })} <button class="link" onclick={load}>刷新</button></p>
  {/if}
</section>

<style>
  .mirror {
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
  .mh .sec {
    margin: 0;
  }
  .sec {
    display: flex;
    align-items: baseline;
    gap: 10px;
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
  .tl {
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
  .td {
    font-size: 12px;
    color: var(--fg-3);
  }
  .projects {
    display: flex;
    flex-direction: column;
    gap: 14px;
  }
  .proj header {
    display: flex;
    align-items: baseline;
    flex-wrap: wrap;
    gap: 4px 12px;
    margin-bottom: 6px;
  }
  .proj header b {
    font-size: 14px;
    color: var(--fg-1);
  }
  .pn {
    font-size: 13px;
    color: var(--fg-3);
  }
  .pn em {
    font-style: normal;
    color: var(--fg-1);
  }
  .rf {
    color: var(--red-fg);
  }
  .mix {
    margin: 0 0 8px;
    line-height: 1.7;
  }
  .charts {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
    gap: 12px;
  }
  .empty {
    display: grid;
    place-items: center;
    min-height: 140px;
    background: var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line-soft, var(--line));
  }
  .foot {
    margin-top: 10px;
  }
</style>
