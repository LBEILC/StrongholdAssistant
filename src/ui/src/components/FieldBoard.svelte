<script lang="ts">
  import { control, MODES } from '../lib/control.svelte'
  import { slide } from 'svelte/transition'
  import { opMatches } from '../lib/opsearch'
  import { cubicOut } from 'svelte/easing'
  import { app } from '../lib/state.svelte'
  import { field, cellKey, sameCell, placeable, terrainOf } from '../lib/field.svelte'
  import { asset, ROMAN } from '../lib/assets'
  import { api, type FieldCell, type FieldUnit, type PlacementGoal, type PlacementMove } from '../lib/api'
  import SectionHeader from './SectionHeader.svelte'
  import Icon from './Icon.svelte'

  // 本局页小棋盘（任务 169，设计见 docs/design.md「本局页小棋盘」「场上认人」）：
  // 排布同游戏（棋盘 + 下面的整备区），显示认出的人、待确认的格子、本回合敌人路线；
  // 可以自己拖人推演（只在这里，不动游戏），一键回到识别结果；站位推荐接上后叠在上面
  const b = $derived(field.board)
  const st = $derived(field.state)

  // ── 视图：只看自己的棋盘（列 −1～7），或联机首领回合两边连起来（17 列）
  const LS = 'sa_fieldboard'
  const saved = (() => {
    try {
      return JSON.parse(localStorage.getItem(LS) ?? '{}') as { routes?: boolean; both?: boolean }
    } catch {
      return {}
    }
  })()
  let showRoutes = $state(saved.routes ?? true)
  let wantBoth = $state(saved.both ?? true)
  $effect(() => {
    try {
      localStorage.setItem(LS, JSON.stringify({ routes: showRoutes, both: wantBoth }))
    } catch {}
  })
  const canBoth = $derived(!!b?.bossRound && !b.solo)
  const both = $derived(canBoth && wantBoth)
  /** 两边视图里自己在右侧：整块左右翻过来 */
  const flip = $derived(both && b?.side === 'right')
  const nCols = $derived(both ? 17 : 9)
  const ROWS = [0, 1, 2, 3, 4]

  /** 自己棋盘的列 → 画面上第几列（0 起） */
  const xOf = (col: number) => (both ? (flip ? 15 - col : col + 1) : col + 1)
  /** 画面上第几列 → 自己棋盘的列；不属于自己这边的为 null */
  const colOf = (d: number) => {
    if (!both) return d - 1
    const i = flip ? 16 - d : d
    return i <= 8 ? i - 1 : null
  }
  /** 地形：能不能走（地面敌人的路）；画面第 d 列、第 row 行 */
  const walk = (d: number, row: number) => {
    if (!b) return false
    if (b.bossRound) return b.bossWalkable[row + 1]?.[both ? (flip ? 16 - d : d) : d] ?? false
    return b.walkable[row]?.[d + 1] ?? false
  }

  // ── 尺寸：按宽度算格子大小，整备区 10 格占自己这边 9 列的宽度
  let w = $state(0)
  const cell = $derived(Math.max(16, Math.min(46, Math.floor((w || 320) / nCols))))
  const benchX0 = $derived(both && flip ? 8 : 0)
  const benchW = $derived((cell * 9) / 10)

  // ── 路线：格坐标 → 画面像素（SVG 用格单位，viewBox 跟着列数）
  const px = (p: { col: number; row: number }) => [(both ? (flip ? 16 - p.col : p.col + 1) : p.col + 1), p.row] as const
  const ROUTE_COLOR = { walk: 'var(--fb-walk)', fly: 'var(--fb-fly)', boss: 'var(--fb-boss)' }
  // 飞行线往右上、首领线往左下偏一点，三种线不叠在一起（同覆盖层）
  const shift = (r: { motion: string; boss: boolean }) => (r.boss ? [-0.14, 0.14] : r.motion === 'fly' ? [0.14, -0.14] : [0, 0])
  // 飞行路线跟设置里的「飞行路线」走（只显示最多的一条 / 全部 / 不显示），和游戏里的覆盖一致
  const shown = $derived.by(() => {
    const rs = b?.routes ?? []
    const fly = app.settings.routes.fly
    const flys = rs.filter((r) => r.motion === 'fly' && !r.boss)
    const main = flys.reduce<(typeof flys)[number] | null>((m, r) => (!m || r.count > m.count ? r : m), null)
    return rs.filter((r) => r.motion !== 'fly' || r.boss || fly === 'all' || (fly === 'main' && r === main))
  })
  const routes = $derived(
    shown.map((r) => {
      const [dx, dy] = shift(r)
      const max = Math.max(...shown.map((x) => x.count), 1)
      return {
        r,
        color: ROUTE_COLOR[r.boss ? 'boss' : r.motion],
        width: r.boss ? 0.09 : 0.045 + 0.06 * (r.count / max),
        segs: r.segments.map((s) => s.map((p) => px(p)).map(([x, y]) => `${x + dx},${y + dy}`).join(' ')),
      }
    }),
  )
  // 出生点、防守点：取路线的起点和终点（巡逻的首领除外）
  const ends = $derived.by(() => {
    const spawn = new Map<string, [number, number]>()
    const goal = new Map<string, [number, number]>()
    for (const r of b?.routes ?? []) {
      if (r.boss) continue
      for (const s of r.segments) {
        const a = px(s[0]),
          z = px(s[s.length - 1])
        spawn.set(`${Math.floor(a[0])},${Math.floor(a[1])}`, [Math.floor(a[0]), Math.floor(a[1])])
        goal.set(`${Math.floor(z[0])},${Math.floor(z[1])}`, [Math.floor(z[0]), Math.floor(z[1])])
      }
    }
    return { spawn: [...spawn.values()], goal: [...goal.values()] }
  })
  const walkN = $derived((b?.routes ?? []).filter((r) => r.motion === 'walk' && !r.boss).reduce((n, r) => n + r.count, 0))
  const flyN = $derived((b?.routes ?? []).filter((r) => r.motion === 'fly' && !r.boss).reduce((n, r) => n + r.count, 0))

  // ── 人：推演后的位置；推荐里有、但人不在推荐格上的，画成虚影
  let showPlan = $state(true)
  const atCell = (c: FieldCell) => field.layout.find((x) => sameCell(x.at, c))
  // 召唤物（任务 291，如鸟笼）：不是干员，画成淡色的圆，写名字第一个字；推演拖人时那格照样当有东西
  const summonAt = (c: FieldCell) => st?.summons?.find((s) => sameCell(s, c))
  // 推荐：每条 move 按 from 认人；人现在（含推演）不在推荐格上的，在推荐格上画虚影
  const placed = (m: PlacementMove) => field.layout.some((x) => sameCell(x.unit, m.from) && sameCell(x.at, m.to))
  const ghosts = $derived(showPlan && field.plan ? field.plan.moves.filter((m) => !placed(m)).map((m) => ({ ...m.to, operator: m.operator, facing: m.facing })) : [])
  /** 已经站在推荐格上的人的朝向（显示小箭头） */
  const facingOf = (u: FieldUnit, at: FieldCell) =>
    showPlan ? (field.plan?.moves.find((m) => sameCell(m.from, u) && sameCell(m.to, at))?.facing ?? null) : null
  // 推荐目标（Settings.placementGoal）：改了宿主重算；旧宿主没有这一项时按平衡
  const GOALS = [
    { id: 'leak', label: '漏怪最少', tip: '尽量一只不漏，不管叠层' },
    { id: 'balanced', label: '平衡', tip: '守住为主，兼顾叠层' },
    { id: 'layers', label: '叠层最多', tip: '尽量多叠盟约层数，可能多漏几只' },
  ] as const
  const goal = $derived(app.settings.placementGoal ?? 'balanced')
  const setGoal = (g: PlacementGoal) => {
    app.settings.placementGoal = g
    app.saveSettings()
  }
  // 场上自动识别开关（Settings.fieldAuto，v1.0.1）：关着时场上停在上一次，只有点「识别」才认；旧宿主没有这一项时按开着
  const auto = $derived(app.settings.fieldAuto ?? true)
  const busy = $derived(field.recognizing)
  const keyTip = $derived(app.settings.fieldHotkey ? `（${app.settings.fieldHotkey}）` : '')
  const hhmm = (iso: string) => new Date(iso).toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' })
  const moveN = $derived(field.plan ? field.plan.moves.filter((m) => !sameCell(m.from, m.to)).length : 0)
  const ghostAt = (c: FieldCell) => ghosts.find((g) => sameCell(g, c))

  // ── 拖动推演：按下后移动超过 4px 才算拖，否则是点（打开确认面板）
  let drag = $state<{ unit: FieldUnit; x: number; y: number; moved: boolean; over: FieldCell | null } | null>(null)
  let downAt = { x: 0, y: 0 }
  const cellUnder = (x: number, y: number): FieldCell | null => {
    const el = (document.elementFromPoint(x, y) as HTMLElement | null)?.closest<HTMLElement>('[data-cell]')
    if (!el) return null
    const [zone, col, row] = el.dataset.cell!.split(':')
    return { zone: zone as FieldCell['zone'], col: +col, row: +row }
  }
  function down(e: PointerEvent, unit: FieldUnit) {
    if (e.button !== 0) return
    ;(e.currentTarget as HTMLElement).setPointerCapture(e.pointerId)
    downAt = { x: e.clientX, y: e.clientY }
    drag = { unit, x: e.clientX, y: e.clientY, moved: false, over: null }
  }
  function moveP(e: PointerEvent) {
    if (!drag) return
    const moved = drag.moved || Math.hypot(e.clientX - downAt.x, e.clientY - downAt.y) > 4
    drag = { ...drag, x: e.clientX, y: e.clientY, moved, over: moved ? cellUnder(e.clientX, e.clientY) : null }
  }
  // 拖动时跟着鼠标的小人挂到 body 下（v1.0.1）：内容区的入场动画（App .shell.in，fill both）一直挂着 transform，
  // 里面的 fixed 会按内容区定位，比鼠标低一截（标题栏 + 追踪栏的高度）

  const portal = (node: HTMLElement) => {
    document.body.appendChild(node)
    return { destroy: () => node.remove() }
  }
  function up() {
    if (!drag) return
    const d = drag
    drag = null
    if (!d.moved) return pick(d.unit)
    if (d.over && placeable(b, d.over) && !summonAt(d.over)) field.move(d.unit, d.over)
  }

  // ── 点选确认：点一格打开下面的面板；前 3 名 + 「都不是」（这一阶全部干员，可搜索）
  let sel = $state<string | null>(null)
  let all = $state(false)
  let q = $state('')
  const selUnit = $derived(field.units.find((u) => cellKey(u) === sel) ?? null)
  function pick(u: FieldUnit) {
    sel = sel === cellKey(u) ? null : cellKey(u)
    addCell = null
    all = false
    q = ''
  }

  // ── 手动加人（任务 278）：认不出的空格子点一下，从本局名单或全部干员里选一个放进去
  let addCell = $state<FieldCell | null>(null)
  let addTier = $state<number | null>(null)
  function addAt(c: FieldCell) {
    addCell = addCell && sameCell(addCell, c) ? null : c
    sel = null
    addTier = null
    q = ''
  }
  // 本局名单里还没在场上的人（买过、送的、确认过的）
  const addRoster = $derived.by(() => {
    const onField = new Set(field.units.map((u) => u.operator))
    return [...new Set((st?.roster ?? []).filter((r) => !r.sold && !onField.has(r.operator)).map((r) => r.operator))].slice(0, 8)
  })
  const addAll = $derived(
    addCell
      ? app.catalog.operators
          .filter((o) => (addTier === null || o.tier === addTier) && opMatches(o.name, q))
          .map((o) => o.name)
      : [],
  )
  async function confirmAdd(name: string) {
    const c = addCell
    if (!c) return
    try {
      await field.confirm(c, name)
      addCell = null
    } catch (e) {
      app.showToast((e as Error).message, true)
    }
  }
  const pickNext = () => {
    const next = field.pending.find((u) => cellKey(u) !== sel) ?? field.pending[0]
    if (next) pick(next)
  }
  // 候选：待确认、自动的给识别的前 3；其他（确定的、认不出的）给本局有的同阶的人
  const choices = $derived.by(() => {
    const u = selUnit
    if (!u) return []
    if (u.candidates.length) return u.candidates.map((c) => c.operator)
    return [...new Set((st?.roster ?? []).filter((r) => !r.sold && app.op(r.operator)?.tier === u.tier).map((r) => r.operator))].slice(0, 6)
  })
  const tierAll = $derived(
    selUnit
      ? app.catalog.operators.filter((o) => o.tier === selUnit.tier && opMatches(o.name, q)).map((o) => o.name)
      : [],
  )
  async function confirm(name: string | null) {
    const u = selUnit
    if (!u) return
    try {
      await field.confirm(u, name)
      sel = null
      if (name && field.pending.length) app.showToast(`已确认：${name}。还有 ${field.pending.length} 格待确认`)
    } catch (e) {
      app.showToast((e as Error).message, true)
    }
  }
  const FACE = { up: '上', down: '下', left: '左', right: '右' }
  const where = (c: FieldCell) => (c.zone === 'bench' ? `整备区第 ${c.col} 格` : `棋盘 ${c.col + 1} 列 ${c.row + 1} 行`)
  const SRC: Record<string, string> = {
    purchase: '买进时读到商店卡',
    drag: '拖动时读到名字',
    manual: '你点选确认的',
    carry: '从别的格子挪过来，名字跟着走',
    merge: '三合一后沿用',
    only: '这一阶本局只有这一个人',
    sprite: '按小人样子比对的',
  }
  const statusLine = (u: FieldUnit) =>
    u.status === 'pending'
      ? '待确认：没把握，先按第 1 名算'
      : u.status === 'auto'
        ? '自动认的，把握比较大；不对就改'
        : u.status === 'unknown'
          ? '本局有的人里没对上，点下面的人或「都不是」'
          : (SRC[u.source ?? ''] ?? '已确认')

  const open = $derived(!app.folded['match.field'])
  const sl = { duration: 200, easing: cubicOut }

  // 帮助改进识别（v1.0.5，任务 235）：开着采集时，先把认错的格子改对，再点「都对了」，这一刻整盘名单记成答案
  let confirming = $state(false)
  async function confirmAll() {
    confirming = true
    try {
      await api.confirmField()
      app.showToast('记下了，谢谢帮忙')
    } catch (e) {
      app.showToast((e as Error).message, true)
    } finally {
      confirming = false
    }
  }
</script>

{#snippet unitFace(u: FieldUnit)}
  <span class="face" class:pending={u.status === 'pending'} class:unknown={u.status === 'unknown'} style:--c="var(--tier-{u.tier})">
    {#if u.operator}<img src={asset.operator(u.operator)} alt="" draggable="false" />{/if}
    <i class="tier">{ROMAN[u.tier]}</i>
    {#if u.status === 'pending' || u.status === 'unknown'}<b class="q">?</b>{/if}
    {#if u.status === 'auto'}<em class="auto">自动</em>{/if}
  </span>
{/snippet}

{#snippet slotBody(c: FieldCell)}
  {@const x = atCell(c)}
  {@const s = !x ? summonAt(c) : undefined}
  {@const g = !x && !s ? ghostAt(c) : undefined}
  {#if s}
    <span class="summon" title="召唤物：{s.name}{s.owner ? `（${s.owner} 的）` : ''}。不是干员，站位推荐不会往这格放人">
      <b>{[...s.name][0]}</b>
    </span>
  {:else if x}
    <button
      class="unit"
      class:moved={!sameCell(x.at, x.unit)}
      class:sel={sel === cellKey(x.unit)}
      class:dragging={drag?.moved && drag.unit === x.unit}
      onpointerdown={(e) => down(e, x.unit)}
      onpointermove={moveP}
      onpointerup={up}
      onpointercancel={() => (drag = null)}
      title="{x.unit.operator ?? '不知道是谁'} · {ROMAN[x.unit.tier]} 阶 · {statusLine(x.unit)}。点一下改是谁，拖动可以推演站位"
    >
      {@render unitFace(x.unit)}
      {#if x.at.zone === 'board' && facingOf(x.unit, x.at)}<i class="dir {facingOf(x.unit, x.at)}"></i>{/if}
    </button>
  {:else if c.zone === 'bench' || placeable(b, c)}
    <button
      class="add"
      class:sel={!!addCell && sameCell(addCell, c)}
      onclick={() => addAt(c)}
      title="{g ? `推荐：${g.operator}${g.facing ? `，朝${FACE[g.facing]}` : ''}。` : ''}这格有人但没认出来？点一下加上"
    >
      {#if g}
        <span class="plan">
          <img src={asset.operator(g.operator)} alt="" draggable="false" />
          {#if g.facing}<i class="dir {g.facing}"></i>{/if}
        </span>
      {:else}
        <Icon name="add" size={12} />
      {/if}
    </button>
  {/if}
{/snippet}

<section>
  <SectionHeader title="场上" en="FIELD" ontoggle={() => app.toggleFold('match.field')} folded={!open} tag={field.pending.length ? `${field.pending.length} 格待确认` : undefined} tagTone="warn">
    {#snippet right()}
      <button
        class="scan"
        class:manual={!auto}
        class:busy
        disabled={busy}
        onclick={() => field.recognize()}
        title={auto ? `现在认一次场上，重算推荐${keyTip}` : `点一下认一次场上，算一次推荐${keyTip}`}
      ><Icon name="scan" size={13} />{busy ? '识别中' : '识别'}</button>
      {#if b}
        <button class="chip" class:on={showRoutes} onclick={() => (showRoutes = !showRoutes)} title="在小棋盘上画本回合敌人路线（和游戏里的路线覆盖分开）"><i></i>路线</button>
      {/if}
    {/snippet}
  </SectionHeader>
  {#if open}
    <div class="fb" transition:slide={sl}>
      {#if !st && !b}
        <p class="empty">{auto ? '进休整期后自动认出场上和整备区的人，显示在这块小棋盘上。' : `休整期在棋盘画面点右上角「识别」${app.settings.fieldHotkey ? `或按 ${app.settings.fieldHotkey}` : ''}，认一次场上和整备区的人。`}</p>
      {:else}
        <!-- 顶栏：回合、视图切换、推演状态 -->
        <div class="bar">
          <span class="meta">
            {#if b?.round}第 {b.round} 回合{#if b.bossRound} · 首领{/if}{/if}
            {#if !auto && st}<small class="man" title="场上停在这次识别的结果，摆好人后再点「识别」更新">手动 · {hhmm(st.updatedAt)}</small>{/if}
            {#if showRoutes && (walkN || flyN)}<small><i class="lg walk"></i>地面 {walkN}<i class="lg fly"></i>飞行 {flyN}</small>{/if}
          </span>
          {#if canBoth}
            <span class="seg">
              <button class:on={!both} onclick={() => (wantBoth = false)}>我的</button>
              <button class:on={both} onclick={() => (wantBoth = true)}>两边</button>
            </span>
          {/if}
          {#if app.settings.collect && st}
            <button class="ok" disabled={confirming} onclick={confirmAll} title="帮助改进识别：先把认错的格子点开改对，再点这里，把这一刻场上和整备区的名单记成答案"><Icon name="check" size={12} />{confirming ? '记录中' : '都对了'}</button>
          {/if}
        </div>
        {#if field.pending.length}
          <button class="ask" onclick={pickNext}>
            <b>?</b><span><strong>{field.pending.length} 格待确认</strong>认不准，先按最像的算；点一下选是谁，确认过的不再问</span>
          </button>
        {/if}

        <!-- 棋盘 + 整备区 -->
        <div class="stage" bind:clientWidth={w} style:--cell="{cell}px" style:--n={nCols} class:drag={drag?.moved}>
          {#if both}
            <div class="sides" style:grid-template-columns="repeat(17, var(--cell))">
              <span style:grid-area="1 / {flip ? 9 : 1} / 2 / span 9">我的</span>
              <span style:grid-area="1 / {flip ? 1 : 10} / 2 / span 8">队友</span>
            </div>
          {/if}
          <div class="board" style:grid-template-columns="repeat({nCols}, var(--cell))">
            {#each ROWS as row (row)}
              {#each { length: nCols } as _, d (d)}
                {@const col = colOf(d)}
                {@const mine = col !== null && col >= 0 && col <= 6}
                {@const c = { zone: 'board' as const, col: col ?? -9, row }}
                {@const t = col === null ? null : terrainOf(b, col, row)}
                {@const ok = mine && placeable(b, c)}
                <div
                  class="tile"
                  class:walk={t ? 'GTDS'.includes(t) : walk(d, row)}
                  class:high={t === 'H'}
                  class:special={t === 'T'}
                  class:edge={!ok}
                  class:other={col === null}
                  class:ok
                  class:over={ok && drag?.over && sameCell(drag.over, c)}
                  data-cell={ok ? `board:${col}:${row}` : undefined}
                  title={t === 'H' ? '高台：只能放远程' : t === 'T' ? '特殊地面：能放人，地图有特殊效果' : undefined}
                >
                  {#if mine}{@render slotBody(c)}{/if}
                </div>
              {/each}
            {/each}
            {#if b && showRoutes}
              <svg class="routes" viewBox="0 0 {nCols} 5" preserveAspectRatio="none" aria-hidden="true">
                <defs>
                  {#each ['walk', 'fly', 'boss'] as k (k)}
                    <marker id="fb-ar-{k}" viewBox="0 0 10 10" refX="7" refY="5" markerWidth="4" markerHeight="4" orient="auto-start-reverse">
                      <path d="M0 0L10 5L0 10z" fill={ROUTE_COLOR[k as 'walk']} />
                    </marker>
                  {/each}
                </defs>
                {#each ends.spawn as [x, y] (`s${x},${y}`)}<rect class="spawn" x={x + 0.1} y={y + 0.1} width="0.8" height="0.8" />{/each}
                {#each ends.goal as [x, y] (`g${x},${y}`)}<rect class="goal" x={x + 0.1} y={y + 0.1} width="0.8" height="0.8" />{/each}
                {#each routes as rt, i (i)}
                  {#each rt.segs as pts, j (j)}
                    <polyline
                      points={pts}
                      class:fly={rt.r.motion === 'fly'}
                      class:approx={rt.r.approx}
                      stroke={rt.color}
                      stroke-width={rt.width}
                      marker-end={rt.r.patrol ? undefined : `url(#fb-ar-${rt.r.boss ? 'boss' : rt.r.motion})`}
                    />
                  {/each}
                {/each}
                {#each b.bossSpots as p (`${p.col},${p.row}`)}
                  {@const [x, y] = px(p)}
                  <rect class="bspot" x={x - 0.22} y={y - 0.22} width="0.44" height="0.44" transform="rotate(45 {x} {y})" />
                {/each}
              </svg>
            {/if}
          </div>
          <div class="bench" style:margin-left="calc({benchX0} * var(--cell))" style:width="calc(9 * var(--cell))" style:--bw="{benchW}px">
            {#each { length: 10 } as _, i (i)}
              {@const c = { zone: 'bench' as const, col: i + 1, row: 0 }}
              <div class="slot" class:over={drag?.over && sameCell(drag.over, c)} data-cell="bench:{i + 1}:0">
                {@render slotBody(c)}
              </div>
            {/each}
          </div>
        </div>

        <!-- 点选确认（不弹窗，展开在棋盘下面） -->
        {#if selUnit}
          <div class="pick" transition:slide={sl}>
            <div class="ph">
              <span class="pt"><b>{where(selUnit)} · {ROMAN[selUnit.tier]} 阶</b><small>{statusLine(selUnit)}</small></span>
              <button class="x" onclick={() => (sel = null)} aria-label="关闭"><Icon name="close" size={14} /></button>
            </div>
            {#if !all}
              <div class="cands">
                {#each choices as name, i (name)}
                  <button class="cand" class:cur={name === selUnit.operator} onclick={() => confirm(name)}>
                    <img src={asset.operator(name)} alt="" />
                    <span>{name}</span>
                    {#if selUnit.candidates.length && i === 0}<em>最像</em>{:else if name === selUnit.operator}<em>现在</em>{/if}
                  </button>
                {/each}
                <button class="cand none" onclick={() => (all = true)}>
                  <span class="nq">?</span>
                  <span>都不是</span>
                  <em>{ROMAN[selUnit.tier]} 阶全部</em>
                </button>
              </div>
            {:else}
              <label class="sr">
                <Icon name="search" size={13} />
                <input placeholder="搜 {ROMAN[selUnit.tier]} 阶干员" bind:value={q} />
              </label>
              <div class="alllist">
                {#each tierAll as name (name)}
                  <button class="mini" class:cur={name === selUnit.operator} onclick={() => confirm(name)} title={name}>
                    <img src={asset.operator(name)} alt="" loading="lazy" />
                    <span>{name}</span>
                  </button>
                {:else}
                  <p class="empty">没有叫这个的 {ROMAN[selUnit.tier]} 阶干员</p>
                {/each}
              </div>
              <button class="back" onclick={() => (all = false)}><Icon name="arrowLeft" size={13} />回到候选</button>
            {/if}
            {#if selUnit.operator}<button class="clear" onclick={() => confirm(null)}>{selUnit.source === 'manual' ? '移除（这格其实没人，或改成不知道是谁）' : '改成不知道是谁'}</button>{/if}
          </div>
        {:else if addCell}
          <div class="pick" transition:slide={sl}>
            <div class="ph">
              <span class="pt"><b>{where(addCell)} · 加一个人</b><small>认不出的格子，选这里站的是谁；之后识别看不到也会留着，看到别人才换</small></span>
              <button class="x" onclick={() => (addCell = null)} aria-label="关闭"><Icon name="close" size={14} /></button>
            </div>
            {#if addRoster.length}
              <div class="cands">
                {#each addRoster as name (name)}
                  <button class="cand" onclick={() => confirmAdd(name)}>
                    <img src={asset.operator(name)} alt="" />
                    <span>{name}</span>
                    <em>本局有</em>
                  </button>
                {/each}
              </div>
            {/if}
            <div class="tiers">
              <button class:on={addTier === null} onclick={() => (addTier = null)}>全部</button>
              {#each [1, 2, 3, 4, 5, 6] as t (t)}
                <button class:on={addTier === t} style:--c="var(--tier-{t})" onclick={() => (addTier = t)}>{ROMAN[t]}</button>
              {/each}
            </div>
            <label class="sr">
              <Icon name="search" size={13} />
              <input placeholder="搜干员" bind:value={q} />
            </label>
            <div class="alllist">
              {#each addAll as name (name)}
                <button class="mini" onclick={() => confirmAdd(name)} title={name}>
                  <img src={asset.operator(name)} alt="" loading="lazy" />
                  <span>{name}</span>
                </button>
              {:else}
                <p class="empty">没有叫这个的干员</p>
              {/each}
            </div>
          </div>
        {/if}
        <!-- 推演、推荐 -->
        <div class="foot">
          {#if field.sandbox}
            <span class="sbx"><i></i>推演中 · 不影响游戏</span>
            <button class="act" onclick={() => field.reset()}><Icon name="refresh" size={13} />回到识别结果</button>
          {:else}
            <span class="hint">拖动可以自己摆着看，不影响游戏</span>
          {/if}
        </div>
        <div class="rec" class:none={!field.plan}>
          {#if field.plan}
            {@const pl = field.plan}
            {@const rec = pl.recommended}
            {@const worst = rec && rec.streams.length > 1 ? rec.streams.reduce((a, x) => (x.leaked > a.leaked ? x : a)) : null}
            <div class="rh">
              <b>站位推荐</b>
              <span class="seg goal">
                {#each GOALS as g (g.id)}
                  <button class:on={goal === g.id} onclick={() => setGoal(g.id)} title={g.tip}>{g.label}</button>
                {/each}
              </span>
            </div>
            <div class="rh">
              {#if pl.goal !== goal}<em class="stale">正在按「{GOALS.find((g) => g.id === goal)?.label}」重算…</em>
              {:else if field.planStale}<em class="stale">场上变了，等重新算</em>
              {:else}<em class="mv">{moveN ? `游戏里要挪 ${moveN} 人` : '现在就是推荐站位'}</em>{/if}
              <span class="sp"></span>
              <button class="chip" class:on={showPlan} onclick={() => (showPlan = !showPlan)}><i></i>虚影</button>
              {#if moveN}<button class="act" onclick={() => field.applyPlan()} title="在这块小棋盘上照推荐摆着看，不动游戏"><Icon name="magic" size={13} />小棋盘上摆</button>{/if}
              {#if moveN && control.settings.enabled}
                <button
                  class="act game"
                  disabled={field.planStale || pl.goal !== goal || control.running}
                  title={field.planStale ? '场上变了，等重新算完再摆' : control.running ? '上一批操作还没做完' : `在游戏里照推荐挪这 ${moveN} 人（${MODES.find((m) => m.id === control.settings.mode)?.label}）`}
                  onclick={() => control.run(() => api.runPlacement())}
                ><Icon name="play" size={12} />游戏里摆</button>
              {/if}
            </div>
            {#if rec}
              <p class="sim">
                {#if pl.current && moveN}现在这样摆预计漏 <b class:bad={pl.current.leaked > 0}>{pl.current.leaked}</b> 只 → 照推荐漏 <b class:bad={rec.leaked > 0}>{rec.leaked}</b> 只
                {:else}预计漏 <b class:bad={rec.leaked > 0}>{rec.leaked}</b> 只{/if}
                <small>（本回合约 {rec.enemies} 只敌人）</small>
              </p>
              {#if worst}<p class="sim sub">最坏：{worst.label}（{Math.round(worst.chance * 100)}%）漏 <b class:bad={worst.leaked > 0}>{worst.leaked}</b> 只</p>{/if}
              {#if rec.layers.length}
                <p class="sim sub">叠层：{#each rec.layers as l, i (l.covenant)}{i ? ' · ' : ''}{app.cov(l.covenant)?.name ?? l.covenant} <b>{l.layers}</b> 层{/each}</p>
              {/if}
            {/if}
            <p class="asm">
              {#if field.plan.guessed}有 {field.plan.guessed} 格是猜的（待确认的按最像的算）。{/if}
              {#if field.plan.skipped.length}{field.plan.skipped.length} 格认不出或没有资料，没算进去。{/if}
              按这些假设算的：{field.plan.assumptions.join('；')}。结果是近似推荐，漏 0 只也不等于一定守住。
            </p>
          {:else}
            <b>站位推荐</b><span>{field.planPending ? '正在算，算好会显示在这块棋盘上…' : '休整时点「识别」，会在这块棋盘上给出推荐站位'}</span>
          {/if}
        </div>

      {/if}
    </div>
  {/if}
</section>

<!-- 拖动时跟着鼠标的头像 -->
{#if drag?.moved}
  <div class="float" use:portal style:left="{drag.x}px" style:top="{drag.y}px" style:--cell="{cell}px">{@render unitFace(drag.unit)}</div>
{/if}

<style>
  .fb {
    --fb-walk: #ff5a4e;
    --fb-fly: #c79bff;
    --fb-boss: var(--gold);
    --fb-goal: #4fb6ff;
    display: flex;
    flex-direction: column;
    gap: 8px;
    padding-top: 8px;
  }
  .empty {
    margin: 0;
    font-size: var(--fs-sm);
    color: var(--fg-3);
  }
  .bar {
    display: flex;
    align-items: center;
    gap: 8px;
    font-size: var(--fs-sm);
    color: var(--fg-2);
  }
  .meta {
    flex: 1;
    display: flex;
    align-items: center;
    gap: 8px;
    min-width: 0;
  }
  .meta small {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    color: var(--fg-3);
    font-size: var(--fs-xs);
  }
  .lg {
    width: 10px;
    height: 2px;
    margin-left: 4px;
    background: var(--fb-walk);
  }
  .lg.fly {
    background: repeating-linear-gradient(90deg, var(--fb-fly) 0 3px, transparent 3px 5px);
  }
  /* 「都对了」：只在开着采集时出现，薄荷色描边，和「识别」手动态一个调子 */
  .ok {
    display: inline-flex;
    align-items: center;
    gap: 3px;
    padding: 1px 7px;
    font-size: var(--fs-xs);
    color: var(--mint);
    box-shadow: inset 0 0 0 1px rgb(var(--mint-rgb) / 0.45);
  }
  .ok:hover:not(:disabled) {
    background: var(--mint-tint);
  }
  .ok:disabled {
    opacity: 0.5;
    cursor: default;
  }
  .seg {
    display: inline-flex;
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .seg button {
    padding: 1px 8px;
    font-size: var(--fs-xs);
    color: var(--fg-3);
  }
  .seg button.on {
    background: var(--mint-tint);
    color: var(--mint);
    box-shadow: inset 0 0 0 1px rgb(var(--mint-rgb) / 0.5);
  }
  /* 「识别」：自动开着时是不起眼的文字按钮；关着时它是唯一的入口，用薄荷色 */
  .scan {
    display: flex;
    align-items: center;
    gap: 4px;
    padding: 2px 6px;
    font-size: var(--fs-sm);
    color: var(--fg-3);
  }
  .scan:hover:not(:disabled) {
    color: var(--fg-1);
  }
  .scan.manual {
    color: var(--mint);
    box-shadow: inset 0 0 0 1px rgb(var(--mint-rgb) / 0.45);
  }
  .scan.manual:hover:not(:disabled) {
    background: var(--mint-tint);
    color: var(--mint);
  }
  .scan.busy {
    cursor: default;
    animation: scan-pulse 1.2s ease-in-out infinite;
  }
  @keyframes scan-pulse {
    50% {
      opacity: 0.4;
    }
  }
  .man {
    color: var(--fg-4);
  }
  .chip {
    display: flex;
    align-items: center;
    gap: 5px;
    padding: 2px 6px;
    font-size: var(--fs-sm);
    color: var(--fg-3);
  }
  .chip i {
    width: 6px;
    height: 6px;
    border-radius: 50%;
    background: var(--fg-4);
  }
  .chip.on {
    color: var(--mint);
  }
  .chip.on i {
    background: var(--mint);
    box-shadow: 0 0 6px rgb(var(--mint-rgb) / 0.8);
  }
  .chip:hover {
    color: var(--fg-1);
  }

  /* 待确认提示条 */
  .ask {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 6px 8px;
    text-align: left;
    background: rgb(var(--gold-rgb) / 0.1);
    box-shadow: inset var(--edge) 0 0 var(--gold);
    font-size: var(--fs-sm);
    color: var(--fg-2);
  }
  .ask b {
    flex: none;
    display: grid;
    place-items: center;
    width: 18px;
    height: 18px;
    border-radius: 50%;
    background: var(--gold);
    color: var(--gold-ink);
    font-size: var(--fs-base);
  }
  .ask strong {
    color: var(--gold);
    margin-right: 6px;
  }
  .ask:hover {
    background: rgb(var(--gold-rgb) / 0.16);
  }

  /* 棋盘 */
  .stage {
    position: relative;
    display: flex;
    flex-direction: column;
    align-items: center;
    user-select: none;
    touch-action: none;
  }
  .sides {
    display: grid;
    font-size: var(--fs-2xs);
    color: var(--fg-3);
    text-align: center;
    margin-bottom: 2px;
  }
  .board {
    position: relative;
    display: grid;
    grid-auto-rows: var(--cell);
    box-shadow: 0 0 0 1px var(--line);
    background: var(--bg-1);
  }
  .tile {
    position: relative;
    display: grid;
    place-items: center;
    /* 高台：不能走的格子，亮一点、带斜纹和立体边 */
    background: var(--bg-4) var(--hatch);
    box-shadow:
      inset 1px 1px 0 rgb(var(--hl-rgb) / 0.1),
      inset -1px -2px 0 rgb(var(--shade-rgb) / 0.45);
  }
  /* 地面：敌人走的路，压暗、凹下去 */
  .tile.walk {
    background: var(--bg-0);
    box-shadow: inset 0 0 0 1px rgb(var(--hl-rgb) / 0.03);
  }
  /* 高台（cells 'H'）：只能放远程，右上角一个小三角 */
  .tile.high::after {
    content: '';
    position: absolute;
    top: 2px;
    right: 2px;
    border: max(3px, calc(var(--cell) * 0.09)) solid transparent;
    border-top-color: var(--fg-3);
    border-right-color: var(--fg-3);
    opacity: 0.7;
  }
  /* 特殊地面（cells 'T'）：能放人，淡金色 */
  .tile.special {
    background: rgb(var(--gold-rgb) / 0.1);
    box-shadow: inset 0 0 0 1px rgb(var(--gold-rgb) / 0.18);
  }
  /* 不能放人的格子（障碍、行 4、防守点和出生点那两列、另一边）：淡下去 */
  .tile.edge {
    opacity: 0.45;
  }
  /* 拖人时只有能放的格子亮着 */
  .drag .tile.edge {
    opacity: 0.2;
  }
  .drag .tile.ok {
    box-shadow: inset 0 0 0 1px rgb(var(--mint-rgb) / 0.35);
  }
  .tile.other {
    opacity: 0.35;
  }
  .tile.over,
  .slot.over {
    box-shadow: inset 0 0 0 2px var(--mint);
    background-color: var(--mint-tint);
  }
  .routes {
    position: absolute;
    inset: 0;
    width: 100%;
    height: 100%;
    pointer-events: none;
    overflow: visible;
  }
  .routes polyline {
    fill: none;
    stroke-linejoin: round;
    stroke-linecap: round;
    opacity: 0.85;
  }
  .routes polyline.fly {
    stroke-dasharray: 0.16 0.1;
  }
  .routes polyline.approx {
    opacity: 0.6;
  }
  .routes .spawn,
  .routes .goal {
    fill: none;
    stroke-width: 0.05;
  }
  .routes .spawn {
    stroke: var(--fb-walk);
    fill: rgb(255 90 78 / 0.12);
  }
  .routes .goal {
    stroke: var(--fb-goal);
    fill: rgb(79 182 255 / 0.12);
  }
  .routes .bspot {
    fill: var(--fb-boss);
    opacity: 0.9;
  }
  .drag .routes {
    opacity: 0.35;
  }

  /* 整备区：棋盘下面的木板 */
  .bench {
    display: flex;
    margin-top: calc(var(--cell) * 0.18);
    padding: 3px 0;
    background: linear-gradient(var(--bg-4), var(--bg-3));
    box-shadow: 0 0 0 1px var(--line);
  }
  .slot {
    position: relative;
    display: grid;
    place-items: center;
    width: var(--bw);
    height: var(--bw);
    box-shadow: inset -1px 0 0 rgb(var(--shade-rgb) / 0.3);
  }
  .slot:last-child {
    box-shadow: none;
  }

  /* 格子里的人 */
  .unit {
    position: relative;
    z-index: 1;
    display: grid;
    place-items: center;
    width: 100%;
    height: 100%;
    cursor: grab;
  }
  .face {
    position: relative;
    display: block;
    width: 86%;
    height: 86%;
    background: var(--bg-4);
    box-shadow:
      inset 0 0 0 1px var(--line),
      inset 0 -2px 0 var(--c);
    overflow: hidden;
  }
  .face img {
    width: 100%;
    height: 100%;
    object-fit: cover;
    pointer-events: none;
  }
  .tier {
    position: absolute;
    left: 0;
    top: 0;
    padding: 0 2px;
    font-style: normal;
    font-family: var(--font-term);
    font-size: max(8px, calc(var(--cell) * 0.24));
    line-height: 1.15;
    color: var(--c);
    background: rgb(var(--shade-rgb) / 0.7);
  }
  .face.pending img {
    opacity: 0.55;
  }
  .face.unknown {
    background: var(--bg-3) var(--hatch);
  }
  /* 待确认、认不出：金色问号 */
  .q {
    position: absolute;
    right: -1px;
    top: -1px;
    display: grid;
    place-items: center;
    width: max(12px, calc(var(--cell) * 0.4));
    height: max(12px, calc(var(--cell) * 0.4));
    border-radius: 50%;
    background: var(--gold);
    color: var(--gold-ink);
    font-size: max(9px, calc(var(--cell) * 0.3));
    font-style: normal;
    box-shadow: 0 0 6px rgb(var(--gold-rgb) / 0.7);
  }
  .face.unknown .q {
    inset: 0;
    margin: auto;
    background: var(--bg-5);
    color: var(--fg-1);
    box-shadow: none;
  }
  .auto {
    position: absolute;
    left: 0;
    right: 0;
    bottom: 2px;
    font-style: normal;
    font-size: var(--fs-3xs);
    line-height: 1.3;
    text-align: center;
    color: var(--fg-1);
    background: rgb(var(--shade-rgb) / 0.65);
  }
  .unit:hover .face {
    box-shadow:
      inset 0 0 0 1px var(--line-hi),
      inset 0 -2px 0 var(--c);
  }
  .unit.sel .face {
    box-shadow:
      inset 0 0 0 2px var(--mint),
      inset 0 -2px 0 var(--c);
  }
  /* 推演里挪过的：薄荷色虚线框 */
  .unit.moved::after {
    content: '';
    position: absolute;
    inset: 1px;
    border: 1px dashed var(--mint);
    pointer-events: none;
  }
  .unit.dragging {
    opacity: 0.25;
  }
  /* 推荐的虚影 */
  /* 空格子：平时看不见，鼠标移上去露出「+」，点开手动加人 */
  .add {
    display: grid;
    place-items: center;
    width: 100%;
    height: 100%;
    color: transparent;
  }
  .add:hover,
  .add.sel {
    color: var(--fg-3);
    background: color-mix(in srgb, var(--mint) 10%, transparent);
  }
  .add.sel {
    outline: 1px solid var(--mint);
    outline-offset: -1px;
  }
  .tiers {
    display: flex;
    gap: 4px;
  }
  .tiers button {
    flex: 1;
    padding: 3px 0;
    font-size: var(--fs-xs);
    color: var(--fg-2);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .tiers button.on {
    color: var(--fg-0);
    box-shadow: inset 0 0 0 1px var(--c, var(--mint));
    background: color-mix(in srgb, var(--c, var(--mint)) 18%, transparent);
  }
  .summon {
    display: grid;
    place-items: center;
    width: 70%;
    aspect-ratio: 1;
    border-radius: 50%;
    border: 1px dashed var(--fg-2);
    background: color-mix(in srgb, var(--fg-2) 22%, transparent);
    color: var(--fg-1);
    font-size: var(--fs-xs);
    cursor: default;
  }
  .plan {
    position: relative;
    display: block;
    width: 80%;
    height: 80%;
    opacity: 0.45;
    outline: 1px dashed var(--mint);
    outline-offset: 1px;
  }
  .plan img {
    width: 100%;
    height: 100%;
    object-fit: cover;
    filter: grayscale(0.4);
  }
  .float {
    position: fixed;
    z-index: 50;
    width: var(--cell);
    height: var(--cell);
    margin: calc(var(--cell) / -2) 0 0 calc(var(--cell) / -2);
    pointer-events: none;
    filter: drop-shadow(0 4px 8px rgb(var(--shade-rgb) / 0.6));
  }
  .float .face {
    width: 100%;
    height: 100%;
  }

  /* 推演、推荐 */
  .foot {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 8px;
    min-height: 22px;
    font-size: var(--fs-sm);
  }
  .hint {
    color: var(--fg-4);
  }
  .sbx {
    display: flex;
    align-items: center;
    gap: 6px;
    color: var(--mint);
  }
  .sbx i {
    width: 6px;
    height: 6px;
    border-radius: 50%;
    background: var(--mint);
    box-shadow: 0 0 6px rgb(var(--mint-rgb) / 0.8);
  }
  .act {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    padding: 2px 8px;
    font-size: var(--fs-sm);
    color: var(--fg-2);
    box-shadow: inset 0 0 0 1px var(--line-strong);
  }
  .act:hover {
    color: var(--fg-0);
    box-shadow: inset 0 0 0 1px var(--line-hi);
  }
  .act.game {
    color: var(--mint-ink);
    background: var(--mint);
    box-shadow: none;
    font-weight: var(--fw-bold);
  }
  .act.game:hover:not(:disabled) {
    color: var(--mint-ink);
    background: var(--mint-hi);
    box-shadow: none;
  }
  .act.game:disabled {
    opacity: 0.4;
  }
  .rec {
    display: flex;
    flex-direction: column;
    gap: 4px;
    padding: 6px 8px;
    background: var(--well);
    box-shadow: inset 0 0 0 1px var(--line-soft);
    font-size: var(--fs-sm);
  }
  .rec.none {
    flex-direction: row;
    align-items: baseline;
    gap: 8px;
    color: var(--fg-4);
  }
  .rec.none b {
    color: var(--fg-3);
    font-weight: var(--fw-semi);
  }
  .rh {
    display: flex;
    align-items: center;
    gap: 8px;
  }
  .rh b {
    color: var(--fg-1);
  }
  .rh em {
    font-style: normal;
    color: var(--gold);
    font-size: var(--fs-xs);
  }
  .rh em.stale {
    color: var(--fg-3);
  }
  .rh em.mv {
    color: var(--mint);
  }
  .sim {
    margin: 0;
    color: var(--fg-2);
    font-size: var(--fs-xs);
  }
  .sim b {
    color: var(--fg-0);
    font-family: var(--font-term);
  }
  .sim b.bad {
    color: var(--red-fg);
  }
  .sim small {
    color: var(--fg-3);
    font-size: var(--fs-2xs);
  }
  .sim.sub {
    color: var(--fg-3);
  }
  .goal {
    margin-left: auto;
  }
  /* 朝向：格子边上的小三角，指向干员面对的方向（攻击范围那边） */
  .dir {
    position: absolute;
    z-index: 2;
    width: 0;
    height: 0;
    border: max(3px, calc(var(--cell) * 0.1)) solid transparent;
    pointer-events: none;
  }
  .dir.right {
    right: -1px;
    top: 50%;
    translate: 0 -50%;
    border-left-color: var(--mint);
  }
  .dir.left {
    left: -1px;
    top: 50%;
    translate: 0 -50%;
    border-right-color: var(--mint);
  }
  .dir.up {
    top: -1px;
    left: 50%;
    translate: -50% 0;
    border-bottom-color: var(--mint);
  }
  .dir.down {
    bottom: -1px;
    left: 50%;
    translate: -50% 0;
    border-top-color: var(--mint);
  }
  .sp {
    flex: 1;
  }
  .asm {
    margin: 0;
    color: var(--fg-3);
    font-size: var(--fs-xs);
  }

  /* 点选确认 */
  .pick {
    display: flex;
    flex-direction: column;
    gap: 8px;
    padding: 8px;
    background: linear-gradient(var(--pop-top), var(--pop-bottom));
    box-shadow:
      inset 0 0 0 1px var(--line-strong),
      inset var(--edge) 0 0 var(--mint);
  }
  .ph {
    display: flex;
    align-items: flex-start;
    gap: 8px;
  }
  .pt {
    flex: 1;
    display: flex;
    flex-direction: column;
    gap: 1px;
    font-size: var(--fs-base);
    color: var(--fg-1);
  }
  .pt small {
    font-size: var(--fs-xs);
    color: var(--fg-3);
  }
  .x {
    color: var(--fg-3);
  }
  .x:hover {
    color: var(--fg-0);
  }
  .cands {
    display: grid;
    grid-template-columns: repeat(4, 1fr);
    gap: 6px;
  }
  .cand {
    position: relative;
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 3px;
    padding: 4px 2px 3px;
    background: var(--bg-2);
    box-shadow: inset 0 0 0 1px var(--line);
    font-size: var(--fs-xs);
    color: var(--fg-2);
    min-width: 0;
  }
  .cand img {
    width: 100%;
    max-width: 52px;
    aspect-ratio: 1;
    object-fit: cover;
  }
  .cand > span {
    max-width: 100%;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .cand em {
    position: absolute;
    top: 2px;
    right: 2px;
    padding: 0 3px;
    font-style: normal;
    font-size: var(--fs-3xs);
    color: var(--mint-ink);
    background: var(--mint);
  }
  .cand.cur {
    box-shadow: inset 0 0 0 1px rgb(var(--mint-rgb) / 0.5);
  }
  .cand:hover {
    background: var(--bg-3);
    color: var(--fg-0);
    box-shadow: inset 0 0 0 1px var(--mint);
  }
  .cand.none em {
    position: static;
    background: none;
    color: var(--fg-4);
  }
  .nq {
    display: grid;
    place-items: center;
    width: 100%;
    max-width: 52px;
    aspect-ratio: 1;
    background: var(--bg-3) var(--hatch);
    color: var(--fg-3);
    font-size: var(--fs-2xl);
  }
  .sr {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 3px 8px;
    background: var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line);
    color: var(--fg-3);
  }
  .sr input {
    flex: 1;
    min-width: 0;
    background: none;
    border: 0;
    outline: 0;
    color: var(--fg-1);
    font: inherit;
    font-size: var(--fs-base);
  }
  .alllist {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(52px, 1fr));
    gap: 4px;
    max-height: 196px;
    overflow: auto;
  }
  .mini {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 2px;
    padding: 3px 1px;
    font-size: var(--fs-2xs);
    color: var(--fg-2);
    min-width: 0;
  }
  .mini img {
    width: 40px;
    height: 40px;
    object-fit: cover;
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .mini span {
    max-width: 100%;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .mini:hover,
  .mini.cur {
    background: var(--bg-3);
    color: var(--fg-0);
  }
  .back,
  .clear {
    align-self: flex-start;
    display: inline-flex;
    align-items: center;
    gap: 3px;
    font-size: var(--fs-xs);
    color: var(--fg-3);
  }
  .back:hover,
  .clear:hover {
    color: var(--fg-1);
  }
</style>
