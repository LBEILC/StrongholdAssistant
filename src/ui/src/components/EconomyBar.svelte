<script lang="ts">
  import { fly } from 'svelte/transition'
  import { app } from '../lib/state.svelte'
  import { asset, ROMAN } from '../lib/assets'
  import Icon from './Icon.svelte'

  // 回合与资金（v0.7.0，设计见 docs/design.md「回合与资金」）：「第 6 回合 · 资金 4 · 下回合约 12 · 升级 5」。
  // 本局页顶部一行；向上收起时放在追踪列表上方（compact：只留这一行和「+ 收入」）。
  // 不在休整期（读不到资金）时变灰，保留最后读到的数
  let { compact = false }: { compact?: boolean } = $props()

  const e = $derived(app.match.economy)
  const STALE_MS = 20_000
  let now = $state(Date.now())
  $effect(() => {
    const t = setInterval(() => (now = Date.now()), 5000)
    return () => clearInterval(t)
  })
  const stale = $derived(!!e && now - new Date(e.readAt).getTime() > STALE_MS)
  // 得闲饮茶：第 1、2 回合不发钱，第 3 回合一起发；第 2 回合的「下回合」就是三回合合计（宿主给出）
  const saving = $derived(e?.rule === 'deferred' && e.round < 2)
  const RULE: Record<string, string> = {
    carry: '利滚利：剩的钱加到下回合，剩 5 以上再 +1',
    kazimier: '每买 1 名卡西米尔干员，下回合 +1，最多 +3',
    random: '策略收入另算',
    deferred: '得闲饮茶：前三回合的钱第 3 回合一起发',
  }
  const notes = $derived(
    e ? [e.extra > 0 && `含本局额外 +${e.extra}`, e.rule && RULE[e.rule], e.basis === 'shared' && '基础资金来自大家的资金表'].filter(Boolean) : [],
  )

  // 「+ 收入」：只列会加以后回合资金、但识别不了的（Catalog.incomeSources）
  let picking = $state(false)
  const sources = $derived(app.catalog.incomeSources ?? [])
  const when = (k: 'each' | 'once', n: number) => (k === 'each' ? `之后每回合 +${n}` : `下回合 +${n}`)
  async function add(name: string, elite: boolean) {
    await app.addIncome(name, elite)
    picking = false
  }
  const openShare = () => (app.settingsOpen = true)
</script>

<svelte:window onkeydown={(ev) => ev.key === 'Escape' && (picking = false)} />

{#if e}
  <div class="econ" class:stale class:compact style:--coin="url({asset.hud('s_icon_gold')})">
    <div class="line">
      <span class="rd">回合<b class="tag">{e.round}</b></span>
      <span class="sep"></span>
      <span class="mn" title="当前资金"><i class="coin"></i><b>{e.money}</b></span>
      <span class="sep"></span>
      {#if saving}
        <span class="nx muted">攒到第 3 回合一起发</span>
      {:else if e.next !== null}
        <span class="nx" title="下回合预计资金">下回合约 <b>{e.next}</b></span>
      {:else}
        <span class="nx muted">下回合 —</span>
      {/if}
      {#if e.upgradeCost !== null}
        <span class="sep"></span>
        <span class="up" title={e.level ? `调度中心 ${ROMAN[e.level]} 级，升级要花的钱` : '升级调度中心要花的钱'}>
          {#if e.level}<img class="lv" src={asset.skin(`level_${e.level}`)} alt="{ROMAN[e.level]} 级" />{/if}升级 <b>{e.upgradeCost}</b>
        </span>
      {/if}
      <span class="spacer"></span>
      {#if stale}<span class="old" title="不在休整期，读不到资金；显示最后读到的数">未更新</span>{/if}
      {#if sources.length}
        <button class="add" class:on={picking} onclick={() => (picking = !picking)} title="有些装备和干员会加以后回合的钱，助手识别不了，点上就算进预计">
          <Icon name="add" size={12} />收入
        </button>
      {/if}
    </div>

    {#if !compact}
      {#if e.next === null && e.basis === 'none' && !saving}
        <p class="sub">
          还没有这个模式的资金数据，打一局就有了{#if !app.settings.shareIncome}<button class="link" onclick={openShare}>帮大家补全资金表</button>{/if}
        </p>
      {:else if notes.length}
        <p class="sub">{notes.join(' · ')}</p>
      {/if}
      {#if e.incomes.length}
        <div class="chips">
          {#each e.incomes as i (i.id)}
            <span class="chip">
              {i.source}{#if i.elite}<em>{app.catalog.incomeSources?.find((s) => s.name === i.source)?.trigger === 'sell' ? '精锐' : '进阶'}</em>{/if}
              <span class="amt">{when(i.kind, i.amount)}</span>
              <button class="cx" aria-label="去掉 {i.source}" onclick={() => app.removeIncome(i.id)}><Icon name="close" size={11} /></button>
            </span>
          {/each}
        </div>
      {/if}
    {/if}

    {#if picking}
      <div class="pick" transition:fly={{ y: -4, duration: 140 }}>
        <p class="ph">点上的收入从下回合起算；立即给钱的不用点，当前资金读得到</p>
        {#each sources as s (s.name)}
          <div class="src">
            {#if s.trigger === 'sell'}
              <img class="av" src={asset.operator(s.name)} alt="" />
              <span class="sn">售出{s.name}</span>
            {:else}
              <img class="eq" src={asset.equipment(s.name)} alt="" />
              <span class="sn">{s.name}</span>
            {/if}
            <button class="opt" onclick={() => add(s.name, false)}>普通<b>{when(s.kind, s.amount)}</b></button>
            <button class="opt" onclick={() => add(s.name, true)}>{s.trigger === 'sell' ? '精锐' : '进阶'}<b>{when(s.kind, s.eliteAmount)}</b></button>
          </div>
        {/each}
      </div>
    {/if}
  </div>
{/if}

<style>
  .econ {
    position: relative;
    margin: 0 0 8px;
    padding: 6px 8px;
    background: rgb(var(--shade-rgb) / 0.28);
    box-shadow: inset 2px 0 0 var(--gold);
  }
  .econ.compact {
    margin: 0;
    padding: 4px 10px;
    box-shadow: inset 0 -1px 0 var(--line-soft);
  }
  .line {
    display: flex;
    align-items: center;
    gap: 7px;
    font-size: var(--fs-base);
    color: var(--fg-3);
    white-space: nowrap;
    font-variant-numeric: tabular-nums;
  }
  /* 数字照游戏 HUD：粗体无衬线（用界面的 MiSans，和游戏的数字字形接近） */
  .line b {
    margin: 0 1px;
    font-size: var(--fs-lg);
    font-weight: 700;
    color: var(--fg-1);
  }
  /* 回合数：游戏顶栏「回合」后面的数字底块（颜色随皮肤） */
  .rd b.tag {
    margin: 0 0 0 4px;
    min-width: 26px;
    padding: 0 6px;
    line-height: 18px;
    text-align: center;
    color: var(--round-fg);
    background: var(--round-bg);
    border-radius: var(--r-1);
  }
  .stale .rd b.tag {
    color: var(--round-fg);
    opacity: 0.6;
  }
  .mn {
    display: inline-flex;
    align-items: center;
    gap: 3px;
  }
  .mn b {
    color: var(--gold);
  }
  .up {
    display: inline-flex;
    align-items: center;
    gap: 3px;
  }
  /* 调度中心等级牌（皮肤位图 63×58） */
  .lv {
    height: 20px;
    margin: -4px 2px -4px 0;
  }
  .stale .lv {
    filter: grayscale(1) brightness(0.7);
  }
  /* 游戏里的资金图标（三颗菱形，官方素材是白色，游戏里染成金色） */
  .coin {
    width: 16px;
    height: 13px;
    background: var(--gold);
    mask: var(--coin) center / contain no-repeat;
  }
  .nx b {
    color: var(--mint);
  }
  .muted {
    color: var(--fg-4);
  }
  .sep {
    width: 1px;
    height: 10px;
    background: var(--line);
  }
  .spacer {
    flex: 1;
  }
  /* 侧栏窄时这一行放不下：只让「未更新」缩成省略号（变灰已经说明了），其他项不缩 */
  .line > * {
    flex-shrink: 0;
  }
  .line > .old {
    flex-shrink: 1;
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    font-size: var(--fs-sm);
    color: var(--fg-4);
  }
  .stale .line b,
  .stale .mn b,
  .stale .nx b {
    color: var(--fg-3);
  }
  .stale .coin {
    background: var(--fg-4);
  }
  .add {
    display: inline-flex;
    align-items: center;
    gap: 2px;
    height: 22px;
    padding: 0 7px 0 4px;
    font-size: var(--fs-base);
    color: var(--fg-2);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .add:hover,
  .add.on {
    color: var(--gold);
    box-shadow: inset 0 0 0 1px rgb(var(--gold-rgb) / 0.6);
  }
  .sub {
    margin: 4px 0 0;
    font-size: var(--fs-sm);
    color: var(--fg-4);
  }
  .link {
    margin-left: 8px;
    font-size: var(--fs-sm);
    color: var(--mint);
  }
  .chips {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
    margin-top: 5px;
  }
  .chip {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    height: 22px;
    padding: 0 2px 0 7px;
    font-size: var(--fs-sm);
    color: var(--fg-2);
    background: var(--bg-3);
  }
  .chip em {
    font-style: normal;
    font-size: var(--fs-xs);
    padding: 0 3px;
    color: var(--gold-ink);
    background: var(--gold);
  }
  .amt {
    color: var(--gold);
  }
  .cx {
    width: 18px;
    height: 18px;
    display: grid;
    place-items: center;
    color: var(--fg-4);
  }
  .cx:hover {
    color: var(--fg-1);
  }
  .pick {
    position: absolute;
    z-index: 5;
    top: calc(100% + 2px);
    left: 0;
    right: 0;
    padding: 8px;
    background: var(--bg-2);
    box-shadow:
      0 0 0 1px var(--line),
      0 10px 24px rgb(var(--shade-rgb) / 0.55);
  }
  /* 向上收起时窗口高度跟着内容走，浮层会被裁掉：直接占位，把窗口撑高 */
  .compact .pick {
    position: static;
    margin: 4px -10px 0;
    box-shadow: inset 0 1px 0 var(--line);
  }
  .ph {
    margin: 0 0 6px;
    font-size: var(--fs-sm);
    color: var(--fg-4);
  }
  .src {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 4px 0;
    border-top: 1px solid var(--line-soft);
  }
  .src img {
    width: 28px;
    height: 28px;
    flex: none;
  }
  .src .eq {
    object-fit: contain;
  }
  .src .av {
    object-fit: cover;
    background: var(--bg-3);
  }
  .sn {
    flex: 1;
    min-width: 0;
    font-size: var(--fs-base);
    color: var(--fg-1);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .opt {
    display: flex;
    flex-direction: column;
    align-items: center;
    flex: none;
    width: 92px;
    padding: 2px 0;
    font-size: var(--fs-sm);
    color: var(--fg-3);
    background: var(--bg-3);
  }
  .opt b {
    font-size: var(--fs-sm);
    font-weight: 600;
    color: var(--gold);
  }
  .opt:hover {
    background: var(--bg-4);
  }
</style>
