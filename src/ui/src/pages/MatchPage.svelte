<script lang="ts">
  import { slide } from 'svelte/transition'
  import { cubicOut } from 'svelte/easing'
  import { app } from '../lib/state.svelte'
  import { fmtHp, asset, ROMAN } from '../lib/assets'
  import SectionHeader from '../components/SectionHeader.svelte'
  import BondIcon from '../components/BondIcon.svelte'
  import OpTile from '../components/OpTile.svelte'
  import LineupCard from '../components/LineupCard.svelte'
  import Icon from '../components/Icon.svelte'
  import EconomyBar from '../components/EconomyBar.svelte'
  import StrategyGrid from '../components/StrategyGrid.svelte'
  import { openMenu, operatorMenu } from '../lib/menu.svelte'

  // 本局：开局识别到的信息，排版仿游戏「确认本局信息」页；可手动修正；后面接本局能用的已存阵容
  const m = $derived(app.match)
  const core = $derived(m.covenants.filter((c) => app.cov(c.id).kind === 'core'))
  const extra = $derived(m.covenants.filter((c) => app.cov(c.id).kind === 'extra'))
  const boss = $derived(app.catalog.bosses.find((b) => b.id === m.bossId))
  // 本局模式读到了才显示血量（开局页左上角的「规程等级16」「险境模拟」）
  const bossHp = $derived(boss && m.mode ? boss.hp.find((h) => h.mode === m.mode!.id) : undefined)
  const strategy = $derived(app.catalog.strategies.find((s) => s.id === m.strategyId))
  // 策略识别不了（v0.7.0），用户自己选：资金预测和学习要看策略
  let pickingSt = $state(false)
  let stq = $state('')
  const pickSt = (id: string) => {
    app.setMatchStrategy(id === m.strategyId ? null : id)
    pickingSt = false
    stq = ''
  }
  // 我的阵容先按目标盟约里阵容不完整的个数、再按名单被禁人数排序，取前 3
  const usable = $derived(
    app.lineups
      .map((l) => ({ l, bad: app.lineupPenalty(l) }))
      .sort((a, b) => a.bad - b.bad)
      .slice(0, 3),
  )

  // 特别关注：本局阵容不完整的关注盟约、被禁的关注干员
  const focusCovsHit = $derived(app.settings.focusCovenants.filter((id) => app.matchCovenant(id)?.incomplete))
  const focusOpsHit = $derived(app.settings.focusOperators.filter((n) => app.banned.has(n)))
  const focusTotal = $derived(app.settings.focusCovenants.length + app.settings.focusOperators.length)
  const edit = $derived(app.matchEdit)
  // 清空本局要点两次：第一次变成「确认清空」，3 秒内不点就恢复
  let armReset = $state(false)
  let armTimer = 0
  const reset = () => {
    if (!armReset) {
      armReset = true
      armTimer = window.setTimeout(() => (armReset = false), 3000)
      return
    }
    clearTimeout(armTimer)
    armReset = false
    app.resetMatch()
  }
  const tapCov = (id: string) => (edit ? app.toggleCovenantIncomplete(id) : (app.detail = { kind: 'covenant', id }))

  const ago = $derived.by(() => {
    if (!m.recognizedAt) return ''
    const min = Math.round((Date.now() - new Date(m.recognizedAt).getTime()) / 60000)
    return min < 1 ? '刚刚' : `${min} 分钟前`
  })
  const incompleteCount = (list: typeof core) => list.filter((c) => c.incomplete).length

  // 分组可折叠，状态记在本机；修正时盟约和禁用干员总是展开
  const open = (key: string, forceOpen = false) => forceOpen || !app.folded[`match.${key}`]
  const fold = (key: string) => () => app.toggleFold(`match.${key}`)
  const sl = { duration: 200, easing: cubicOut }

  // 禁用干员仿游戏按盟约分组：先不完整的核心盟约，再不完整的附加盟约。和游戏一样，
  // 干员属于多个不完整盟约时在每组都出现，组内按星级从高到低，方便和游戏画面逐个核对。
  // 不属于任何不完整盟约的（手动禁用等）归「其他」，放最后。每个盟约组末尾有「添加」格，点开只列这个盟约的干员；
  // 没有任何阵容不完整的盟约时，只剩一个「手动添加」组，点开列全部。
  const rank = new Map(app.catalog.operators.map((o, i) => [o.name, i]))
  const byTier = (a: string, b: string) => app.op(b).tier - app.op(a).tier || rank.get(a)! - rank.get(b)!
  const bannedGroups = $derived.by(() => {
    const banned = m.bannedOperators
    const groups: { id: string | null; names: string[] }[] = [
      ...core.filter((c) => c.incomplete).map((c) => ({ id: c.id, names: banned.filter((n) => app.op(n).core.includes(c.id)) })),
      ...extra.filter((c) => c.incomplete).map((c) => ({ id: c.id, names: banned.filter((n) => app.op(n).extra.includes(c.id)) })),
    ]
    const shown = new Set(groups.flatMap((g) => g.names))
    const rest = banned.filter((n) => !shown.has(n))
    if (rest.length || !groups.length) groups.push({ id: null, names: rest })
    return groups.map((g) => {
      // 识别过的组按游戏里的格子排：认出的、推测的、没认出的问号格各在原位；手动加的、推算出的其余禁用接在后面
      const bg = g.id ? m.banGroups.find((b) => b.covenant === g.id) : undefined
      const inSlots = new Set(bg?.slots.map((s) => s.name) ?? [])
      const cells: Cell[] = [
        ...(bg?.slots.map((s, i): Cell => (s.name ? { kind: 'op', name: s.name, inferred: !!s.inferred, i } : { kind: 'unknown', i, tier: s.tier, n: s.candidates.length })) ?? []),
        ...g.names.filter((n) => !inSlots.has(n)).sort(byTier).map((name): Cell => ({ kind: 'op', name, inferred: false, i: -1 })),
      ]
      const pos = (pred: (s: NonNullable<typeof bg>['slots'][number]) => boolean) => bg?.slots.flatMap((s, i) => (pred(s) ? [i + 1] : [])) ?? []
      return { id: g.id, count: g.names.length, cells, missing: pos((s) => !s.name), guessed: pos((s) => !!s.inferred), more: !!bg && !bg.complete }
    })
  })
  type Cell = { kind: 'op'; name: string; inferred: boolean; i: number } | { kind: 'unknown'; i: number; tier: number; n: number }
  const nth = (xs: number[]) => `第 ${xs.join('、')} 格`
  // 只做过入场那一次自动识别：禁用名单主要靠规则推算
  const ruleOnly = $derived(m.source !== 'none' && !m.bansScanned && !app.continuous)
</script>

<div class="page">
  <div class="status">
    <span class="st" class:ok={m.source !== 'none'}>
      <span class="term">Info</span>
      {m.source === 'recognized' ? '已识别' : m.source === 'manual' ? '已修正' : m.awaitingDetail ? '等对局详情' : '未识别'}
      {#if m.solo}<b class="solo">单人</b>{/if}
      <em>{ago}</em>
    </span>
    <span class="tools">
      <button class="tool" class:on={edit} onclick={() => (app.matchEdit = !edit)}>
        <Icon name="pencil" size={13} />{edit ? '完成' : '修正'}
      </button>
      <button class="tool" class:busy={app.scanning && !app.continuous} onclick={() => app.rescan()} disabled={app.scanning || app.continuous}>
        <span class="spin"><Icon name="refresh" size={13} /></span>
        {app.scanning && !app.continuous ? '识别中…' : '重新识别'}
      </button>
      <button
        class="tool"
        class:live={app.continuous}
        onclick={() => app.toggleContinuous()}
        disabled={app.scanning && !app.continuous}
        title="点一次后在游戏里往下滚动禁用列表，会自动逐屏识别"
      >
        {#if app.continuous}<span class="live-dot"></span>停止{:else}<Icon name="scan" size={13} />持续识别{/if}
      </button>
      {#if m.source !== 'none'}
        <button
          class="tool"
          class:arm={armReset}
          onclick={reset}
          disabled={app.scanning || app.continuous}
          title="清掉本局的盟约、禁用干员、首领等信息；打完一局编辑阵容时用"
        >
          <Icon name={armReset ? 'trash' : 'close'} size={13} />{armReset ? '确认清空' : '清空'}
        </button>
      {/if}
    </span>
  </div>
  <EconomyBar />
  {#if m.awaitingDetail && !app.continuous}
    <div class="solo-guide">
      <div class="sg-h">单人模式 · 开局页不识别</div>
      <p>单人开局要在两组核心盟约里选一组，选完才算数，所以到对局里再读。</p>
      <ol>
        <li>选好盟约，点「准备就绪」进入对局</li>
        <li>点顶部回合数左边的 <span class="key"><Icon name="search" size={11} /></span> 打开对局详情</li>
        <li>会自动开始识别，慢慢往下滚动禁用列表</li>
      </ol>
      <p class="sg-f">每局只自动识别这一次，之后再打开详情不会自动开始，要补读点「持续识别」。</p>
    </div>
  {/if}
  {#if app.continuous}
    <div class="note">
      <span>持续识别中，已识别 <b>{m.bannedOperators.length}</b> 名禁用干员。在游戏里慢慢往下滚动禁用列表，看全后点「停止」。</span>
    </div>
  {:else if app.scanNote?.message}
    <div class="note" class:bad={!app.scanNote.ok}>
      <span>{app.scanNote.message}</span>
      <button class="nx" aria-label="关闭提示" onclick={() => (app.scanNote = null)}><Icon name="close" size={13} /></button>
    </div>
  {/if}
  {#if edit}
    <div class="edithint">点盟约切换「可用 / 阵容不完整」，禁用干员会跟着重新推算；点禁用干员可解除，漏掉的点各组末尾的「添加」补上。不进修正，直接右键盟约或干员也能改。</div>
  {/if}

  {#if focusTotal && m.source !== 'none'}
    {#if focusCovsHit.length || focusOpsHit.length}
      <div class="focus bad">
        <span class="fh"><Icon name="heartFill" size={13} />关注的被影响了</span>
        <div class="fl">
          {#each focusCovsHit as id (id)}
            <button class="fc" onclick={() => (app.detail = { kind: 'covenant', id })}>
              <BondIcon id={id} size={22} incomplete />{app.cov(id).name}<em>阵容不完整</em>
            </button>
          {/each}
          {#each focusOpsHit as n (n)}
            <OpTile name={n} size={32} showName={false} onclick={() => (app.detail = { kind: 'operator', name: n })} />
          {/each}
        </div>
      </div>
    {:else}
      <div class="focus ok"><Icon name="heartFill" size={13} />关注的 {app.settings.focusCovenants.length} 个盟约、{app.settings.focusOperators.length} 名干员本局都可用</div>
    {/if}
  {/if}

  {#if pickingSt}
    <div class="stpick">
      <div class="sph">
        <b>选本局策略</b>
        <input bind:value={stq} placeholder="搜名字、发起者、效果" />
        <button class="tool" onclick={() => ((pickingSt = false), (stq = ''))}>取消</button>
      </div>
      <StrategyGrid selected={m.strategyId} q={stq} onpick={pickSt} />
    </div>
  {:else if strategy}
    <div class="strategy">
      <img src={asset.strategy(strategy.id)} alt="" />
      <div>
        <div class="sn"><b>{strategy.name}</b><span>{strategy.initiator}</span><button class="chg" onclick={() => (pickingSt = true)}>换</button></div>
        <p>{strategy.description}</p>
      </div>
    </div>
  {:else if m.source !== 'none'}
    <button class="stnone" onclick={() => (pickingSt = true)}>
      <Icon name="add" size={14} />选本局策略<em>识别读不到策略；选上后资金预测会按策略算</em>
    </button>
  {/if}

  <section>
    <SectionHeader
      title="核心盟约"
      tag={incompleteCount(core) ? `${incompleteCount(core)} 个阵容不完整` : undefined}
      ontoggle={edit ? undefined : fold('core')}
      folded={!open('core', edit)}
    >
      {#snippet icon()}
        <img src={asset.stage('img_title_icon_temp_bond')} alt="" class="ti" />
      {/snippet}
    </SectionHeader>
    {#if open('core', edit)}
      <div class="grid" class:editing={edit} transition:slide={sl}>
        {#each core as c (c.id)}
          <BondIcon id={c.id} size={42} on={!c.incomplete} incomplete={c.incomplete} name onclick={() => tapCov(c.id)} />
        {/each}
      </div>
    {/if}
  </section>

  <section>
    <SectionHeader
      title="附加盟约"
      tag={incompleteCount(extra) ? `${incompleteCount(extra)} 个阵容不完整` : undefined}
      ontoggle={edit ? undefined : fold('extra')}
      folded={!open('extra', edit)}
    >
      {#snippet icon()}
        <img src={asset.stage('img_title_icon_perm_bond')} alt="" class="ti" />
      {/snippet}
    </SectionHeader>
    {#if open('extra', edit)}
      <div class="grid" class:editing={edit} transition:slide={sl}>
        {#each extra as c (c.id)}
          <BondIcon id={c.id} size={42} on={!c.incomplete} incomplete={c.incomplete} name onclick={() => tapCov(c.id)} />
        {/each}
      </div>
    {/if}
  </section>

  <section>
    <SectionHeader
      title="本局禁用干员"
      tag="{m.bannedOperators.length} 名{ruleOnly ? ' · 推算' : ''}"
      tagTone="warn"
      ontoggle={edit ? undefined : fold('banned')}
      folded={!open('banned', edit)}
    >
      {#snippet icon()}
        <img src={asset.stage('img_title_icon_chess')} alt="" class="ti" />
      {/snippet}
    </SectionHeader>
    {#if open('banned', edit)}
      <div class="banned" class:editing={edit} transition:slide={sl}>
        {#if ruleOnly}
          <p class="rule">按盟约规则推算的名单。入场自动识别只看得到禁用列表的开头，要核对真实名单：打开对局详情往下翻，点「持续识别」。</p>
        {/if}
        {#each bannedGroups as g (g.id ?? '')}
          <div class="bg">
            <div class="bgh">
              {#if g.id}
                <BondIcon id={g.id} size={20} />
                <b>{app.cov(g.id).name}</b>
              {:else}
                <b>{g.count ? '其他' : '手动添加'}</b>
              {/if}
              <span>{g.count}</span>
              <!-- 识别的格位：第几格没认出、哪格是推测、这组还没看到结尾 -->
              {#if g.missing.length}<em class="miss">{nth(g.missing)}没认出</em>{/if}
              {#if g.guessed.length}<em class="guess">{nth(g.guessed)}是推测</em>{/if}
              {#if g.more}<em class="more" title="游戏里这一组还没滚到底，持续识别时往下滚动就能补上">往下还有</em>{/if}
            </div>
            <div class="tiles">
              {#each g.cells as c (c.kind === 'op' ? c.name : `?${c.i}`)}
                {#if c.kind === 'unknown'}
                  <button
                    class="slot"
                    style:--c="var(--tier-{c.tier})"
                    onclick={() => (app.banPicker = { cov: g.id!, slot: c.i })}
                    title="第 {c.i + 1} 格没认出（{ROMAN[c.tier]} 阶）{c.n ? `，有 ${c.n} 个候选，点开选` : '，点开手动补'}"
                  >
                    <span class="q"><b>?</b><i class="term">{ROMAN[c.tier]}</i></span>
                    <span class="nm">第 {c.i + 1} 格</span>
                  </button>
                {:else if c.inferred}
                  <span class="inf">
                    <OpTile name={c.name} size={42} onclick={(e) => openMenu(e, operatorMenu(c.name))} />
                    <em>推测</em>
                  </span>
                {:else}
                  <OpTile name={c.name} size={42} onclick={() => (edit ? app.toggleBanned(c.name) : (app.detail = { kind: 'operator', name: c.name }))} />
                {/if}
              {/each}
              {#if g.id || !g.count}
                <button class="addtile" onclick={() => (app.banPicker = g.id ?? true)} title={g.id ? `补上${app.cov(g.id).name}里识别漏掉的禁用干员` : '手动添加禁用干员'}>
                  <span class="plus"><Icon name="add" size={20} /></span>
                  <span class="nm">添加</span>
                </button>
              {/if}
            </div>
          </div>
        {/each}
      </div>
    {/if}
  </section>

  <section>
    <SectionHeader title="敌方情报" en="HOSTILE INTEL" ontoggle={fold('enemy')} folded={!open('enemy')}>
      {#snippet icon()}
        <img src={asset.stage('img_title_icon_enemy')} alt="" class="ti" />
      {/snippet}
    </SectionHeader>
    {#if open('enemy')}
    <div class="enemy" transition:slide={sl}>
      {#if boss}
        <button class="boss" onclick={() => (app.detail = { kind: 'boss', id: boss.id })}>
          <span class="bpic"><img src={asset.boss(boss.id)} alt="" /></span>
          <span class="binfo">
            <span class="leader">敌方领袖</span>
            <b>{boss.name}</b>
            {#if bossHp}<span class="bhp">{m.mode!.name} · 血量 <b>{fmtHp(bossHp.hp)}</b>{#if bossHp.coreHp} · 隐秘核心 <b>{fmtHp(bossHp.coreHp)}</b>{/if}</span>{/if}
          </span>
          <span class="more term">Detail<Icon name="arrowRight" size={12} /></span>
        </button>
      {/if}
      {#each m.enemyTypes as code (code)}
        {@const t = app.catalog.enemyTypes.find((x) => x.code === code)!}
        <div class="etype">
          <span class="ei"><img src={asset.enemyType(code)} alt="" /></span>
          <span>特训敌人 · {t.name}</span>
        </div>
      {/each}
    </div>
    {/if}
  </section>

  <section>
    <SectionHeader title="本局可用阵容" en="LINEUP PLANNER" ontoggle={fold('lineups')} folded={!open('lineups')}>
      {#snippet icon()}
        <Icon name="grid" size={18} />
      {/snippet}
      {#snippet right()}
        <button class="hbtn" onclick={() => (app.setTab('lineup'), app.edit(null))}><Icon name="add" size={14} />新建阵容</button>
      {/snippet}
    </SectionHeader>
    {#if open('lineups')}
    <div class="recs" transition:slide={sl}>
      {#each usable as { l } (l.id)}
        {@const isT = app.session.targetLineupId === l.id}
        <LineupCard lineup={l} target={isT}>
          {#snippet actions()}
            {#if !isT}<button class="act" onclick={() => app.setTarget(l.id)}>设为目标</button>{/if}
          {/snippet}
        </LineupCard>
      {/each}
      <button class="all" onclick={() => (app.setTab('lineup'), (app.lineupView = 'mine'))}>我的阵容<Icon name="arrowRight" size={14} /></button>
    </div>
    {/if}
  </section>
</div>

<style>
  .page {
    display: flex;
    flex-direction: column;
    gap: 12px;
    padding: 10px 10px 24px;
  }
  /* 侧栏窄时按钮整组换到第二行靠右，不和状态文字叠在一起 */
  .status {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    justify-content: space-between;
    gap: 6px 4px;
    font-size: 12px;
  }
  .st {
    min-width: 0;
    white-space: nowrap;
    display: flex;
    align-items: center;
    gap: 6px;
    color: var(--fg-2);
  }
  .st .term {
    font-size: 9px;
    padding: 2px 4px 1px;
    color: var(--bg-0);
    background: var(--fg-4);
  }
  .st.ok .term {
    background: var(--mint);
  }
  .st .solo {
    font-size: 10px;
    font-weight: 700;
    padding: 1px 5px;
    color: var(--gold);
    box-shadow: inset 0 0 0 1px rgb(243 179 34 / 0.5);
  }
  .solo-guide {
    font-size: 12px;
    line-height: 1.55;
    color: var(--fg-2);
    padding: 8px 10px 8px 12px;
    background: rgb(243 179 34 / 0.07);
    box-shadow: inset 2px 0 0 var(--gold);
  }
  .sg-h {
    font-weight: 700;
    color: var(--gold);
    margin-bottom: 2px;
  }
  .solo-guide p {
    margin: 0;
  }
  .solo-guide ol {
    margin: 6px 0;
    padding-left: 18px;
    color: var(--fg-1);
  }
  .solo-guide li::marker {
    color: var(--gold);
    font-weight: 700;
  }
  .key {
    display: inline-grid;
    place-items: center;
    width: 22px;
    height: 16px;
    vertical-align: -3px;
    color: var(--mint);
    border-radius: 8px;
    box-shadow: inset 0 0 0 1px var(--mint);
  }
  .sg-f {
    font-size: 11px;
    color: var(--fg-3);
  }
  .st em {
    font-style: normal;
    color: var(--fg-4);
  }
  .tools {
    display: flex;
    gap: 4px;
    margin-left: auto;
  }
  .tool {
    white-space: nowrap;
    display: flex;
    align-items: center;
    gap: 4px;
    font-size: 12px;
    color: var(--fg-2);
    padding: 3px 7px;
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .tool:hover {
    color: var(--fg-1);
    box-shadow: inset 0 0 0 1px var(--fg-3);
  }
  .tool:disabled {
    opacity: 0.45;
  }
  .tool.live {
    color: var(--mint);
    box-shadow: inset 0 0 0 1px var(--mint);
    opacity: 1;
  }
  .tool.arm {
    color: #fff;
    background: var(--red);
    box-shadow: none;
  }
  .tool.on {
    color: var(--mint-ink);
    background: var(--mint);
    box-shadow: none;
  }
  .note {
    display: flex;
    align-items: flex-start;
    gap: 6px;
    font-size: 12px;
    line-height: 1.5;
    color: var(--mint);
    padding: 6px 8px;
    background: var(--mint-tint);
    box-shadow: inset 2px 0 0 var(--mint);
  }
  .note span {
    flex: 1;
  }
  .nx {
    flex: none;
    width: 20px;
    height: 20px;
    margin: -1px -4px 0 0;
    display: grid;
    place-items: center;
    color: inherit;
    opacity: 0.6;
  }
  .nx:hover {
    opacity: 1;
    background: rgb(255 255 255 / 0.08);
  }
  .note.bad {
    color: #f2b8ba;
    background: var(--red-tint);
    box-shadow: inset 2px 0 0 var(--red);
  }
  .focus {
    display: flex;
    gap: 6px;
    font-size: 12px;
    padding: 7px 8px;
  }
  .focus.ok {
    align-items: center;
    color: var(--gold);
    background: rgb(243 179 34 / 0.08);
    box-shadow: inset 2px 0 0 var(--gold);
  }
  .focus.bad {
    flex-direction: column;
    color: #f2b8ba;
    background: var(--red-tint);
    box-shadow: inset 2px 0 0 var(--red);
  }
  .fh {
    display: flex;
    align-items: center;
    gap: 5px;
    font-weight: 700;
  }
  .fl {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 6px 8px;
  }
  .fc {
    display: inline-flex;
    align-items: center;
    gap: 5px;
    color: var(--fg-1);
  }
  .fc em {
    font-style: normal;
    font-size: 11px;
    color: var(--red);
  }
  .edithint {
    font-size: 11px;
    line-height: 1.5;
    color: var(--mint);
    padding: 6px 8px;
    background: var(--mint-tint);
    box-shadow: inset 2px 0 0 var(--mint);
  }
  .editing {
    outline: 1px dashed rgb(34 227 178 / 0.45);
    outline-offset: -1px;
  }
  .hbtn {
    display: flex;
    align-items: center;
    gap: 3px;
    font-size: 12px;
    color: var(--mint);
    padding: 3px 4px;
  }
  .hbtn:hover {
    color: var(--mint-hi);
  }
  .spin {
    display: grid;
  }
  .tool.busy .spin {
    animation: spin 0.8s linear infinite;
  }
  @keyframes spin {
    to {
      transform: rotate(360deg);
    }
  }
  .stpick {
    margin-bottom: 8px;
    padding: 8px;
    background: var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .sph {
    display: flex;
    align-items: center;
    gap: 8px;
    margin-bottom: 6px;
  }
  .sph b {
    flex: none;
    font-size: 13px;
  }
  .sph input {
    flex: 1;
    min-width: 0;
    height: 26px;
    padding: 0 8px;
    font-size: 12px;
    color: var(--fg-1);
    background: var(--bg-0);
    border: 1px solid var(--line);
  }
  .stnone {
    display: flex;
    align-items: center;
    gap: 5px;
    width: 100%;
    padding: 8px 9px;
    font-size: 12.5px;
    color: var(--fg-2);
    border: 1px dashed var(--line);
  }
  .stnone:hover {
    color: var(--fg-1);
    border-color: var(--fg-4);
  }
  .stnone em {
    font-style: normal;
    font-size: 11px;
    color: var(--fg-4);
  }
  .chg {
    margin-left: auto;
    font-size: 11px;
    color: var(--mint);
  }
  .strategy {
    display: flex;
    gap: 9px;
    align-items: center;
    padding: 7px 9px;
    background: var(--hatch), var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line-soft);
  }
  .strategy img {
    width: 38px;
    height: 38px;
    object-fit: cover;
    flex: none;
    box-shadow: 0 0 0 1px var(--line);
  }
  .sn {
    display: flex;
    gap: 6px;
    align-items: baseline;
  }
  .sn b {
    font-size: 13px;
  }
  .sn span {
    font-size: 11px;
    color: var(--fg-3);
  }
  .strategy p {
    margin: 1px 0 0;
    font-size: 11px;
    color: var(--fg-3);
    line-height: 1.35;
  }
  section {
    display: flex;
    flex-direction: column;
  }
  .ti {
    width: 20px;
    height: 20px;
    object-fit: contain;
  }
  .grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(58px, 1fr));
    gap: 12px 2px;
    padding: 14px 2px 6px;
    justify-items: center;
  }
  .banned {
    display: flex;
    flex-direction: column;
    gap: 10px;
    padding: 10px 4px 4px;
  }
  .rule {
    margin: 0;
    font-size: 11px;
    line-height: 1.5;
    color: var(--gold);
    padding: 5px 8px;
    background: rgb(243 179 34 / 0.07);
    box-shadow: inset 2px 0 0 var(--gold);
  }
  .bg {
    display: flex;
    flex-direction: column;
    gap: 6px;
  }
  .bgh {
    display: flex;
    align-items: center;
    gap: 6px;
    font-size: 12px;
    color: var(--fg-2);
  }
  .bgh span {
    font-size: 11px;
    color: var(--fg-4);
  }
  .tiles {
    display: flex;
    flex-wrap: wrap;
    gap: 8px 6px;
  }
  .bgh em {
    font-style: normal;
    font-size: 11px;
    padding: 0 5px;
  }
  .bgh .miss {
    color: var(--gold-ink);
    background: var(--gold);
  }
  .bgh .guess {
    color: var(--gold);
    box-shadow: inset 0 0 0 1px rgb(243 179 34 / 0.5);
  }
  .bgh .more {
    color: var(--fg-4);
  }
  /* 没认出的格：在游戏里的原位置放问号格，金色虚线框，底边是该格星级色，角上写星级 */
  .slot {
    display: inline-flex;
    flex-direction: column;
    align-items: center;
    gap: 3px;
    width: 42px;
  }
  .q {
    position: relative;
    width: 42px;
    height: 42px;
    display: grid;
    place-items: center;
    background: rgb(243 179 34 / 0.08);
    outline: 1px dashed var(--gold);
    outline-offset: -1px;
    box-shadow: inset 0 -2px 0 var(--c);
    transition: background var(--t-fast);
  }
  .q b {
    font-size: 20px;
    line-height: 1;
    color: var(--gold);
  }
  .q i {
    position: absolute;
    left: 2px;
    bottom: 3px;
    font-style: normal;
    font-size: 9px;
    color: var(--c);
  }
  .slot .nm {
    font-size: 11px;
    color: var(--gold);
  }
  .slot:hover .q {
    background: rgb(243 179 34 / 0.2);
  }
  /* 推测的格：头像照常，金色虚线框 + 名字下面的「推测」小签，点开确认或否认 */
  .inf {
    position: relative;
    display: inline-flex;
  }
  .inf :global(.pic) {
    outline: 1px dashed var(--gold);
    outline-offset: 1px;
  }
  .inf em {
    position: absolute;
    left: 50%;
    top: 30px;
    transform: translateX(-50%);
    z-index: 2;
    font-style: normal;
    font-size: 9px;
    line-height: 12px;
    padding: 0 3px;
    white-space: nowrap;
    color: var(--gold-ink);
    background: var(--gold);
    pointer-events: none;
  }
  /* 添加格：和 42 的头像格同尺寸，虚线框 + 加号 */
  .addtile {
    display: inline-flex;
    flex-direction: column;
    align-items: center;
    gap: 3px;
    width: 42px;
    color: var(--fg-3);
  }
  .plus {
    width: 42px;
    height: 42px;
    display: grid;
    place-items: center;
    outline: 1px dashed var(--fg-4);
    outline-offset: -1px;
    transition:
      color var(--t-fast),
      outline-color var(--t-fast),
      background var(--t-fast);
  }
  .addtile .nm {
    font-size: 11px;
  }
  .addtile:hover {
    color: var(--red);
  }
  .addtile:hover .plus {
    outline-color: var(--red);
    background: var(--red-tint);
  }
  .enemy {
    display: flex;
    flex-direction: column;
    gap: 4px;
    padding-top: 8px;
  }
  .boss {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 6px;
    text-align: left;
    background: linear-gradient(90deg, #2a2a26, var(--bg-2));
    box-shadow: inset 0 0 0 1px #3b3a33;
  }
  .boss:hover {
    box-shadow: inset 0 0 0 1px var(--gold);
  }
  .bpic {
    width: 54px;
    height: 54px;
    flex: none;
    background: #fff1;
    box-shadow: 0 0 0 1px #4a4a44;
    overflow: hidden;
  }
  .bpic img {
    width: 100%;
    height: 100%;
    object-fit: cover;
  }
  .binfo {
    flex: 1;
    display: flex;
    flex-direction: column;
    align-items: flex-start;
    gap: 3px;
  }
  .leader {
    font-size: 11px;
    font-weight: 700;
    padding: 1px 8px;
    color: var(--gold-ink);
    background: var(--gold);
  }
  .binfo b {
    font-size: 15px;
  }
  .binfo .bhp {
    font-size: 11px;
    color: var(--fg-3);
    font-variant-numeric: tabular-nums;
  }
  .binfo .bhp b {
    font-size: 12px;
    color: var(--gold);
  }
  .more {
    display: flex;
    align-items: center;
    font-size: 9px;
    color: var(--fg-4);
    align-self: flex-end;
  }
  .etype {
    display: flex;
    align-items: center;
    gap: 0;
    background: var(--bg-2);
    font-size: 13px;
    font-weight: 600;
    color: var(--fg-1);
  }
  .etype > span:last-child {
    flex: 1;
    text-align: center;
    letter-spacing: 0.05em;
  }
  .ei {
    width: 32px;
    height: 32px;
    display: grid;
    place-items: center;
    background: #101113;
  }
  .ei img {
    width: 22px;
    height: 22px;
  }
  .recs {
    display: flex;
    flex-direction: column;
    gap: 6px;
    padding-top: 8px;
  }
  .all {
    display: flex;
    align-items: center;
    align-self: flex-end;
    font-size: 12px;
    color: var(--fg-3);
    padding: 2px 0;
  }
  .all:hover {
    color: var(--mint);
  }
  /* 宽（工作台）：状态行占满，下面各区块分两列从上往下排 */
  @container (min-width: 820px) {
    .page {
      display: block;
      columns: 2;
      column-gap: 16px;
    }
    .page > :global(*) {
      break-inside: avoid;
      margin-bottom: 12px;
    }
    .status {
      column-span: all;
    }
  }
</style>
