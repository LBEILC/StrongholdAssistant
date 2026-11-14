<script lang="ts">
  import BondPick from './BondPick.svelte'
  import { sheet } from '../lib/sheet'
  import { fly, fade } from 'svelte/transition'
  import { untrack } from 'svelte'
  import type { CovenantGoal, LineupMember } from '../lib/api/types'
  import { stageLabel, MAX_ROUND } from '../lib/stages'
  import { defaultsFor, effective, customSkill, setChoice, setMyDefault } from '../lib/opconfig'
  import { app, goalProgress, otherBonds, rankForGoals, lineupEquipment } from '../lib/state.svelte'
  import { asset } from '../lib/assets'
  import BondIcon from './BondIcon.svelte'
  import OpTile from './OpTile.svelte'
  import EqTile from './EqTile.svelte'
  import Seg from './Seg.svelte'
  import StrategyGrid from './StrategyGrid.svelte'
  import Icon from './Icon.svelte'
  import { openMenu, type MenuEntry } from '../lib/menu.svelte'
  import { tick } from 'svelte'

  // 阵容编辑器：几乎全高的面板。上半是阵容本身（名字、目标盟约、想抓的干员），下半是挑干员。
  // 阵容不限人数；选了目标盟约后干员池默认只列这些盟约的干员，覆盖目标越多越靠前。
  const e = $derived(app.editing)
  let q = $state('')
  let bond = $state<string | null>(null)
  // 目标盟约：本局阵容不完整的核心盟约不能选（已选的还能取消）；只有一档的盟约（调和、独行等）选上就只亮起，不标档位
  const goalState = (id: string) => {
    const c = app.cov(id)
    const lv = levelOf(id)
    const blocked = c.kind === 'core' && !!app.matchCovenant(id)?.incomplete
    return { on: !!lv, badge: lv && c.thresholds.length >= 2 ? (lv === 2 ? 'II' : 'I') : undefined, dim: blocked, disabled: blocked && !lv }
  }
  let goalOnly = $state(true)
  // 下半挑选区：干员 / 装备
  let poolKind = $state<'op' | 'eq' | 'st'>('op')
  let pheadH = $state(0)
  const strat = $derived(e?.strategyId ? app.catalog.strategies.find((s) => s.id === e.strategyId) : null)
  const pickSt = (id: string) => e && (e.strategyId = e.strategyId === id ? null : id)
  let cat = $state<string | null>(null)
  const cats = [...new Set(app.catalog.equipment.flatMap((x) => x.categories))]
  // 点已选的干员展开他的装备面板；展开时下面「装备」里点的装备进他的候选，否则进通用装备
  let sel = $state<string | null>(null)
  const selM = $derived(sel ? e?.members.find((m) => m.name === sel) : undefined)
  const selOp = $derived(sel ? app.op(sel) : undefined)
  const pickMember = (n: string) => {
    sel = sel === n ? null : n
    if (sel) poolKind = 'eq'
  }
  const memberHas = (n: string) => !!selM?.equipment?.some((x) => x.name === n)
  const setMemberEq = (f: (l: { name: string; priority: boolean }[]) => { name: string; priority: boolean }[]) => {
    if (!e || !sel) return
    e.members = e.members.map((m) => (m.name === sel ? { ...m, equipment: f(m.equipment ?? []) } : m))
  }
  // 精锐、技能、模组（v0.7.0）：改的是这套阵容（这个阶段）里的这名干员；和默认一样就不写
  const my = $derived(app.settings.operatorDefaults)
  const setMember = (m: LineupMember) => e && (e.members = e.members.map((x) => (x.name === m.name ? m : x)))
  function toggleElite() {
    const { elite, ...rest } = selM!
    setMember(elite ? rest : { ...rest, elite: true })
  }
  /** 把这名干员现在的技能、模组设为我的默认；这套阵容里就不用再单独写了 */
  function saveMyDefault() {
    app.settings.operatorDefaults = setMyDefault(my, selOp!, effective(selM!, selOp!, my))
    app.saveSettings()
    app.showToast(`以后加「${selOp!.name}」默认用这套技能和模组`)
  }
  const togglePriority = (n: string) => setMemberEq((l) => l.map((x) => (x.name === n ? { ...x, priority: !x.priority } : x)))
  const removeMemberEq = (n: string) => setMemberEq((l) => l.filter((x) => x.name !== n))
  const priCount = (m: { equipment?: { priority: boolean }[] }) => m.equipment?.filter((x) => x.priority).length ?? 0
  const slotText = (o: { equipSlots: { normal: number; elite: number } }) =>
    o.equipSlots.elite === o.equipSlots.normal ? `装备栏 ${o.equipSlots.normal}` : `装备栏 ${o.equipSlots.normal} · 精锐 ${o.equipSlots.elite}`
  const hasEq = (n: string) => (sel ? memberHas(n) : !!e?.equipment.includes(n))
  const addEq = (n: string) => {
    if (!e) return
    if (sel) return setMemberEq((l) => (l.some((x) => x.name === n) ? l.filter((x) => x.name !== n) : [...l, { name: n, priority: false }]))
    e.equipment = e.equipment.includes(n) ? e.equipment.filter((x) => x !== n) : [...e.equipment, n]
  }
  const eqPool = $derived(
    app.catalog.equipment.filter((x) => (!q || x.name.includes(q)) && (!cat || x.categories.includes(cat))).sort((a, b) => b.tier - a.tier),
  )
  const empty = $derived(!e || (!e.members.length && !e.goals.length && !lineupEquipment(e).length))

  const inLineup = (n: string) => !!e?.members.some((m) => m.name === n)
  const add = (n: string) => {
    if (!e) return
    if (inLineup(n) && sel === n) sel = null
    e.members = inLineup(n) ? e.members.filter((m) => m.name !== n) : [...e.members, { name: n }]
  }
  const toggleKey = (n: string) => {
    if (!e) return
    e.members = e.members.map((m) => (m.name === n ? { ...m, key: !m.key } : m))
  }
  const levelOf = (id: string) => e?.goals.find((g) => g.id === id)?.level
  const useGoals = $derived(!!e?.goals.length && goalOnly)
  const pool = $derived.by(() => {
    if (!e) return []
    const base = useGoals ? rankForGoals(e.goals, app.catalog.operators) : [...app.catalog.operators].sort((a, b) => b.tier - a.tier)
    return base.filter((o) => (!q || o.name.includes(q)) && (!bond || o.core.includes(bond) || o.extra.includes(bond)))
  })
  const progress = $derived(e ? goalProgress(e.goals, e.members, e.members.map((m) => m.name)) : [])
  const others = $derived(e ? otherBonds(e.goals, e.members) : [])
  let confirmDelete = $state(false)
  // 名字可以不填：默认用前三个目标盟约起名，保存时补上
  const autoName = $derived(
    e?.goals
      .slice(0, 3)
      .map((g) => app.cov(g.id).name + (g.level === 2 ? ' II' : ''))
      .join(' · ') || '未命名阵容',
  )
  // 保存失败（宿主回错）时把原因显示在底栏，编辑内容保留，可以直接再点保存
  let saving = $state(false)
  let err = $state('')
  // 阶段（v0.7.0）：顶层字段始终是「正在编辑的那个阶段」，下面的盟约、干员、装备代码都不用改。
  // cur = -1 表示成型阵容在顶层；切到回合阶段时成型阵容先放进 finalBuf，切回来再放回去
  type Part = { goals: CovenantGoal[]; members: LineupMember[]; equipment: string[] }
  let cur = $state(-1)
  let finalBuf = $state.raw<Part | null>(null)
  // 加回合阶段：依次点起始、结束回合（可以相同），照森空岛
  let addingStage = $state(false)
  let pickFrom = $state<number | null>(null)
  // 改已有阶段的回合范围：用同一个回合选择器重选起止回合，阶段内容不动
  let rangeFor = $state<number | null>(null)
  $effect.pre(() => {
    app.editing
    untrack(() => ((cur = -1), (finalBuf = null), (addingStage = false), (rangeFor = null)))
  })
  function swapTo(i: number) {
    if (!e || i === cur) return
    const top: Part = { goals: e.goals, members: e.members, equipment: e.equipment }
    if (cur === -1) finalBuf = top
    else e.stages![cur] = { ...e.stages![cur], ...top }
    const next = i === -1 ? finalBuf! : e.stages![i]
    e.goals = next.goals
    e.members = next.members
    e.equipment = next.equipment
    if (i === -1) finalBuf = null
    cur = i
    sel = null
  }
  // 每个回合都能单独做一个阶段（照森空岛），回合不能重叠：已经被别的阶段占用的回合点不了（改范围时不算它自己）
  const covered = (r: number) => !!e?.stages?.some((x, i) => i !== rangeFor && r >= x.fromRound && r <= x.toRound)
  function pickRound(r: number) {
    if (pickFrom === null) return void (pickFrom = r)
    const [from, to] = pickFrom <= r ? [pickFrom, r] : [r, pickFrom]
    pickFrom = null
    for (let k = from; k <= to; k++) if (covered(k)) return
    if (rangeFor !== null) {
      // 当前阶段的内容在顶层，stages 里这一项只改回合；重新排序后 cur 跟着它走
      const moved = { ...e!.stages![rangeFor], fromRound: from, toRound: to }
      const stages = e!.stages!.map((x, i) => (i === rangeFor ? moved : x)).sort((a, b) => a.fromRound - b.fromRound)
      e!.stages = stages
      cur = stages.indexOf(moved)
      rangeFor = null
      return
    }
    swapTo(-1)
    // 新阶段先从它前面最近的回合阶段复制一份，接着改最方便；前面没有就空着
    const prev = (e!.stages ?? []).filter((x) => x.toRound < from).at(-1)
    const seed = prev ? structuredClone($state.snapshot(prev)) : null
    const stages = [...(e!.stages ?? []), { name: '', fromRound: from, toRound: to, goals: seed?.goals ?? [], members: seed?.members ?? [], equipment: seed?.equipment ?? [] }]
    stages.sort((a, b) => a.fromRound - b.fromRound)
    e!.stages = stages
    addingStage = false
    swapTo(stages.findIndex((x) => x.fromRound === from))
  }
  function removeStage(i: number) {
    swapTo(-1)
    e!.stages = e!.stages!.filter((_, j) => j !== i)
  }
  // 从别的阶段复制（整段覆盖当前阶段的盟约、干员、装备）；当前阶段有内容时要点两次
  let confirmCopy = $state<number | null>(null)
  const partOf = (i: number): Part => (i === cur ? { goals: e!.goals, members: e!.members, equipment: e!.equipment } : i === -1 ? finalBuf! : e!.stages![i])
  const curEmpty = $derived(!e?.members.length && !e?.goals.length && !e?.equipment.length)
  function copyFrom(i: number) {
    if (!curEmpty && confirmCopy !== i) return void (confirmCopy = i)
    overwriteFrom(i)
  }
  function overwriteFrom(i: number) {
    const src = structuredClone($state.snapshot(partOf(i)))
    e!.goals = src.goals
    e!.members = src.members
    e!.equipment = src.equipment
    confirmCopy = null
    sel = null
  }

  // 右键菜单（编辑器里自己的，不弹全局的干员菜单）：已选干员、阶段页签
  function memberMenu(ev: MouseEvent, n: string) {
    const m = e!.members.find((x) => x.name === n)!
    openMenu(ev, {
      title: n,
      sub: '这套阵容里',
      items: [
        { label: '关键干员', icon: 'star', on: !!m.key, run: () => toggleKey(n) },
        { label: '候选装备、技能、模组…', icon: 'pencil', run: () => sel !== n && pickMember(n) },
        { label: '查看详情', icon: 'arrowRight', run: () => (app.detail = { kind: 'operator', name: n }) },
        'sep',
        { label: '从阵容里移出', icon: 'close', tone: 'danger', run: () => add(n) },
      ],
    })
  }
  // i = -1 是成型阵容
  function stageMenu(ev: MouseEvent, i: number) {
    const parts = [...(e!.stages ?? []).map((x, j) => ({ j, label: stageLabel(x) })), { j: -1, label: '成型阵容' }].filter((x) => x.j !== i)
    const items: MenuEntry[] = []
    if (i >= 0)
      items.push(
        { label: '改回合', icon: 'pencil', run: () => (swapTo(i), (rangeFor = i), (pickFrom = null), (addingStage = false)) },
        { label: '改名', icon: 'pencil', run: async () => (swapTo(i), await tick(), document.querySelector<HTMLInputElement>('.ed .sname')?.focus()) },
        'sep',
      )
    items.push(...parts.map((x): MenuEntry => ({ label: `用「${x.label}」覆盖`, icon: 'copy', confirm: '再点一次覆盖', run: () => (swapTo(i), overwriteFrom(x.j)) })))
    if (i >= 0) items.push('sep', { label: '删除这个阶段', icon: 'trash', tone: 'danger', confirm: '再点一次删除', run: () => removeStage(i) })
    openMenu(ev, { title: i >= 0 ? stageLabel(e!.stages![i]) : '成型阵容', sub: '阶段', items })
  }

  const save = async () => {
    swapTo(-1)
    if (!e!.stages?.length) delete e!.stages
    if (!e!.name.trim()) e!.name = autoName
    saving = true
    err = ''
    try {
      await app.saveEditing()
    } catch (x) {
      err = `没保存上：${(x as Error).message}`
    } finally {
      saving = false
    }
  }
</script>

{#if e}
  <div class="scrim" transition:fade={{ duration: 160 }}></div>
  <aside class="ed win" transition:sheet={{ y: 400, duration: 260 }}>
    <header>
      <input class="nm" bind:value={e.name} placeholder="名称（不填就叫「{autoName}」）" />
      <button class="x" onclick={() => (app.editing = null)} aria-label="关闭"><Icon name="close" size={16} /></button>
    </header>

    <!-- 阶段页签：回合阶段按先后在前，成型阵容在最后；每个回合都可以单独一个阶段（照森空岛） -->
    <div class="stages">
      {#each e.stages ?? [] as s, i (i)}
        <span class="stab" class:on={cur === i}>
          <button onclick={() => swapTo(i)} oncontextmenu={(ev) => stageMenu(ev, i)}>{stageLabel(s)}</button>
          <button class="sx" aria-label="删除这个阶段" title="删除这个阶段" onclick={() => removeStage(i)}><Icon name="close" size={11} /></button>
        </span>
      {/each}
      <span class="stab final" class:on={cur === -1}><button onclick={() => swapTo(-1)} oncontextmenu={(ev) => e?.stages?.length && stageMenu(ev, -1)}>成型阵容</button></span>
      <button class="sadd" class:on={addingStage} title="按回合加一个阶段，每个回合都可以单独一个" onclick={() => ((addingStage = !addingStage), (pickFrom = null), (rangeFor = null))}><Icon name="add" size={14} />回合阶段</button>
    </div>
    {#if addingStage || rangeFor !== null}
      <div class="rpick" transition:fly={{ y: -6, duration: 160 }}>
        <p>
          {pickFrom === null ? (rangeFor !== null ? '改成从第几回合开始？' : '点起始回合') : `从第 ${pickFrom} 回合到第几回合？（可以相同）`}
          {#if rangeFor !== null}<button class="link" onclick={() => ((rangeFor = null), (pickFrom = null))}>取消</button>{/if}
        </p>
        <div class="rounds">
          {#each Array.from({ length: MAX_ROUND }, (_, k) => k + 1) as r (r)}
            <button class:on={pickFrom === r} disabled={covered(r)} title={covered(r) ? '这个回合已经在别的阶段里' : undefined} onclick={() => pickRound(r)}>{r}</button>
          {/each}
        </div>
      </div>
    {:else}
      {#if cur >= 0 && e.stages}
        {@const st = e.stages[cur]}
        <div class="sbar">
          <button class="srange" title="改这个阶段的回合" onclick={() => ((rangeFor = cur), (pickFrom = null), (addingStage = false))}>
            <span class="term">{st.fromRound === st.toRound ? `第 ${st.fromRound}` : `${st.fromRound}～${st.toRound}`}</span> 回合<Icon name="pencil" size={12} />
          </button>
          <input class="sname" bind:value={st.name} placeholder="阶段名（不填就按回合叫）" maxlength="12" />
        </div>
      {/if}
      {#if e.stages?.length}
        <div class="copyrow">
          <span>复制自</span>
          {#each [...e.stages.map((x, i) => ({ i, label: stageLabel(x) })), { i: -1, label: '成型阵容' }].filter((x) => x.i !== cur) as x (x.i)}
            <button class:armed={confirmCopy === x.i} onclick={() => copyFrom(x.i)}>{confirmCopy === x.i ? `再点一次，用「${x.label}」覆盖` : x.label}</button>
          {/each}
        </div>
      {/if}
    {/if}

    <!-- 上半区（目标盟约、已选）和下半区（挑选）一起滚动：已选的多了也不会把挑选区挤没 -->
    <div class="body">
    <div class="secs">
    <div class="sec">
      <div class="lbl">目标盟约<em>点一下选上，有 II 档的再点升 II 档，再点取消；本局阵容不完整的核心盟约不能选</em></div>
      <BondPick state={goalState} onpick={(id) => app.cycleGoal(id)} />
      <div class="bonds">
        {#each progress as g (g.covenant.id)}
          <span class="bc" class:short={!g.done}><BondIcon id={g.covenant.id} size={18} on />{g.covenant.name}<b>{g.have}/{g.need}</b></span>
        {:else}
          <span class="muted">还没有目标盟约</span>
        {/each}
      </div>
      {#if others.length}
        <!-- 名单顺带凑出的其他盟约，暗的是还没激活 -->
        <div class="bonds others">
          <span class="muted">其他</span>
          {#each others as g (g.covenant.id)}
            <span class="bc" class:off={!g.active}><BondIcon id={g.covenant.id} size={18} on={g.active} />{g.covenant.name}<b>{g.have}/{g.need}</b></span>
          {/each}
        </div>
      {/if}
    </div>

    <div class="sec">
      <div class="lbl">想抓的干员 <b class="term">{e.members.length}</b><em>不限人数；点已选的头像设「关键」和候选装备</em></div>
      <div class="chosen">
        {#each e.members as m (m.name)}
          <span class="mt" class:sel={sel === m.name}>
            <OpTile name={m.name} transform={m.transform} isKey={m.key} elite={!!m.elite} skill={customSkill(m, app.op(m.name), my)} size={34} showName={false} oncontext={(ev) => memberMenu(ev, m.name)} onclick={() => pickMember(m.name)} />
            {#if priCount(m)}<b class="pc" title="{priCount(m)} 件优先装备">{priCount(m)}</b>{:else if m.equipment?.length}<b class="pc soft" title="{m.equipment.length} 件候选装备">{m.equipment.length}</b>{/if}
          </span>
        {:else}
          <span class="muted">在下面点头像加入</span>
        {/each}
      </div>
      {#if selM && selOp}
        {@const pri = priCount(selM)}
        {@const d = defaultsFor(selOp, my)}
        {@const ef = effective(selM, selOp, my)}
        <div class="mp" transition:fly={{ y: -6, duration: 160 }}>
          <div class="mph">
            <b>{selM.name}</b>
            <span class="slots">{slotText(selOp)}</span>
            <span class="spacer"></span>
            <button class="kt" class:on={selM.key} onclick={() => toggleKey(selM.name)}>{selM.key ? '★ 关键' : '☆ 设为关键'}</button>
            <button class="x2" aria-label="收起" onclick={() => (sel = null)}><Icon name="close" size={13} /></button>
          </div>
          <!-- 精锐 · 技能 · 模组：默认就够用，只改想改的；带「默认」角标的是不写时用的那个 -->
          <div class="cfg">
            <button class="elt" class:on={selM.elite} onclick={toggleElite} title="这名干员要凑到精锐">精锐</button>
            {#if selOp.skills.length > 1}
              <span class="cl">技能</span>
              {#each selOp.skills as k (k.id)}
                <button class="opt" class:on={ef.skill === k.id} title={k.name} onclick={() => setMember(setChoice(selM, selOp, my, 'skill', k.id))}>
                  <img src={asset.skill(k.id)} alt={k.name} />{#if k.id === d.skill}<em>默认</em>{/if}
                </button>
              {/each}
            {/if}
            {#if selOp.modules.length}
              <span class="cl">模组</span>
              <button class="opt txt" class:on={ef.module === ''} onclick={() => setMember(setChoice(selM, selOp, my, 'module', ''))}>不带{#if d.module === ''}<em>默认</em>{/if}</button>
              {#each selOp.modules as k (k.id)}
                <button class="opt" class:on={ef.module === k.id} title={k.name} onclick={() => setMember(setChoice(selM, selOp, my, 'module', k.id))}>
                  <img src={asset.module(k.id)} alt={k.name} />{#if k.id === d.module}<em>默认</em>{/if}
                </button>
              {/each}
            {/if}
          </div>
          <p class="cfgn">
            {selOp.skills.find((k) => k.id === ef.skill)?.name ?? ''}{selOp.modules.length ? ` · ${selOp.modules.find((k) => k.id === ef.module)?.name ?? '不带模组'}` : ''}
            {#if selM.skill !== undefined || selM.module !== undefined}<button class="link" onclick={saveMyDefault}>设为我的默认</button>{/if}
          </p>
          <div class="ces">
            {#each selM.equipment ?? [] as x (x.name)}
              <span class="ce" class:pri={x.priority}>
                <button class="cb" onclick={() => togglePriority(x.name)} title={x.priority ? '点一下改成候选' : '点一下改成优先'}>
                  <img src={asset.equipment(x.name)} alt="" />{x.name}<em>{x.priority ? '优先' : '候选'}</em>
                </button>
                <button class="cx" aria-label="移除{x.name}" onclick={() => removeMemberEq(x.name)}><Icon name="close" size={11} /></button>
              </span>
            {:else}
              <span class="muted">还没有候选装备，在下面「装备」里点选</span>
            {/each}
          </div>
          {#if pri > selOp.equipSlots.elite}<p class="over">优先 {pri} 件，超过装备栏 {selOp.equipSlots.elite} 格</p>{/if}
          {#if selOp.givesEquipment?.length}
            <div class="gives">提供装备：{#each selOp.givesEquipment as n (n)}<button class:on={memberHas(n)} onclick={() => addEq(n)}>{n}{memberHas(n) ? ' ✓' : ' +'}</button>{/each}</div>
          {/if}
          <p class="mhint">点装备切换「优先 / 候选」：优先的在商店里按关键标记（金色括号）。</p>
        </div>
      {/if}
      {#if strat}
        <div class="lbl">策略<em>点一下取消</em></div>
        <button class="stchip" onclick={() => pickSt(strat.id)}>
          <img src={asset.strategy(strat.id)} alt="" />
          <b>{strat.name}</b><span>{strat.initiator}</span>
          <Icon name="close" size={13} />
        </button>
      {/if}
      <div class="lbl">简介 <b class="term">{[...(e.description ?? '')].length}/500</b><em>可以不写；发布到广场时默认带上</em></div>
      <textarea class="desc" rows="2" maxlength="500" placeholder="这套阵容的思路、运营要点、适合打哪个首领……" value={e.description ?? ''} oninput={(ev) => (e.description = ev.currentTarget.value || undefined)}></textarea>
      {#if e.equipment.length}
        <div class="lbl">通用装备 <b class="term">{e.equipment.length}</b><em>不指定干员；点一下移出</em></div>
        <div class="chosen">
          {#each e.equipment as n (n)}
            <EqTile name={n} size={34} showName={false} menu={false} onclick={() => addEq(n)} />
          {/each}
        </div>
      {/if}
    </div>

    </div>
    <div class="pick">
      <div class="phead" bind:clientHeight={pheadH}>
      <Seg
        items={[
          { id: 'op', label: '干员', count: e.members.length },
          { id: 'eq', label: '装备', count: lineupEquipment(e).length },
          { id: 'st', label: '策略', count: e.strategyId ? 1 : 0 },
        ]}
        value={poolKind}
        onchange={(v) => (poolKind = v)}
        height={24}
      />
      <div class="search">
        <Icon name="search" size={14} />
        <input bind:value={q} placeholder={poolKind === 'op' ? '搜索干员' : poolKind === 'eq' ? '搜索装备' : '搜索策略、发起者或效果'} />
        {#if poolKind === 'op' && e.goals.length}
          <button class="tog" class:on={goalOnly} onclick={() => (goalOnly = !goalOnly)}>只看目标盟约</button>
        {/if}
      </div>
      {#if poolKind === 'op'}
        <BondPick state={(id) => ({ on: bond === id })} onpick={(id) => (bond = bond === id ? null : id)} />
      {:else if poolKind === 'eq'}
        <div class="for" class:member={!!sel}>
          {#if sel}给 <b>{sel}</b> 选候选装备<button onclick={() => (sel = null)}>改加通用装备</button>{:else}点装备加入通用装备；要给某名干员配，先点上面他的头像{/if}
        </div>
        <div class="cats">
          {#each cats as c (c)}
            <button class="chip" class:on={cat === c} onclick={() => (cat = cat === c ? null : c)}>{c}</button>
          {/each}
        </div>
      {/if}
      </div>
      {#if poolKind === 'op'}
        <div class="pool">
          {#each pool as o (o.name)}
            <span class="pi"><OpTile name={o.name} size={44} owned={inLineup(o.name)} menu={false} onclick={() => add(o.name)} /></span>
          {/each}
        </div>
      {:else if poolKind === 'st'}
        <div class="stwrap" style:--sticky-top="{pheadH}px"><StrategyGrid selected={e.strategyId} {q} onpick={pickSt} current={app.match.strategyId} /></div>
      {:else}
        <div class="pool">
          {#each eqPool as x (x.name)}
            <EqTile name={x.name} size={44} picked={hasEq(x.name)} menu={false} onclick={() => addEq(x.name)} />
          {/each}
        </div>
      {/if}
    </div>
    </div>

    {#if err}<p class="err">{err}</p>{/if}
    <footer>
      {#if e.id}
        {#if confirmDelete}
          <button class="danger" onclick={() => app.deleteLineup(e.id)}>确认删除</button>
          <button class="ghost" onclick={() => (confirmDelete = false)}>不删</button>
        {:else}
          <button class="ghost" onclick={() => (confirmDelete = true)}><Icon name="trash" size={14} />删除</button>
        {/if}
      {/if}
      <span class="spacer"></span>
      <button class="ghost" onclick={() => (app.editing = null)}>取消</button>
      <button class="primary" disabled={empty || saving} title={empty ? '先选目标盟约、干员或装备' : ''} onclick={save}>{saving ? '保存中…' : '保存'}</button>
    </footer>
  </aside>
{/if}

<style>
  .desc {
    width: 100%;
    /* 随内容长高，不要原生的拖拽手柄 */
    resize: none;
    field-sizing: content;
    min-height: 44px;
    max-height: 168px;
    padding: 6px 8px;
    font: inherit;
    font-size: var(--fs-base);
    line-height: 1.6;
    color: var(--fg-1);
    background: var(--bg-1);
    border: 0;
    outline: 0;
    box-shadow: inset 0 0 0 1px var(--line);
    user-select: text;
  }
  .desc:focus {
    box-shadow: inset 0 0 0 1px var(--mint-lo);
  }
  .desc::placeholder {
    color: var(--fg-4);
  }
  .mt {
    position: relative;
    display: inline-flex;
  }
  .mt.sel {
    outline: 2px solid var(--mint);
    outline-offset: 1px;
  }
  .pc {
    position: absolute;
    right: -4px;
    bottom: -4px;
    min-width: 15px;
    height: 15px;
    padding: 0 3px;
    font-size: var(--fs-xs);
    line-height: 15px;
    text-align: center;
    color: var(--gold-ink);
    background: var(--gold);
    pointer-events: none;
  }
  .pc.soft {
    color: var(--fg-1);
    background: var(--bg-3);
  }
  .mp {
    margin-top: 8px;
    padding: 8px;
    background: rgb(var(--shade-rgb) / 0.3);
    box-shadow: inset 2px 0 0 var(--mint);
  }
  .mph {
    display: flex;
    align-items: center;
    gap: 8px;
    font-size: var(--fs-base);
  }
  .mph b {
    font-size: var(--fs-md);
    color: var(--fg-1);
  }
  .slots {
    font-size: var(--fs-sm);
    color: var(--fg-3);
  }
  .mph .spacer {
    flex: 1;
  }
  .kt {
    height: 22px;
    padding: 0 8px;
    font-size: var(--fs-sm);
    color: var(--fg-2);
    background: var(--bg-2);
  }
  .kt.on {
    color: var(--gold-ink);
    background: var(--gold);
    font-weight: var(--fw-bold);
  }
  .x2 {
    width: 22px;
    height: 22px;
    display: grid;
    place-items: center;
    color: var(--fg-3);
  }
  .ces {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
    margin-top: 8px;
  }
  .ce {
    display: inline-flex;
    align-items: stretch;
    background: var(--bg-2);
    box-shadow: inset 0 -2px 0 var(--fg-4);
  }
  .ce.pri {
    background: rgb(var(--gold-rgb) / 0.12);
    box-shadow: inset 0 -2px 0 var(--gold);
  }
  .cb {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    height: 26px;
    padding: 0 6px 0 3px;
    font-size: var(--fs-base);
    color: var(--fg-1);
  }
  .cb img {
    width: 20px;
    height: 20px;
    object-fit: contain;
  }
  .cb em {
    font-style: normal;
    font-size: var(--fs-xs);
    padding: 0 4px;
    color: var(--fg-3);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .ce.pri .cb em {
    color: var(--gold-ink);
    background: var(--gold);
    box-shadow: none;
    font-weight: var(--fw-bold);
  }
  .cx {
    width: 20px;
    display: grid;
    place-items: center;
    color: var(--fg-4);
  }
  .cx:hover {
    color: var(--fg-1);
  }
  .over {
    margin: 6px 0 0;
    font-size: var(--fs-sm);
    color: var(--gold);
  }
  .gives {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 4px;
    margin-top: 6px;
    font-size: var(--fs-sm);
    color: var(--fg-3);
  }
  .gives button {
    height: 20px;
    padding: 0 6px;
    font-size: var(--fs-sm);
    color: var(--mint);
    box-shadow: inset 0 0 0 1px var(--mint);
  }
  .gives button.on {
    color: var(--fg-3);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .mhint {
    margin: 6px 0 0;
    font-size: var(--fs-sm);
    color: var(--fg-4);
  }
  .for {
    font-size: var(--fs-sm);
    color: var(--fg-4);
    margin: 4px 0;
  }
  .for.member {
    color: var(--mint);
  }
  .for b {
    color: var(--fg-1);
  }
  .for button {
    margin-left: 8px;
    font-size: var(--fs-sm);
    color: var(--fg-3);
    text-decoration: underline;
  }
  .scrim {
    position: absolute;
    inset: 0;
    background: var(--scrim-strong);
    z-index: 20;
  }
  .ed {
    position: absolute;
    left: 0;
    right: 0;
    bottom: 0;
    top: 24px;
    z-index: 21;
    display: flex;
    flex-direction: column;
    background:
      var(--noise),
      linear-gradient(180deg, var(--sheet-top), var(--sheet-bottom) 160px);
    box-shadow:
      0 -1px 0 var(--line),
      0 -12px 30px rgb(var(--shade-rgb) / 0.6);
  }
  .ed::before {
    content: '';
    position: absolute;
    top: 0;
    left: 12px;
    width: 64px;
    height: 2px;
    background: var(--mint);
    box-shadow: var(--mint-glow);
  }
  header {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 12px 8px 8px 12px;
  }
  .nm {
    flex: 1;
    min-width: 0;
    height: 32px;
    padding: 0 8px;
    font-size: var(--fs-xl);
    font-weight: var(--fw-bold);
    background: var(--bg-1);
    border: 0;
    outline: 0;
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .nm:focus {
    box-shadow: inset 0 0 0 1px var(--mint-lo);
  }
  .nm::placeholder {
    color: var(--fg-4);
    font-weight: var(--fw-base);
  }
  .x {
    width: 30px;
    height: 30px;
    display: grid;
    place-items: center;
    color: var(--fg-3);
  }
  .x:hover {
    color: var(--fg-1);
    background: var(--bg-4);
  }
  .sec {
    display: flex;
    flex-direction: column;
    gap: 6px;
    padding: 0 12px 10px;
    border-bottom: 1px solid var(--line-soft);
  }
  .sec + .sec {
    padding-top: 8px;
  }
  .lbl {
    display: flex;
    flex-wrap: wrap;
    align-items: baseline;
    gap: 2px 6px;
    font-size: var(--fs-base);
    font-weight: var(--fw-bold);
    color: var(--fg-2);
  }
  .lbl b {
    color: var(--mint);
  }
  .lbl em {
    font-style: normal;
    font-weight: var(--fw-base);
    font-size: var(--fs-sm);
    color: var(--fg-4);
  }
  .bonds {
    display: flex;
    flex-wrap: wrap;
    gap: 4px 10px;
    min-height: 20px;
  }
  .bc.short b {
    color: var(--gold);
  }
  .others {
    margin-top: 4px;
  }
  .bc.off {
    color: var(--fg-4);
  }
  .bc.off b {
    color: var(--fg-3);
  }
  /* 已选的不再单独滚动，跟着编辑器整体滚：避免滚轮落在这里时外层滚不动 */
  .chosen {
    display: flex;
    flex-wrap: wrap;
    gap: 3px;
  }
  .tog {
    flex: none;
    font-size: var(--fs-sm);
    padding: 2px 6px;
    color: var(--fg-3);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .tog.on {
    color: var(--mint-ink);
    background: var(--mint);
    box-shadow: none;
  }
  .bc {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    font-size: var(--fs-sm);
    color: var(--fg-2);
  }
  .bc b {
    font-family: var(--font-term);
    margin-left: 2px;
    color: var(--mint);
  }
  .muted {
    font-size: var(--fs-sm);
    color: var(--fg-4);
  }
  .body {
    flex: 1;
    min-height: 0;
    overflow-y: auto;
  }
  /* 宽（工作台）：左边阵容内容、右边挑选区，各自滚动 */
  .ed {
    container-type: inline-size;
  }
  @container (min-width: 700px) {
    .body {
      display: grid;
      grid-template-columns: minmax(0, 1fr) minmax(0, 1.1fr);
      overflow: hidden;
    }
    .secs {
      min-height: 0;
      overflow-y: auto;
      border-right: 1px solid var(--line);
    }
    .pick {
      min-height: 0;
      overflow-y: auto;
    }
  }
  .pick {
    display: flex;
    flex-direction: column;
    gap: 6px;
    padding: 0 12px;
    /* 挑选区至少占面板大半，已选的再多，往下滚也有完整的挑选区 */
    min-height: 70%;
  }
  /* 分段、搜索、筛选吸在滚动区顶部 */
  .phead {
    position: sticky;
    top: 0;
    z-index: 2;
    display: flex;
    flex-direction: column;
    gap: 6px;
    margin: 0 -12px;
    padding: 10px 12px 6px;
    background: var(--bg-1a);
    box-shadow: 0 1px 0 var(--line-soft);
  }
  .stwrap {
    padding: 4px 0 12px;
  }
  .search {
    display: flex;
    align-items: center;
    gap: 6px;
    height: 28px;
    padding: 0 8px;
    color: var(--fg-3);
    background: var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .search input {
    flex: 1;
    min-width: 0;
    background: none;
    border: 0;
    outline: 0;
    font-size: var(--fs-base);
    color: var(--fg-1);
  }
  .stchip {
    align-self: flex-start;
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 3px 8px 3px 3px;
    font-size: var(--fs-base);
    color: var(--fg-3);
    background: var(--bg-2);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .stchip:hover {
    color: var(--fg-1);
  }
  .stchip img {
    width: 26px;
    height: 26px;
    object-fit: cover;
    flex: none;
  }
  .stchip b {
    color: var(--fg-1);
  }
  .cats {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
  }
  .chip {
    font-size: var(--fs-base);
    padding: 2px 7px;
    color: var(--fg-2);
    background: var(--bg-2);
    box-shadow: inset 0 0 0 1px var(--line-soft);
  }
  .chip.on {
    color: var(--mint-ink);
    background: var(--mint);
    box-shadow: none;
  }
  .pool {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(52px, 1fr));
    gap: 6px 2px;
    justify-items: center;
    align-content: start;
    padding: 4px 0 12px;
  }
  .err {
    margin: 0;
    padding: 7px 12px;
    font-size: var(--fs-base);
    line-height: 1.5;
    color: var(--red-fg);
    background: var(--red-tint);
    box-shadow: inset 2px 0 0 var(--red);
  }
  footer {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 10px 12px;
    border-top: 1px solid var(--line);
    background: var(--bg-1);
  }
  .spacer {
    flex: 1;
  }
  .ghost {
    display: flex;
    align-items: center;
    gap: 3px;
    font-size: var(--fs-base);
    color: var(--fg-3);
    padding: 5px 8px;
  }
  .ghost:hover {
    color: var(--fg-1);
  }
  .danger {
    font-size: var(--fs-base);
    padding: 5px 10px;
    color: var(--fg-0);
    background: var(--red);
  }
  .primary {
    height: 30px;
    padding: 0 18px;
    font-size: var(--fs-md);
    font-weight: var(--fw-bold);
    color: var(--mint-ink);
    background: var(--mint);
  }
  .primary:disabled {
    color: var(--fg-4);
    background: var(--bg-3);
    cursor: default;
  }
  .stages {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
    padding: 6px 12px 0;
  }
  .stab {
    display: inline-flex;
    align-items: center;
    height: 26px;
    background: var(--bg-2);
    box-shadow: inset 0 0 0 1px var(--line);
    font-size: var(--fs-base);
    color: var(--fg-2);
  }
  .stab > button:first-child {
    height: 100%;
    padding: 0 10px;
  }
  .stab.on {
    color: var(--fg-1);
    background: var(--mint-tint);
    box-shadow: inset 0 0 0 1px var(--mint);
  }
  .sx {
    display: grid;
    place-items: center;
    width: 20px;
    height: 100%;
    color: var(--fg-4);
  }
  .sx:hover {
    color: var(--fg-1);
  }
  .sadd {
    display: inline-flex;
    align-items: center;
    gap: 3px;
    height: 26px;
    padding: 0 8px;
    font-size: var(--fs-base);
    color: var(--mint);
  }
  .sadd.on,
  .sadd:hover {
    background: var(--bg-2);
  }
  .rpick {
    margin: 6px 12px 0;
    padding: 8px;
    background: var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .rpick p {
    margin: 0 0 6px;
    font-size: var(--fs-base);
    color: var(--fg-3);
  }
  .rounds {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(34px, 1fr));
    gap: 4px;
  }
  .rounds button {
    height: 28px;
    font-family: var(--font-term);
    font-size: var(--fs-md);
    background: var(--bg-3);
  }
  .rounds button:hover {
    background: var(--bg-4);
  }
  .rounds button.on {
    color: var(--mint-ink);
    background: var(--mint);
  }
  .sbar {
    display: flex;
    gap: 8px;
    align-items: center;
    padding: 6px 12px 0;
  }
  .srange {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    flex: none;
    height: 26px;
    padding: 0 8px;
    font-size: var(--fs-base);
    color: var(--fg-2);
    background: var(--bg-1);
    border: 1px solid var(--line);
  }
  .srange:hover {
    color: var(--mint);
    border-color: var(--mint-lo);
  }
  .rpick .link {
    margin-left: 8px;
    font-size: var(--fs-base);
    color: var(--mint);
  }
  .sname {
    flex: 1;
    min-width: 0;
    height: 26px;
    padding: 0 8px;
    font-size: var(--fs-base);
    color: var(--fg-1);
    background: var(--bg-1);
    border: 1px solid var(--line);
  }
  .copyrow {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 4px;
    padding: 6px 12px 0;
    font-size: var(--fs-sm);
    color: var(--fg-3);
  }
  .copyrow button {
    height: 22px;
    padding: 0 7px;
    font-size: var(--fs-sm);
    color: var(--mint);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .copyrow button:hover {
    background: var(--bg-2);
  }
  .copyrow button.armed {
    color: var(--gold-ink);
    background: var(--gold);
  }
  .rounds button:disabled {
    opacity: 0.25;
    cursor: default;
  }
  .cfg {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 4px;
    margin: 6px 0 2px;
  }
  .elt {
    height: 26px;
    padding: 0 9px;
    font-size: var(--fs-base);
    color: var(--fg-2);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .elt.on {
    color: var(--gold-ink);
    background: var(--gold);
    box-shadow: none;
  }
  .cl {
    margin: 0 2px 0 8px;
    font-size: var(--fs-sm);
    color: var(--fg-3);
  }
  .opt {
    position: relative;
    width: 30px;
    height: 30px;
    display: grid;
    place-items: center;
    background: var(--bg-2);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .opt img {
    width: 24px;
    height: 24px;
  }
  .opt.txt {
    width: auto;
    padding: 0 8px;
    font-size: var(--fs-sm);
    color: var(--fg-2);
  }
  .opt.on {
    box-shadow: inset 0 0 0 1.5px var(--mint);
    background: var(--mint-tint);
  }
  .opt em {
    position: absolute;
    right: -3px;
    top: -6px;
    padding: 0 2px;
    font-style: normal;
    font-size: var(--fs-3xs);
    line-height: 11px;
    color: var(--fg-1);
    background: var(--bg-4);
  }
  .cfgn {
    margin: 0 0 4px;
    font-size: var(--fs-sm);
    color: var(--fg-3);
  }
  .cfgn .link {
    margin-left: 8px;
    font-size: var(--fs-sm);
    color: var(--mint);
  }
</style>
