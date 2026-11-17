<script lang="ts">
  import BondPick from '../components/BondPick.svelte'
  import { opMatches } from '../lib/opsearch'
  import { app } from '../lib/state.svelte'
  import { asset, ROMAN, fmtHp } from '../lib/assets'
  import OpCard from '../components/OpCard.svelte'
  import TierBadge from '../components/TierBadge.svelte'
  import Icon from '../components/Icon.svelte'
  import { openMenu, equipmentMenu } from '../lib/menu.svelte'
  import Seg from '../components/Seg.svelte'
  import StrategyGrid from '../components/StrategyGrid.svelte'
  import RosterBar from '../components/RosterBar.svelte'
  import { roster } from '../lib/roster.svelte'
  import { fly } from 'svelte/transition'
  import { cubicOut } from 'svelte/easing'


  // 图鉴：干员 / 装备网格，筛选常驻展开；点卡片在底部面板看详情（不靠悬停）。
  // 策略只有名字、发起者和一句效果，做成列表直接看全，本局策略排最前
  type Kind = 'operator' | 'equipment' | 'strategy' | 'boss' | 'bounty'
  let kind = $state<Kind>('operator')
  const kinds: { id: Kind; label: string; count: number }[] = [
    { id: 'operator', label: '干员', count: app.catalog.operators.length },
    { id: 'equipment', label: '装备', count: app.catalog.equipment.length },
    { id: 'strategy', label: '策略', count: app.catalog.strategies.length },
    { id: 'boss', label: '首领', count: app.catalog.bosses.length },
    { id: 'bounty', label: '悬赏', count: (app.catalog.bounties ?? []).length },
  ]
  // 首领：本局首领排最前；血量显示本局模式的，没读到模式时显示规程等级 1
  const bosses = $derived(
    app.catalog.bosses.filter((b) => !q || b.name.includes(q)).sort((a, b) => Number(b.id === app.match.bossId) - Number(a.id === app.match.bossId)),
  )
  const hpAt = (b: (typeof app.catalog.bosses)[number]) => b.hp.find((h) => h.mode === (app.match.mode?.id ?? 'mode_single_1'))
  // 悬赏（任务 201）：悬赏决策里能选的全部项。按名字里的类别分组，组内悬赏在前、战术特训在后，再按奖励从高到低；本回合名单里有的标「本回合」
  type Bounty = (typeof app.catalog.bounties)[number]
  const BTAGS = ['飞行', '频次', '损伤', '持续', '隐匿', '折射', '特异']
  const bGroupOf = (b: Bounty) => (b.rounds === null ? '每场都加' : b.tag ?? '知名敌人')
  const bGroupNames = [...BTAGS, '知名敌人', '每场都加']
  let bKind = $state<Bounty['kind'] | null>(null)
  let bCoin = $state<number | null>(null)
  const roundNames = $derived(new Set(app.match.roundEnemies?.round === app.match.economy?.round ? (app.match.roundEnemies?.enemies ?? []).map((e) => e.name) : []))
  const bounties = $derived(
    (app.catalog.bounties ?? []).filter(
      (b) => (!q || b.enemyName.includes(q) || b.name.includes(q)) && (!bKind || b.kind === bKind) && (bCoin === null || (bCoin >= 4 ? b.coin >= 4 : b.coin === bCoin)),
    ),
  )
  const bGroups = $derived(
    bGroupNames
      .map((g) => ({ g, list: bounties.filter((b) => bGroupOf(b) === g).sort((a, b) => Number(a.kind === 'perfect') - Number(b.kind === 'perfect') || b.coin - a.coin || (a.grade ?? 0) - (b.grade ?? 0) || a.enemyName.localeCompare(b.enemyName, 'zh')) }))
      .filter((x) => x.list.length),
  )
  const bWhen = (b: Bounty) => (b.rounds === null ? '之后每场' : b.rounds === 2 ? '接下来两场' : '下场')
  // 切换时内容从切换方向滑入
  let dir = $state(1)
  let topH = $state(0)
  const pick = (k: Kind) => {
    dir = kinds.findIndex((x) => x.id === k) >= kinds.findIndex((x) => x.id === kind) ? 1 : -1
    kind = k
  }
  let q = $state('')
  let tiers = $state<number[]>([])
  let bonds = $state<string[]>([])
  /** 选了几个盟约时：false = 全部符合（默认），true = 符合其一（v1.2.8 群友要） */
  let bondAny = $state(false)
  const hasBond = (o: { core: string[]; extra: string[] }, b: string) => o.core.includes(b) || o.extra.includes(b)
  let onlyTracked = $state(false)
  // 从顶栏追踪的「N 项」点进来：切到干员、只看追踪中
  $effect(() => {
    if (!app.codexTracked) return
    app.codexTracked = false
    kind = 'operator'
    onlyTracked = true
  })
  let onlyFocus = $state(false)
  let hideBanned = $state(false)
  // 我的干员（任务 163）：只看没认全的
  let onlyIncomplete = $state(false)
  const incompleteNames = $derived(new Set(roster.incomplete.map((o) => o.name)))
  let cat = $state<string | null>(null)

  // 效果时机：干员效果文字里的 <战斗中>、<休整期结束时> 这类标签，从数据里统计；少见的并成「其他时机」
  const OTHER = '其他时机'
  const timingsOf = (o: (typeof app.catalog.operators)[number]) =>
    new Set([...o.normal, ...o.elite].flatMap((t) => [...t.text.matchAll(/<([^<>]*(?:时|战斗中))>/g)].map((m) => m[1])))
  const opTimings = new Map(app.catalog.operators.map((o) => [o.name, timingsOf(o)]))
  const timingCount = new Map<string, number>()
  for (const s of opTimings.values()) for (const t of s) timingCount.set(t, (timingCount.get(t) ?? 0) + 1)
  const mainTimings = [...timingCount].filter(([, n]) => n >= 3).sort((a, b) => b[1] - a[1]).map(([t]) => t)
  const hasTiming = (name: string, t: string) => {
    const s = opTimings.get(name)!
    return t === OTHER ? [...s].some((x) => !mainTimings.includes(x)) : s.has(t)
  }
  const timingChips = [...mainTimings, ...([...timingCount.keys()].some((t) => !mainTimings.includes(t)) ? [OTHER] : [])]
  let timings = $state<string[]>([])

  const toggle = <T,>(arr: T[], v: T) => (arr.includes(v) ? arr.filter((x) => x !== v) : [...arr, v])

  const ops = $derived(
    app.catalog.operators.filter(
      (o) =>
        opMatches(o.name, q) &&
        (!tiers.length || tiers.includes(o.tier)) &&
        (!bonds.length || (bondAny ? bonds.some((b) => hasBond(o, b)) : bonds.every((b) => hasBond(o, b)))) &&
        (!timings.length || timings.some((t) => hasTiming(o.name, t))) &&
        (!onlyTracked || app.tracked.has(o.name)) &&
        (!onlyFocus || app.focusOps.has(o.name)) &&
        (!hideBanned || !app.banned.has(o.name)) &&
        (!onlyIncomplete || incompleteNames.has(o.name)),
    ),
  )
  const cats = [...new Set(app.catalog.equipment.flatMap((e) => e.categories))]
  const eqs = $derived(
    app.catalog.equipment.filter(
      (e) =>
        (!q || e.name.includes(q)) &&
        (!tiers.length || tiers.includes(e.tier)) &&
        (!cat || e.categories.includes(cat)) &&
        (!onlyTracked || app.trackedEq.has(e.name)),
    ),
  )
  // 按等阶分组；自选干员单独一组（t = -1），不可招募的特殊干员（如暴躁铁皮）放最后一组（t = 0）
  const groups = $derived(
    [1, 2, 3, 4, 5, 6, -1, 0]
      .map((t) => ({
        t,
        ops: ops.filter((o) => (t > 0 ? !o.obtain && !o.diy && o.tier === t : t < 0 ? !!o.diy : !!o.obtain)),
        eqs: t > 0 ? eqs.filter((e) => e.tier === t) : [],
      }))
      .filter((g) => (kind === 'operator' ? g.ops.length : g.eqs.length)),
  )
  const cur = $derived(app.detail)
  const moreOpen = $derived(!!app.folded['codex.more'])
  const filtered = $derived(tiers.length + bonds.length + timings.length + (onlyTracked ? 1 : 0) + (onlyFocus ? 1 : 0) + (hideBanned ? 1 : 0) + (onlyIncomplete ? 1 : 0) + (cat ? 1 : 0) + (q ? 1 : 0) + (bKind ? 1 : 0) + (bCoin !== null ? 1 : 0))
</script>

<div class="page">
  <!-- 只让类型切换和搜索吸顶，筛选条件随页面滚走，免得占掉半屏 -->
  <div class="top" bind:clientHeight={topH}>
    <Seg items={kinds} value={kind} onchange={pick} height={26} />
    <div class="search">
      <span class="si"><Icon name="search" size={15} /></span>
      <input bind:value={q} placeholder={kind === 'operator' ? '搜索干员' : kind === 'equipment' ? '搜索装备' : kind === 'boss' ? '搜索首领' : kind === 'bounty' ? '搜索悬赏或敌人' : '搜索策略、发起者或效果'} />
      {#if filtered}
        <button class="clear" onclick={() => { q = ''; tiers = []; bonds = []; timings = []; onlyTracked = false; onlyFocus = false; hideBanned = false; onlyIncomplete = false; cat = null; bKind = null; bCoin = null }}>清除筛选</button>
      {/if}
    </div>
  </div>
  {#key kind}
  <div in:fly={{ x: 24 * dir, duration: 220, easing: cubicOut }}>
  {#if kind === 'strategy'}
    <!-- 仿游戏「策略报告」：上面详情（默认本局策略），下面按泛用型 / 盟约型分组排头像 -->
    <div class="strats" style:--sticky-top="{topH}px"><StrategyGrid {q} current={app.match.strategyId} /></div>
  {:else if kind === 'boss'}
    <p class="bnote">血量为{app.match.mode?.name ?? '规程等级1'}{app.match.mode ? '（本局）' : ''}；点开看各模式血量。地图和敌人路线之后在网站上看。</p>
    <div class="blist">
      {#each bosses as b (b.id)}
        {@const h = hpAt(b)}
        <button class="brow" class:cur={b.id === app.match.bossId} class:selected={cur?.kind === 'boss' && cur.id === b.id} onclick={() => (app.detail = { kind: 'boss', id: b.id })}>
          <span class="bpic"><img src={asset.boss(b.id)} alt="" loading="lazy" /></span>
          <span class="bn">
            <b>{b.name}</b>
            <span class="btags">
              {#if b.id === app.match.bossId}<em class="now">本局</em>{/if}
              {#if b.hidingCore}<em>隐秘核心</em>{/if}
            </span>
          </span>
          {#if h}<span class="bhp"><b>{fmtHp(h.hp)}</b>{#if h.coreHp}<small>核心 {fmtHp(h.coreHp)}</small>{/if}</span>{/if}
        </button>
      {:else}
        <p class="none">没有符合条件的首领。</p>
      {/each}
    </div>
  {:else if kind === 'bounty'}
    <!-- 悬赏：对局中途「悬赏决策」框里选一项，自己接下来的作战多出这只敌人 -->
    <p class="bnote">悬赏：打掉它的人拿资金，漏了或被队友打掉，钱归队友。战术特训：各自行动阶段就完美作战才拿。数据里的全部项，不一定每个赛季都会出。</p>
    <div class="chips bchips">
      <button class="chip" class:on={bKind === 'kill'} onclick={() => (bKind = bKind === 'kill' ? null : 'kill')}>悬赏</button>
      <button class="chip" class:on={bKind === 'perfect'} onclick={() => (bKind = bKind === 'perfect' ? null : 'perfect')}>战术特训</button>
      <span class="sep"></span>
      {#each [1, 2, 3, 4] as c (c)}
        <button class="chip coinchip" class:on={bCoin === c} style:--coin="url({asset.hud('s_icon_gold')})" onclick={() => (bCoin = bCoin === c ? null : c)}><i></i>{c >= 4 ? '4+' : c}</button>
      {/each}
    </div>
    {#each bGroups as grp (grp.g)}
      <div class="bgh">{grp.g}<small>{grp.list.length}</small></div>
      <div class="blist">
        {#each grp.list as b (b.id)}
          {@const en = app.catalog.enemies.find((e) => e.id === b.enemyId)}
          <button class="brow bty" class:cur={roundNames.has(b.enemyName)} class:selected={cur?.kind === 'enemy' && cur.id === b.enemyId} title={b.text}
            onclick={() => en && (app.detail = { kind: 'enemy', id: b.enemyId })}>
            <span class="bpic epic"><img src={asset.enemy(b.enemyId)} alt="" loading="lazy" onerror={(e) => e.currentTarget.classList.add('miss')} /><b>{b.enemyName.slice(0, 1)}</b></span>
            <span class="bn">
              <b>{b.enemyName}{#if b.count > 1}<small> ×{b.count}</small>{/if}</b>
              <span class="btags">
                {#if roundNames.has(b.enemyName)}<em class="now">本回合</em>{/if}
                <em class:train={b.kind === 'perfect'}>{b.kind === 'perfect' ? '战术特训' : '悬赏'}{#if b.tag} · {b.tag}{ROMAN[b.grade ?? 0]}{/if}</em>
                <em>{bWhen(b)}</em>
              </span>
            </span>
            <span class="bcoin" class:zero={!b.coin} style:--coin="url({asset.hud('s_icon_gold')})">
              {#if b.coin}<b><i></i>+{b.coin}</b><small>{b.kind === 'perfect' ? '完美作战得' : '击倒得'}</small>{:else}<b>0</b><small>不给钱</small>{/if}
            </span>
          </button>
        {/each}
      </div>
    {:else}
      <p class="none">没有符合条件的悬赏。</p>
    {/each}
  {:else}
  {#if kind === 'operator'}<RosterBar {onlyIncomplete} ontoggle={() => (onlyIncomplete = !onlyIncomplete)} />{/if}
  <div class="filters">
    <div class="chips">
      {#each [1, 2, 3, 4, 5, 6] as t (t)}
        <button class="chip tier" class:on={tiers.includes(t)} style:--c="var(--tier-{t})" onclick={() => (tiers = toggle(tiers, t))}>{ROMAN[t]}</button>
      {/each}
      <span class="sep"></span>
      <button class="chip" class:on={onlyTracked} onclick={() => (onlyTracked = !onlyTracked)}>追踪中</button>
      {#if kind === 'operator'}
        <button class="chip" class:on={onlyFocus} onclick={() => (onlyFocus = !onlyFocus)}>关注</button>
        <button class="chip" class:on={hideBanned} onclick={() => (hideBanned = !hideBanned)}>隐藏禁用</button>
        <!-- 时机和盟约收进「更多筛选」，默认收起、记住开合，免得筛选区占半屏（v1.2.7 群友反馈太长） -->
        <button class="chip more" class:open={moreOpen} aria-expanded={moreOpen} onclick={() => app.toggleFold('codex.more')}>
          更多筛选{#if timings.length + bonds.length}<em>{timings.length + bonds.length}</em>{/if}<span class="chev"><Icon name="arrowDown" size={11} /></span>
        </button>
      {/if}
    </div>
    {#if kind === 'operator'}
      {#if moreOpen}
        <!-- 按效果时机：选几个就列出带其中任一个的干员 -->
        <div class="chips timing">
          <span class="clbl">时机</span>
          {#each timingChips as t (t)}
            <button class="chip" class:on={timings.includes(t)} onclick={() => (timings = toggle(timings, t))}>{t === OTHER ? t : `<${t}>`}</button>
          {/each}
        </div>
        <BondPick state={(id) => ({ on: bonds.includes(id) })} onpick={(id) => (bonds = toggle(bonds, id))}>
          {#snippet coreAside()}
            <span class="anyall" role="group" aria-label="多个盟约怎么筛">
              <button class:on={!bondAny} onclick={() => (bondAny = false)}>全部符合</button>
              <button class:on={bondAny} onclick={() => (bondAny = true)}>符合其一</button>
            </span>
          {/snippet}
        </BondPick>
      {:else if timings.length + bonds.length}
        <!-- 收起时也看得到筛了什么，点 × 单个去掉 -->
        <div class="chips picked">
          {#each timings as t (t)}<button class="chip on" title="去掉这个时机" onclick={() => (timings = toggle(timings, t))}>{t === OTHER ? t : `<${t}>`}<Icon name="close" size={10} /></button>{/each}
          {#each bonds as b (b)}<button class="chip on" title="去掉这个盟约" onclick={() => (bonds = toggle(bonds, b))}>{app.cov(b).name}<Icon name="close" size={10} /></button>{/each}
        </div>
      {/if}
    {:else}
      <div class="chips">
        {#each cats as c (c)}
          <button class="chip" class:on={cat === c} onclick={() => (cat = cat === c ? null : c)}>{c}</button>
        {/each}
      </div>
    {/if}
  </div>

  {#each groups as g (g.t)}
    <div class="ghead" style:--c={g.t > 0 ? `var(--tier-${g.t})` : g.t < 0 ? 'var(--mint)' : 'var(--gold)'}>
      {#if g.t > 0}
        <TierBadge tier={g.t} size={18} />
        <span class="term">Tier {ROMAN[g.t]}</span>
      {:else}
        <span class="special">{g.t < 0 ? '自选 · 开局选好后会在商店刷出' : '特殊 · 不可招募'}</span>
      {/if}
      <i></i>
      <em>{kind === 'operator' ? g.ops.length : g.eqs.length}</em>
    </div>
    <div class="grid">
      {#if kind === 'operator'}
        {#each g.ops as o (o.name)}
          <OpCard name={o.name} selected={cur?.kind === 'operator' && cur.name === o.name} onclick={() => (app.detail = { kind: 'operator', name: o.name })} />
        {/each}
      {:else}
        {#each g.eqs as e (e.name)}
          <button
            class="eq"
            class:selected={cur?.kind === 'equipment' && cur.name === e.name}
            class:tracked={app.trackedEq.has(e.name)}
            style:--c="var(--tier-{e.tier})"
            style:--bg="url({asset.card('bg_black')})"
            onclick={() => (app.detail = { kind: 'equipment', name: e.name })}
            oncontextmenu={(ev) => openMenu(ev, equipmentMenu(e.name))}
          >
            <span class="et"><TierBadge tier={e.tier} size={18} /></span>
            {#if app.trackedEq.has(e.name)}<span class="trk">追踪</span>{/if}
            <img src={asset.equipment(e.name)} alt="" loading="lazy" />
            <span class="en">{e.name}</span>
            <span class="ec">{e.categories.join(' · ')}</span>
          </button>
        {/each}
      {/if}
    </div>
  {:else}
    <p class="none">没有符合条件的{kind === 'operator' ? '干员' : '装备'}。</p>
  {/each}
  {/if}
  </div>
  {/key}
</div>

<style>
  .bnote {
    margin: 10px 2px 6px;
    font-size: var(--fs-sm);
    color: var(--fg-4);
  }
  .blist {
    display: flex;
    flex-direction: column;
    gap: 2px;
  }
  .brow {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 6px 10px 6px 6px;
    text-align: left;
    background: var(--bg-1);
  }
  .brow:hover,
  .brow.selected {
    background: var(--bg-2);
  }
  .brow.cur {
    box-shadow: inset var(--edge) 0 0 var(--gold);
  }
  .bpic {
    flex: none;
    width: 40px;
    height: 40px;
    background: rgb(var(--shade-rgb) / 0.35);
  }
  .bpic img {
    width: 100%;
    height: 100%;
    object-fit: cover;
  }
  .bn {
    flex: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 2px;
  }
  .bn b {
    font-size: var(--fs-md);
    color: var(--fg-1);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .btags {
    display: flex;
    gap: 4px;
  }
  .btags em {
    font-style: normal;
    font-size: var(--fs-xs);
    padding: 0 5px;
    color: var(--fg-3);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .btags em.now {
    color: var(--gold-ink);
    background: var(--gold);
    box-shadow: none;
  }
  .bhp {
    flex: none;
    display: flex;
    flex-direction: column;
    align-items: flex-end;
    font-variant-numeric: tabular-nums;
  }
  .bhp b {
    font-size: var(--fs-md);
    color: var(--fg-1);
  }
  .bhp small {
    font-size: var(--fs-xs);
    color: var(--gold);
  }
  .strats {
    margin-top: 10px;
  }
  /* 悬赏 */
  .bchips {
    margin-bottom: 4px;
  }
  .coinchip {
    display: inline-flex;
    align-items: center;
    gap: 2px;
  }
  .coinchip i,
  .bcoin i {
    width: 13px;
    height: 13px;
    background: var(--coin) center / contain no-repeat;
  }
  .bgh {
    display: flex;
    align-items: baseline;
    gap: 6px;
    margin: 12px 2px 4px;
    font-size: var(--fs-sm);
    font-weight: var(--fw-bold);
    color: var(--fg-2);
  }
  .bgh small {
    font-weight: var(--fw-base);
    color: var(--fg-4);
  }
  .epic {
    position: relative;
    display: grid;
    place-items: center;
  }
  .epic img {
    position: relative;
    z-index: 1;
    object-fit: contain;
  }
  .epic img:global(.miss) {
    display: none;
  }
  .epic b {
    position: absolute;
    font-size: var(--fs-md);
    color: var(--fg-4);
  }
  .bn b small {
    font-size: var(--fs-sm);
    color: var(--fg-3);
    font-weight: var(--fw-base);
  }
  .btags em.train {
    color: var(--mint);
    box-shadow: inset 0 0 0 1px rgb(var(--mint-rgb) / 0.5);
  }
  .bcoin {
    flex: none;
    display: flex;
    flex-direction: column;
    align-items: flex-end;
    font-variant-numeric: tabular-nums;
  }
  .bcoin b {
    display: flex;
    align-items: center;
    gap: 3px;
    font-size: var(--fs-lg);
    color: var(--gold);
  }
  .bcoin small {
    font-size: var(--fs-2xs);
    color: var(--fg-4);
  }
  .bcoin.zero b {
    color: var(--fg-4);
  }
  .special {
    font-size: var(--fs-base);
    font-weight: var(--fw-bold);
    color: var(--gold);
  }
  .page {
    padding: 0 10px 24px;
  }
  .filters {
    display: flex;
    flex-direction: column;
    gap: 7px;
    padding-top: 2px;
  }
  .top {
    position: sticky;
    top: 0;
    z-index: 2;
    display: flex;
    flex-direction: column;
    gap: 7px;
    margin: 0 -10px 4px;
    padding: 10px 10px 9px;
    background: var(--bg-0);
    border-bottom: 1px solid var(--line-soft);
  }
  .search {
    display: flex;
    align-items: center;
    gap: 6px;
    height: 30px;
    padding: 0 8px;
    background: var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .search:focus-within {
    box-shadow: inset 0 0 0 1px var(--mint-lo);
  }
  .si {
    display: grid;
    color: var(--fg-3);
  }
  .search input {
    flex: 1;
    min-width: 0;
    background: none;
    border: 0;
    outline: 0;
    font-size: var(--fs-md);
  }
  .search input::placeholder {
    color: var(--fg-4);
  }
  .clear {
    font-size: var(--fs-sm);
    color: var(--mint);
    white-space: nowrap;
  }
  .chips {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
    align-items: center;
  }
  .chip {
    font-size: var(--fs-base);
    padding: 2px 8px;
    color: var(--fg-2);
    background: var(--bg-2);
    box-shadow: inset 0 0 0 1px var(--line-soft);
  }
  .chip:hover {
    color: var(--fg-1);
  }
  .chip.on {
    color: var(--mint-ink);
    background: var(--mint);
    box-shadow: none;
  }
  .chip.tier {
    font-family: var(--font-term);
    font-weight: var(--fw-bold);
    min-width: 30px;
    color: var(--c);
  }
  .chip.tier.on {
    color: var(--bg-0);
    background: var(--c);
  }
  .sep {
    width: 1px;
    height: 14px;
    background: var(--line);
    margin: 0 3px;
  }
  .clbl {
    font-size: var(--fs-sm);
    color: var(--fg-4);
    margin-right: 2px;
  }
  /* 多个盟约「全部符合 / 符合其一」，放在盟约组标题右边 */
  .anyall {
    display: inline-flex;
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .anyall button {
    padding: 2px 7px;
    font-size: var(--fs-xs);
    color: var(--fg-4);
  }
  .anyall button.on {
    color: var(--mint-ink);
    background: var(--mint);
  }
  .timing .chip {
    font-size: var(--fs-sm);
  }
  .chip.more {
    display: inline-flex;
    align-items: center;
    gap: 3px;
    margin-left: auto;
  }
  .chip.more em {
    font-style: normal;
    font-size: var(--fs-xs);
    padding: 0 4px;
    color: var(--mint-ink);
    background: var(--mint);
  }
  .chip.more .chev {
    display: inline-flex;
    transition: transform var(--t-fast);
  }
  .chip.more.open .chev {
    transform: rotate(180deg);
  }
  .picked .chip {
    display: inline-flex;
    align-items: center;
    gap: 3px;
  }
  .ghead {
    display: flex;
    align-items: center;
    gap: 6px;
    margin: 12px 0 6px;
  }
  .ghead span {
    font-size: var(--fs-xs);
    color: var(--c);
  }
  .ghead i {
    flex: 1;
    height: 1px;
    background: linear-gradient(90deg, var(--c), transparent);
    opacity: 0.4;
  }
  .ghead em {
    font-style: normal;
    font-family: var(--font-term);
    font-size: var(--fs-sm);
    color: var(--fg-4);
  }
  .grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(78px, 1fr));
    gap: 6px;
  }
  .eq {
    position: relative;
    display: flex;
    flex-direction: column;
    align-items: center;
    padding: 8px 4px 6px;
    background: var(--bg) center / cover, var(--bg-2);
    box-shadow:
      inset 0 0 0 1px var(--line),
      inset 0 2px 0 var(--c);
  }
  .eq:hover {
    box-shadow:
      inset 0 0 0 1px var(--line-hi),
      inset 0 2px 0 var(--c);
  }
  .eq.selected {
    box-shadow:
      inset 0 0 0 1.5px var(--fg-1),
      inset 0 2px 0 var(--c);
  }
  /* 追踪中：和干员卡一样的右上角薄荷绿标签 */
  .eq.tracked {
    box-shadow:
      inset 0 0 0 1px rgb(var(--mint-rgb) / 0.55),
      inset 0 2px 0 var(--c);
  }
  .trk {
    position: absolute;
    right: 0;
    top: 2px;
    font-size: var(--fs-xs);
    font-weight: var(--fw-bold);
    line-height: 1;
    padding: 3px 5px;
    color: var(--mint-ink);
    background: var(--mint);
  }
  .et {
    position: absolute;
    left: 3px;
    top: 5px;
  }
  .eq img {
    width: 56px;
    height: 56px;
    object-fit: contain;
    margin: 6px 0 4px;
  }
  .en {
    font-size: var(--fs-base);
    font-weight: var(--fw-semi);
    text-align: center;
    line-height: 1.25;
  }
  .ec {
    font-size: var(--fs-xs);
    color: var(--fg-4);
  }
  .none {
    color: var(--fg-3);
    text-align: center;
    padding: 30px 0;
  }
  /* 宽（工作台）：盟约筛选不再按 13 列撑满（圆按钮会变得很大），固定 38 像素一个，尽量排成一行 */
  @container (min-width: 720px) {
  }
</style>
