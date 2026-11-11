<script lang="ts">
  import { fly, fade } from 'svelte/transition'
  import { cubicOut } from 'svelte/easing'
  import { app, goalProgress, rankForGoals } from '../lib/state.svelte'
  import { asset } from '../lib/assets'
  import BondIcon from './BondIcon.svelte'
  import OpTile from './OpTile.svelte'
  import EqTile from './EqTile.svelte'
  import Seg from './Seg.svelte'
  import Icon from './Icon.svelte'

  // 阵容编辑器：几乎全高的面板。上半是阵容本身（名字、目标盟约、想抓的干员），下半是挑干员。
  // 阵容不限人数；选了目标盟约后干员池默认只列这些盟约的干员，覆盖目标越多越靠前。
  const e = $derived(app.editing)
  let q = $state('')
  let bond = $state<string | null>(null)
  let goalOnly = $state(true)
  // 下半挑选区：干员 / 装备
  let poolKind = $state<'op' | 'eq' | 'st'>('op')
  const strat = $derived(e?.strategyId ? app.catalog.strategies.find((s) => s.id === e.strategyId) : null)
  const stPool = $derived(app.catalog.strategies.filter((s) => !q || s.name.includes(q) || s.initiator.includes(q) || s.description.includes(q)))
  const pickSt = (id: string) => e && (e.strategyId = e.strategyId === id ? null : id)
  let cat = $state<string | null>(null)
  const cats = [...new Set(app.catalog.equipment.flatMap((x) => x.categories))]
  const hasEq = (n: string) => !!e?.equipment.includes(n)
  const addEq = (n: string) => {
    if (!e) return
    e.equipment = hasEq(n) ? e.equipment.filter((x) => x !== n) : [...e.equipment, n]
  }
  const eqPool = $derived(
    app.catalog.equipment.filter((x) => (!q || x.name.includes(q)) && (!cat || x.categories.includes(cat))).sort((a, b) => b.tier - a.tier),
  )
  const empty = $derived(!e || (!e.members.length && !e.goals.length && !e.equipment.length))

  const inLineup = (n: string) => !!e?.members.some((m) => m.name === n)
  const add = (n: string) => {
    if (!e) return
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
  const save = async () => {
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
  <aside class="ed" transition:fly={{ y: 400, duration: 260, easing: cubicOut }}>
    <header>
      <input class="nm" bind:value={e.name} placeholder="名称（不填就叫「{autoName}」）" />
      <button class="x" onclick={() => (app.editing = null)} aria-label="关闭"><Icon name="close" size={16} /></button>
    </header>

    <div class="sec">
      <div class="lbl">目标盟约<em>点一次 I 档，再点 II 档，再点取消；本局阵容不完整的核心盟约不能选</em></div>
      <div class="goalpick">
        {#each app.catalog.covenants as c (c.id)}
          {@const lv = levelOf(c.id)}
          {@const blocked = c.kind === 'core' && !!app.matchCovenant(c.id)?.incomplete}
          <button class="gp" class:on={!!lv} class:core={c.kind === 'core'} class:blocked disabled={blocked && !lv} title={c.name} onclick={() => app.cycleGoal(c.id)}>
            <span class="glyph" style:--src="url({asset.bond(c.id)})"></span>
            {#if lv}<span class="lv term">{lv === 2 ? 'II' : 'I'}</span>{/if}
          </button>
        {/each}
      </div>
      <div class="bonds">
        {#each progress as g (g.covenant.id)}
          <span class="bc" class:short={!g.done}><BondIcon id={g.covenant.id} size={18} on />{g.covenant.name}<b>{g.have}/{g.need}</b></span>
        {:else}
          <span class="muted">还没有目标盟约</span>
        {/each}
      </div>
    </div>

    <div class="sec">
      <div class="lbl">想抓的干员 <b class="term">{e.members.length}</b><em>不限人数；点已选的头像切换「关键」</em></div>
      <div class="chosen">
        {#each e.members as m (m.name)}
          <OpTile name={m.name} transform={m.transform} isKey={m.key} size={34} showName={false} menu={false} onclick={() => toggleKey(m.name)} />
        {:else}
          <span class="muted">在下面点头像加入</span>
        {/each}
      </div>
      {#if strat}
        <div class="lbl">策略<em>点一下取消</em></div>
        <button class="stchip" onclick={() => pickSt(strat.id)}>
          <img src={asset.strategy(strat.id)} alt="" />
          <b>{strat.name}</b><span>{strat.initiator}</span>
          <Icon name="close" size={13} />
        </button>
      {/if}
      {#if e.equipment.length}
        <div class="lbl">想要的装备 <b class="term">{e.equipment.length}</b><em>点一下移出</em></div>
        <div class="chosen">
          {#each e.equipment as n (n)}
            <EqTile name={n} size={34} showName={false} menu={false} onclick={() => addEq(n)} />
          {/each}
        </div>
      {/if}
    </div>

    <div class="pick">
      <Seg
        items={[
          { id: 'op', label: '干员', count: e.members.length },
          { id: 'eq', label: '装备', count: e.equipment.length },
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
        <div class="bondpick">
          {#each app.catalog.covenants as c (c.id)}
            <button class="bp" class:on={bond === c.id} title={c.name} onclick={() => (bond = bond === c.id ? null : c.id)}>
              <span class="glyph" style:--src="url({asset.bond(c.id)})"></span>
            </button>
          {/each}
        </div>
        <div class="pool">
          {#each pool as o (o.name)}
            <span class="pi"><OpTile name={o.name} size={44} owned={inLineup(o.name)} menu={false} onclick={() => add(o.name)} /></span>
          {/each}
        </div>
      {:else if poolKind === 'st'}
        <p class="sthint">可不选。选了会显示在阵容卡上，分享码也会带上。</p>
        <div class="stlist">
          {#each stPool as s (s.id)}
            <button class="st" class:on={e.strategyId === s.id} onclick={() => pickSt(s.id)}>
              <img src={asset.strategy(s.id)} alt="" loading="lazy" />
              <span class="stt"><span class="stn"><b>{s.name}</b><em>{s.initiator}</em></span><span class="std">{s.description}</span></span>
              {#if e.strategyId === s.id}<span class="ck"><Icon name="check" size={13} /></span>{/if}
            </button>
          {/each}
        </div>
      {:else}
        <div class="cats">
          {#each cats as c (c)}
            <button class="chip" class:on={cat === c} onclick={() => (cat = cat === c ? null : c)}>{c}</button>
          {/each}
        </div>
        <div class="pool">
          {#each eqPool as x (x.name)}
            <EqTile name={x.name} size={44} picked={hasEq(x.name)} menu={false} onclick={() => addEq(x.name)} />
          {/each}
        </div>
      {/if}
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
  .scrim {
    position: absolute;
    inset: 0;
    background: rgb(0 0 0 / 0.6);
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
      linear-gradient(180deg, #202326, #141618 160px);
    box-shadow:
      0 -1px 0 var(--line),
      0 -12px 30px rgb(0 0 0 / 0.6);
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
    font-size: 15px;
    font-weight: 700;
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
    font-weight: 400;
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
    font-size: 12px;
    font-weight: 700;
    color: var(--fg-2);
  }
  .lbl b {
    color: var(--mint);
  }
  .lbl em {
    font-style: normal;
    font-weight: 400;
    font-size: 10.5px;
    color: var(--fg-4);
  }
  .goalpick {
    display: grid;
    grid-template-columns: repeat(13, 1fr);
    gap: 3px;
  }
  .gp {
    position: relative;
    aspect-ratio: 1;
    display: grid;
    place-items: center;
    border-radius: 50%;
    color: var(--fg-3);
    background: var(--bg-2);
  }
  .gp.core {
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .gp .glyph {
    width: 62%;
    height: 62%;
  }
  .gp.on {
    color: var(--mint-ink);
    background: var(--mint);
    box-shadow: var(--mint-glow);
  }
  .gp.blocked {
    opacity: 0.3;
  }
  .gp .lv {
    position: absolute;
    right: -3px;
    bottom: -3px;
    font-size: 8px;
    line-height: 1;
    padding: 2px 3px 1px;
    color: var(--gold-ink);
    background: var(--gold);
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
  .chosen {
    display: flex;
    flex-wrap: wrap;
    gap: 3px;
    max-height: 76px;
    overflow-y: auto;
  }
  .tog {
    flex: none;
    font-size: 11px;
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
    font-size: 11px;
    color: var(--fg-2);
  }
  .bc b {
    font-family: var(--font-term);
    margin-left: 2px;
    color: var(--mint);
  }
  .muted {
    font-size: 11px;
    color: var(--fg-4);
  }
  .pick {
    flex: 1;
    min-height: 0;
    display: flex;
    flex-direction: column;
    gap: 6px;
    padding: 10px 12px 0;
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
    font-size: 12px;
    color: var(--fg-1);
  }
  .stchip {
    align-self: flex-start;
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 3px 8px 3px 3px;
    font-size: 12px;
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
  .sthint {
    margin: 0;
    font-size: 11px;
    color: var(--fg-4);
  }
  .stlist {
    flex: 1;
    overflow-y: auto;
    display: flex;
    flex-direction: column;
    gap: 2px;
    padding-bottom: 10px;
  }
  .st {
    display: flex;
    align-items: flex-start;
    gap: 8px;
    padding: 6px 8px;
    text-align: left;
    background: var(--bg-1);
  }
  .st img {
    width: 32px;
    height: 32px;
    flex: none;
    object-fit: cover;
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .st:hover {
    background: var(--bg-2);
  }
  .st.on {
    background: linear-gradient(90deg, var(--mint-tint), transparent 70%), var(--bg-2);
    box-shadow: inset 2px 0 0 var(--mint);
  }
  .stt {
    flex: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 1px;
  }
  .stn {
    display: flex;
    align-items: baseline;
    gap: 6px;
  }
  .stn b {
    font-size: 12.5px;
  }
  .stn em {
    font-style: normal;
    font-size: 11px;
    color: var(--fg-4);
  }
  .std {
    font-size: 11px;
    line-height: 1.45;
    color: var(--fg-3);
    display: -webkit-box;
    -webkit-line-clamp: 2;
    line-clamp: 2;
    -webkit-box-orient: vertical;
    overflow: hidden;
  }
  .ck {
    flex: none;
    width: 18px;
    height: 18px;
    display: grid;
    place-items: center;
    color: var(--mint-ink);
    background: var(--mint);
  }
  .cats {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
  }
  .chip {
    font-size: 11.5px;
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
  .bondpick {
    display: grid;
    grid-template-columns: repeat(13, 1fr);
    gap: 3px;
  }
  .bp {
    aspect-ratio: 1;
    display: grid;
    place-items: center;
    border-radius: 50%;
    color: var(--fg-3);
    background: var(--bg-2);
  }
  .bp .glyph {
    width: 62%;
    height: 62%;
  }
  .bp.on {
    color: var(--mint-ink);
    background: var(--mint);
  }
  .pool {
    flex: 1;
    overflow-y: auto;
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(52px, 1fr));
    gap: 6px 2px;
    justify-items: center;
    align-content: start;
    padding: 4px 0 10px;
  }
  .err {
    margin: 0;
    padding: 7px 12px;
    font-size: 12px;
    line-height: 1.5;
    color: #f2b8ba;
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
    font-size: 12px;
    color: var(--fg-3);
    padding: 5px 8px;
  }
  .ghost:hover {
    color: var(--fg-1);
  }
  .danger {
    font-size: 12px;
    padding: 5px 10px;
    color: #fff;
    background: var(--red);
  }
  .primary {
    height: 30px;
    padding: 0 18px;
    font-size: 13px;
    font-weight: 700;
    color: var(--mint-ink);
    background: var(--mint);
  }
  .primary:disabled {
    color: var(--fg-4);
    background: var(--bg-3);
    cursor: default;
  }
</style>
