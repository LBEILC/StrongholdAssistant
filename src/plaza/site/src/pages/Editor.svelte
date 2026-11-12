<script lang="ts">
  import { api } from '../api'
  import { router, link } from '../router.svelte'
  import { session, toast } from '../session.svelte'
  import { covenants, op, eq, cov, asset, ROMAN } from '../data'
  import { encode, decode, known, type Lineup } from '../share'
  import StrategyPicker from '../components/StrategyPicker.svelte'
  import OpAvatar from '../components/OpAvatar.svelte'
  import Icon from '../components/Icon.svelte'

  // 网页上新建阵容并发布，或保存到「我的阵容」（v0.7.0，跟着账号云同步到助手）；从「我的阵容」点编辑时 edit 是那套阵容的 id。和助手编辑器同样的结构：目标盟约（点一下选上，有 II 档的再点升 II 档，再点取消）、想抓的干员（关键、候选装备），
  // 通用装备、策略、简介。草稿存在本机浏览器里，去登录再回来不会丢
  type Member = { name: string; key?: boolean; elite?: boolean; skill?: string; module?: string; equipment?: { name: string; priority: boolean }[] }
  interface Draft {
    name: string
    goals: { id: string; level: 1 | 2 }[]
    members: Member[]
    equipment: string[]
    strategyId: string | null
    description: string
    /** 回合阶段：网页上暂时只改成型阵容，阶段原样保留（助手里改） */
    stages?: Lineup['stages']
  }
  let { edit = null }: { edit?: string | null } = $props()
  const empty = (): Draft => ({ name: '', goals: [], members: [], equipment: [], strategyId: null, description: '' })
  function loadDraft(): Draft {
    try {
      const r: Draft = { ...empty(), ...JSON.parse(localStorage.getItem('sa_draft') ?? '{}') }
      // 早先的草稿里可能有只有一档的盟约被点成了 II 档，降回 I 档
      r.goals = r.goals.map((g) => ({ ...g, level: Math.min(g.level, cov(g.id).thresholds.length) as 1 | 2 }))
      return r
    } catch {
      return empty()
    }
  }
  let d = $state<Draft>(edit ? empty() : loadDraft())
  // 编辑我的阵容：从云端取那一套；改的不进草稿
  let loadingEdit = $state(!!edit)
  if (edit)
    api
      .myLineups()
      .then((ls) => {
        const l = ls.find((c) => c.id === edit)?.data
        if (!l) return (err = '这套阵容不在你的「我的阵容」里，可能已经删除了。')
        d = { name: l.name, goals: l.goals as Draft['goals'], members: l.members, equipment: l.equipment, strategyId: l.strategyId, description: l.description ?? '', ...(l.stages?.length && { stages: l.stages }) }
      })
      .catch((e) => (err = e.message))
      .finally(() => (loadingEdit = false))
  $effect(() => {
    if (edit) return
    const s = JSON.stringify(d)
    try {
      localStorage.setItem('sa_draft', s)
    } catch {
      /* 存不了就不存 */
    }
  })

  // 从分享码开始：把助手里复制的码贴进来，接着改
  let paste = $state('')
  let pasteErr = $state('')
  function fromCode() {
    // 网页读不了森空岛的页面，只有助手能读
    if (/game\.skland\.com\/arknights\/stronghold-guide/.test(paste))
      return (pasteErr = '森空岛的攻略链接要在卫戍协议助手里导入：「阵容 → 我的阵容 → 导入」里粘贴这个链接，导入后再发布到广场。')
    const r = decode(paste)
    if (!r.ok) return (pasteErr = r.error)
    const l = r.lineup
    d = { name: l.name, goals: l.goals, members: l.members, equipment: l.equipment, strategyId: l.strategyId, description: d.description, ...(l.stages?.length && { stages: l.stages }) }
    paste = ''
    pasteErr = ''
    toast.show('已从分享码载入，可以接着改。')
  }

  // 目标盟约：只有一档的（调和、独行、协防干员等）点一下选上、再点取消；有 II 档的 I → II → 取消
  const levelOf = (id: string) => d.goals.find((g) => g.id === id)?.level
  const hasII = (id: string) => cov(id).thresholds.length >= 2
  function cycleGoal(id: string) {
    const lv = levelOf(id)
    d.goals = !lv ? [...d.goals, { id, level: 1 }] : lv === 1 && hasII(id) ? d.goals.map((g) => (g.id === id ? { ...g, level: 2 } : g)) : d.goals.filter((g) => g.id !== id)
  }
  const progress = $derived(
    d.goals.map((g) => {
      const have = d.members.filter((m) => [...op(m.name).core, ...op(m.name).extra].includes(g.id)).length
      return { ...g, have, need: cov(g.id).thresholds[g.level - 1] }
    }),
  )
  const autoName = $derived(d.goals.slice(0, 3).map((g) => cov(g.id).name + (g.level === 2 ? ' II' : '')).join(' · ') || '未命名阵容')

  // 挑选区：干员 / 装备
  let tab = $state<'op' | 'eq'>('op')
  let q = $state('')
  let goalOnly = $state(true)
  let sel = $state<string | null>(null)
  const ops = $derived.by(() => {
    const all = [...known.operators].map(op).filter((o) => !o.obtain && (!q || o.name.includes(q)))
    const goalIds = d.goals.map((g) => g.id)
    const hits = (o: ReturnType<typeof op>) => goalIds.filter((id) => o.core.includes(id) || o.extra.includes(id)).length
    const list = goalIds.length && goalOnly ? all.filter((o) => hits(o) > 0) : all
    return list.sort((a, b) => hits(b) - hits(a) || b.tier - a.tier)
  })
  const eqs = $derived([...known.equipment].map(eq).filter((e) => !q || e.name.includes(q)).sort((a, b) => b.tier - a.tier))
  const inLineup = (n: string) => d.members.some((m) => m.name === n)
  function toggleOp(n: string) {
    if (inLineup(n)) {
      d.members = d.members.filter((m) => m.name !== n)
      if (sel === n) sel = null
    } else d.members = [...d.members, { name: n }]
  }
  const selM = $derived(sel ? d.members.find((m) => m.name === sel) : undefined)
  const has = (n: string) => (selM ? !!selM.equipment?.some((x) => x.name === n) : d.equipment.includes(n))
  function toggleEq(n: string) {
    if (selM) {
      const l = selM.equipment ?? []
      selM.equipment = l.some((x) => x.name === n) ? l.filter((x) => x.name !== n) : [...l, { name: n, priority: false }]
    } else d.equipment = d.equipment.includes(n) ? d.equipment.filter((x) => x !== n) : [...d.equipment, n]
  }
  function pickMember(n: string) {
    sel = sel === n ? null : n
    if (sel) tab = 'eq'
  }

  const toLineup = (id: string): Lineup => ({
    id,
    name: d.name.trim() || autoName,
    goals: d.goals,
    members: $state.snapshot(d.members),
    equipment: d.equipment,
    strategyId: d.strategyId,
    ...(d.stages?.length && { stages: $state.snapshot(d.stages) }),
    ...(d.description.trim() && { description: d.description.trim() }),
    source: 'user',
    updatedAt: new Date().toISOString(),
  })

  // 保存到我的阵容（v0.7.0）：推一条到云端；云端那套更新（助手刚同步过）就不覆盖
  async function saveMine() {
    if (!session.me) return router.go(`/login?next=${encodeURIComponent(location.pathname + location.search)}`)
    busy = true
    err = ''
    try {
      const l = toLineup(edit ?? crypto.randomUUID().replace(/-/g, ''))
      const r = await api.sync({ since: new Date().toISOString(), lineups: [{ id: l.id, data: l, updatedAt: l.updatedAt, deleted: false }] })
      if (r.conflicts.length) return (err = '云端有更新的版本（可能助手刚同步过），刷新页面后再改。')
      if (!edit) d = empty()
      toast.show('已保存到我的阵容。助手里打开「同步我的阵容和偏好」就能用。')
      router.go('/me')
    } catch (e) {
      err = (e as Error).message
    } finally {
      busy = false
    }
  }

  // 发布
  let busy = $state(false)
  let err = $state('')
  const canPublish = $derived(d.members.length > 0 || d.goals.length > 0)
  async function publish() {
    if (!session.me) return router.go('/login?next=/new')
    busy = true
    err = ''
    try {
      const p = await api.publish(encode(toLineup('')), d.description)
      d = empty()
      toast.show('发布成功。')
      router.go(`/p/${p.id}`)
    } catch (e) {
      err = (e as Error).message
    } finally {
      busy = false
    }
  }
  let armReset = $state(false)
</script>

<div class="in">
  <div class="top">
    <a class="back" href="/" use:link><Icon name="arrowLeft" size={16} />回到广场</a>
    <h1>新建阵容<span class="term">New lineup</span></h1>
    <div class="paste">
      <input bind:value={paste} placeholder="从分享码开始：粘贴助手里复制的分享码" onkeydown={(e) => e.key === 'Enter' && paste.trim() && fromCode()} />
      <button class="btn ghost sm" disabled={!paste.trim()} onclick={fromCode}>载入</button>
    </div>
    {#if pasteErr}<p class="err">{pasteErr}</p>{/if}
  </div>

  <div class="cols">
    <div class="left">
      <section>
        <h2 class="sec-h">目标盟约<small>点一下选上，有 II 档的再点升 II 档，再点取消</small></h2>
        <div class="goals">
          {#each covenants as c (c.id)}
            {@const lv = levelOf(c.id)}
            <button class="gp" class:on={!!lv} class:core={c.kind === 'core'} title={c.name} onclick={() => cycleGoal(c.id)}>
              <span class="glyph" style:--src="url({asset.bond(c.id)})"></span>
              <span class="gn">{c.name}</span>
              {#if lv && hasII(c.id)}<b class="term">{lv === 2 ? 'II' : 'I'}</b>{/if}
            </button>
          {/each}
        </div>
      </section>

      <section class="picker">
        <div class="ptabs">
          <button class:on={tab === 'op'} onclick={() => (tab = 'op')}>干员</button>
          <button class:on={tab === 'eq'} onclick={() => (tab = 'eq')}>装备</button>
          <input bind:value={q} placeholder={tab === 'op' ? '搜索干员' : '搜索装备'} />
          {#if tab === 'op' && d.goals.length}
            <button class="tog" class:on={goalOnly} onclick={() => (goalOnly = !goalOnly)}>只看目标盟约</button>
          {/if}
        </div>
        {#if tab === 'eq'}
          <p class="for" class:member={!!selM}>
            {#if selM}给 <b>{selM.name}</b> 选候选装备<button onclick={() => (sel = null)}>改加通用装备</button>{:else}点装备加入通用装备；要给某名干员配，先在右边点他的「装备」{/if}
          </p>
        {/if}
        {#if tab === 'op'}
          <div class="pool">
            {#each ops as o (o.name)}
              <button class="pi" class:picked={inLineup(o.name)} onclick={() => toggleOp(o.name)}>
                <OpAvatar name={o.name} size={52} showName />
                {#if inLineup(o.name)}<span class="ck"><Icon name="check" size={14} /></span>{/if}
              </button>
            {/each}
          </div>
        {:else}
          <div class="pool eqp">
            {#each eqs as e (e.name)}
              <button class="ei" class:picked={has(e.name)} style:--c="var(--tier-{e.tier})" onclick={() => toggleEq(e.name)}>
                <img src={asset.equipment(e.name)} alt="" loading="lazy" />
                <span>{e.name}</span>
                {#if has(e.name)}<span class="ck"><Icon name="check" size={13} /></span>{/if}
              </button>
            {/each}
          </div>
        {/if}
      </section>
    </div>

    <aside class="right">
      <div class="card">
        <input class="nm" bind:value={d.name} maxlength="30" placeholder="阵容名（不填就叫「{autoName}」）" />
        {#if progress.length}
          <div class="prog">
            {#each progress as g (g.id)}
              <span class="pg" class:short={g.have < g.need}><span class="glyph" style:--src="url({asset.bond(g.id)})"></span>{cov(g.id).name}{#if g.level === 2}<em>II</em>{/if}<b>{g.have}/{g.need}</b></span>
            {/each}
          </div>
        {/if}

        <div class="lbl">想抓的干员<em>{d.members.length} 名</em></div>
        {#if d.members.length}
          <div class="mems">
            {#each d.members as m (m.name)}
              <div class="mem" class:sel={sel === m.name}>
                <OpAvatar name={m.name} size={36} isKey={m.key} />
                <div class="mi">
                  <div class="mn">
                    <b>{m.name}</b><span class="tier" style:--c="var(--tier-{op(m.name).tier})">{ROMAN[op(m.name).tier]}</span>
                    <span class="sp"></span>
                    <button class="mini" class:key={m.key} onclick={() => (m.key = !m.key)} title="关键干员：商店里优先提醒">{m.key ? '★ 关键' : '☆ 关键'}</button>
                    <button class="mini" class:on={sel === m.name} onclick={() => pickMember(m.name)}>装备</button>
                    <button class="mini x" aria-label="移出{m.name}" onclick={() => toggleOp(m.name)}><Icon name="close" size={12} /></button>
                  </div>
                  {#if m.equipment?.length}
                    <div class="mes">
                      {#each m.equipment as x (x.name)}
                        <button class="ec" class:pri={x.priority} title={x.priority ? '优先装备，点一下改成候选' : '候选装备，点一下改成优先'} onclick={() => (x.priority = !x.priority)}>
                          <img src={asset.equipment(x.name)} alt="" />{x.name}<em>{x.priority ? '优先' : '候选'}</em>
                        </button>
                      {/each}
                    </div>
                  {/if}
                </div>
              </div>
            {/each}
          </div>
        {:else}
          <p class="muted small">在左边点干员加入。</p>
        {/if}

        {#if d.equipment.length}
          <div class="lbl">通用装备<em>不指定干员；点一下移出</em></div>
          <div class="mes">
            {#each d.equipment as n (n)}<button class="ec" onclick={() => (d.equipment = d.equipment.filter((x) => x !== n))}><img src={asset.equipment(n)} alt="" />{n}</button>{/each}
          </div>
        {/if}

        <div class="lbl">策略<em>可不选</em></div>
        <StrategyPicker bind:value={d.strategyId} />

        <div class="lbl">简介<em>{[...d.description].length} / 500</em></div>
        <textarea bind:value={d.description} rows="4" maxlength="500" placeholder="这套阵容的思路、运营要点、适合打哪个首领……可以不写"></textarea>

        {#if err}<p class="err">{err}</p>{/if}
        {#if d.stages?.length}<p class="muted small">这套阵容前面还有 {d.stages.length} 个回合阶段，网页上只改成型阵容，阶段原样保留；要改阶段请在助手里改。</p>{/if}
        <button class="btn full" class:primary={!!edit} disabled={!canPublish || busy || loadingEdit} onclick={saveMine}>
          {busy ? '保存中…' : session.me ? (edit ? '保存修改' : '保存到我的阵容') : '登录后保存到我的阵容'}
        </button>
        {#if !edit}
          <button class="btn primary full" disabled={!canPublish || busy} onclick={publish}>
            {busy ? '发布中…' : session.me ? `以「${session.me.nickname ?? '…'}」发布到广场` : '登录后发布'}
          </button>
        {/if}
        <div class="foot">
          <span class="muted small">{edit ? '正在编辑「我的阵容」里的一套。' : '草稿自动存在这台电脑的浏览器里。'}</span>
          <button class="lnk" class:danger={armReset} onclick={() => (armReset ? ((d = empty()), (sel = null), (armReset = false)) : (armReset = true))}>{armReset ? '确认清空' : '清空'}</button>
        </div>
      </div>
    </aside>
  </div>
</div>

<style>
  .in {
    width: min(1200px, 100% - 32px);
    margin: 0 auto;
  }
  .back {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    margin: 20px 0 10px;
    color: var(--fg-3);
  }
  .back:hover {
    color: var(--fg-1);
  }
  .top {
    margin-bottom: 18px;
  }
  h1 {
    display: flex;
    align-items: baseline;
    gap: 10px;
    margin: 0 0 12px;
    font-size: 24px;
  }
  h1 .term {
    font-size: 11px;
    font-weight: 400;
    color: var(--mint);
  }
  .paste {
    display: flex;
    gap: 6px;
    max-width: 560px;
  }
  .paste input {
    flex: 1;
  }
  .cols {
    display: grid;
    grid-template-columns: 1fr 400px;
    gap: 24px;
    align-items: start;
  }
  section {
    margin-bottom: 20px;
  }
  .goals {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(104px, 1fr));
    gap: 3px;
  }
  .gp {
    position: relative;
    display: flex;
    align-items: center;
    gap: 6px;
    height: 32px;
    padding: 0 8px 0 6px;
    font-size: 12.5px;
    color: var(--fg-3);
    background: var(--bg-1);
    text-align: left;
  }
  .gp .glyph {
    flex: none;
    width: 18px;
    height: 18px;
  }
  .gp:hover {
    color: var(--fg-1);
  }
  .gp.on {
    color: var(--mint);
    background: var(--mint-tint);
    box-shadow: inset 0 0 0 1px var(--mint-lo);
  }
  .gp b {
    margin-left: auto;
    font-size: 11px;
  }
  .gn {
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  /* 挑选区跟着页面一起滚（不再套一层滚动，手机上手指按在列表里也能把页面滚回去）；分段和搜索吸在顶栏下面 */
  .ptabs {
    position: sticky;
    top: 56px;
    z-index: 2;
    display: flex;
    gap: 2px;
    align-items: center;
    padding: 8px 0;
    background: rgb(11 12 13 / 0.86);
    backdrop-filter: blur(8px);
  }
  /* 手机上一行放不下时让搜索框变窄，按钮文字不折行 */
  .ptabs > button {
    flex: none;
    white-space: nowrap;
    height: 32px;
    padding: 0 14px;
    color: var(--fg-2);
    background: var(--bg-2);
  }
  .ptabs > button.on {
    color: var(--mint-ink);
    background: var(--mint);
    font-weight: 700;
  }
  .ptabs input {
    margin-left: 8px;
    flex: 1;
    width: 0;
    max-width: 260px;
    padding: 6px 10px;
  }
  .ptabs .tog {
    height: 28px;
    padding: 0 10px;
    font-size: 12px;
    color: var(--fg-3);
    background: none;
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .ptabs .tog.on {
    color: var(--mint);
    background: none;
    box-shadow: inset 0 0 0 1px var(--mint-lo);
    font-weight: 400;
  }
  .for {
    margin: 0 0 8px;
    font-size: 12px;
    color: var(--fg-4);
  }
  .for.member {
    color: var(--mint);
  }
  .for b {
    color: var(--fg-1);
  }
  .for button {
    margin-left: 8px;
    font-size: 12px;
    color: var(--fg-3);
    text-decoration: underline;
  }
  .pool {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(72px, 1fr));
    gap: 10px 6px;
    padding: 10px;
    background: var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line-soft);
  }
  .pi {
    position: relative;
    display: flex;
    justify-content: center;
    padding: 4px 0;
  }
  .pi.picked {
    background: var(--mint-tint);
    box-shadow: inset 0 0 0 1px var(--mint-lo);
  }
  .ck {
    position: absolute;
    right: 4px;
    top: 4px;
    display: grid;
    place-items: center;
    width: 18px;
    height: 18px;
    color: var(--mint-ink);
    background: var(--mint);
    border-radius: 50%;
  }
  .eqp {
    grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
    gap: 4px;
  }
  .ei {
    position: relative;
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 4px 8px 4px 4px;
    font-size: 12.5px;
    color: var(--fg-2);
    background: var(--bg-2);
    box-shadow: inset 0 -2px 0 var(--c);
    text-align: left;
  }
  .ei img {
    width: 32px;
    height: 32px;
    object-fit: contain;
  }
  .ei.picked {
    color: var(--fg-1);
    background: var(--mint-tint);
  }
  .ei .ck {
    top: 50%;
    transform: translateY(-50%);
  }
  .right {
    position: sticky;
    top: 72px;
  }
  .card {
    display: flex;
    flex-direction: column;
    gap: 8px;
    padding: 14px;
    background: linear-gradient(180deg, var(--bg-2), var(--bg-1));
    box-shadow: inset 0 0 0 1px var(--line-soft);
    max-height: calc(100vh - 96px);
    overflow-y: auto;
  }
  .nm {
    font-size: 16px;
    font-weight: 700;
  }
  .prog {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
  }
  .pg {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    height: 24px;
    padding: 0 8px 0 4px;
    font-size: 12px;
    background: var(--bg-3);
    border-radius: 12px;
  }
  .pg .glyph {
    width: 16px;
    height: 16px;
    color: var(--mint);
  }
  .pg em {
    font-style: normal;
    color: var(--mint);
    font-size: 11px;
  }
  .pg b {
    font-family: var(--font-term);
    font-weight: 500;
    color: var(--mint);
  }
  .pg.short b {
    color: var(--gold);
  }
  .lbl {
    display: flex;
    align-items: baseline;
    gap: 8px;
    margin-top: 6px;
    font-size: 12px;
    font-weight: 700;
    color: var(--fg-2);
  }
  .lbl em {
    font-style: normal;
    font-weight: 400;
    font-size: 11px;
    color: var(--fg-4);
  }
  .mems {
    display: flex;
    flex-direction: column;
    gap: 4px;
  }
  .mem {
    display: flex;
    gap: 8px;
    padding: 6px;
    background: rgb(0 0 0 / 0.2);
  }
  .mem.sel {
    box-shadow: inset 2px 0 0 var(--mint);
  }
  .mi {
    flex: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 4px;
  }
  .mn {
    display: flex;
    align-items: center;
    gap: 5px;
    font-size: 13px;
  }
  .mn .sp {
    flex: 1;
  }
  .tier {
    font-family: var(--font-term);
    font-size: 10px;
    padding: 0 3px;
    color: var(--c);
    box-shadow: inset 0 0 0 1px var(--c);
  }
  .mini {
    height: 22px;
    padding: 0 7px;
    font-size: 11px;
    color: var(--fg-3);
    background: var(--bg-3);
  }
  .mini:hover {
    color: var(--fg-1);
  }
  .mini.key {
    color: var(--gold-ink);
    background: var(--gold);
    font-weight: 700;
  }
  .mini.on {
    color: var(--mint-ink);
    background: var(--mint);
  }
  .mini.x {
    display: grid;
    place-items: center;
    width: 22px;
    padding: 0;
    background: none;
  }
  .mes {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
  }
  .ec {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    height: 24px;
    padding: 0 6px 0 2px;
    font-size: 12px;
    color: var(--fg-2);
    background: var(--bg-3);
    box-shadow: inset 0 -2px 0 var(--fg-4);
  }
  .ec img {
    width: 20px;
    height: 20px;
    object-fit: contain;
  }
  .ec em {
    font-style: normal;
    font-size: 10px;
    padding: 0 4px;
    color: var(--fg-3);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .ec.pri {
    color: var(--fg-1);
    background: rgb(243 179 34 / 0.12);
    box-shadow: inset 0 -2px 0 var(--gold);
  }
  .ec.pri em {
    color: var(--gold-ink);
    background: var(--gold);
    box-shadow: none;
    font-weight: 700;
  }
  textarea {
    /* 随内容长高，不要原生的拖拽手柄 */
    resize: none;
    field-sizing: content;
    min-height: 112px;
    max-height: 360px;
    line-height: 1.6;
  }
  .full {
    width: 100%;
    height: 40px;
    margin-top: 6px;
  }
  .foot {
    display: flex;
    align-items: center;
    justify-content: space-between;
  }
  .small {
    font-size: 12px;
  }
  .lnk {
    font-size: 12px;
    color: var(--fg-3);
    text-decoration: underline;
  }
  .lnk.danger {
    color: var(--red);
  }
  .err {
    margin: 4px 0 0;
  }
  @media (max-width: 960px) {
    .cols {
      grid-template-columns: 1fr;
    }
    .right {
      position: static;
    }
    .card {
      max-height: none;
    }
  }
</style>
