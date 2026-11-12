<script lang="ts">
  import { api, type PostDetail } from '../api'
  import { router, link } from '../router.svelte'
  import { session, toast } from '../session.svelte'
  import { ago, asset, cov, eq, op, strategy, ROMAN } from '../data'
  import OpAvatar from '../components/OpAvatar.svelte'
  import BondChip from '../components/BondChip.svelte'
  import Icon from '../components/Icon.svelte'

  let { id }: { id: string } = $props()
  let p = $state<PostDetail | null>(null)
  let error = $state('')
  $effect(() => {
    (import.meta.env.DEV && id.startsWith('demo-') ? import('../demo').then((m) => m.demoPosts.find((x) => x.id === id)!) : api.post(id))
      .then((r) => (p = r))
      .catch((e) => (error = e.status === 404 ? '这个阵容不存在，或者已经被删除。' : e.message))
  })

  const l = $derived(p?.lineup)
  const st = $derived(strategy(l?.strategyId ?? null))
  const mine = $derived(!!p && session.me?.id === p.author.id)
  // 目标盟约人数：按名单里带这个盟约的干员数，和助手里的算法一样
  const goalRows = $derived(
    (l?.goals ?? []).map((g) => {
      const c = cov(g.id)
      const have = l!.members.filter((m) => {
        const o = op(m.name)
        return o.core.includes(g.id) || o.extra.includes(g.id) || m.transform === g.id
      }).length
      return { ...g, have, need: c.thresholds[g.level - 1] ?? have }
    }),
  )

  async function copyCode() {
    const text = `【卫戍协议助手阵容】${l!.name} ${p!.code}`
    try {
      await navigator.clipboard.writeText(text)
    } catch {
      toast.show('复制失败，手动选中下面的分享码复制吧。', true)
      return
    }
    toast.show('已复制。到助手「阵容 → 我的阵容 → 导入」里粘贴。')
    api
      .imported(p!.id)
      .then((r) => (p!.imports = r.imports))
      .catch(() => {})
  }

  let liking = $state(false)
  async function like() {
    if (!session.me) return router.go(`/login?next=${encodeURIComponent(location.pathname)}`)
    liking = true
    try {
      const r = await api.like(p!.id, !p!.liked)
      p!.likes = r.likes
      p!.liked = r.liked
    } catch (e) {
      toast.show((e as Error).message, true)
    } finally {
      liking = false
    }
  }

  // 作者：改简介、删除
  let editing = $state(false)
  let draft = $state('')
  let saving = $state(false)
  async function save() {
    saving = true
    try {
      const r = await api.updatePost(p!.id, draft)
      p!.description = r.description
      editing = false
      toast.show('简介已更新。')
    } catch (e) {
      toast.show((e as Error).message, true)
    } finally {
      saving = false
    }
  }
  let armDelete = $state(false)
  async function remove() {
    try {
      await api.deletePost(p!.id)
      toast.show('已删除。')
      router.go('/me')
    } catch (e) {
      toast.show((e as Error).message, true)
    }
  }
</script>

<div class="in">
  <a class="back" href="/" use:link><Icon name="arrowLeft" size={16} />回到广场</a>

  {#if error}
    <div class="state"><p>{error}</p></div>
  {:else if !p || !l}
    <div class="state muted">加载中…</div>
  {:else}
    {#if p.hidden}<div class="banner bad">这个阵容已被管理员下架，只有你自己能看到。</div>{/if}
    {#if !p.current}<div class="banner">这是旧赛季的阵容，数据和当前赛季对不上，只能看，不能导入。</div>{/if}

    <div class="head">
      <div class="ht">
        <h1>{l.name}{#if p.featured}<span class="feat"><Icon name="star" size={12} />精选</span>{/if}</h1>
        <div class="by">{p.author.nickname} · 发布于 {ago(p.createdAt)}{#if p.updatedAt !== p.createdAt} · 更新于 {ago(p.updatedAt)}{/if}</div>
      </div>
      <div class="acts">
        <a class="btn ghost" class:off={!p.current} href={p.current ? `stronghold://import?post=${encodeURIComponent(p.id)}` : undefined} title="装了卫戍协议助手的话，直接在助手里打开这个阵容">在助手里打开</a>
        <button class="btn primary" disabled={!p.current} onclick={copyCode}><Icon name="copy" size={16} />复制分享码</button>
        <button class="btn ghost like" class:on={p.liked} disabled={liking} onclick={like} title={session.me ? '' : '登录后点赞'}>
          <Icon name={p.liked ? 'heartFill' : 'heart'} size={16} />{p.likes}
        </button>
        <span class="imp" title="导入次数"><Icon name="download" size={15} />{p.imports}</span>
      </div>
    </div>
    <p class="how">装了卫戍协议助手的话，点「在助手里打开」直接导入；没反应就复制分享码，到助手「阵容 → 我的阵容 → 导入」里粘贴。</p>

    <div class="cols">
      <div class="main">
        <section>
          <h2 class="sec-h">简介<small>Notes</small>{#if mine && !editing}<button class="btn sm ghost ed" onclick={() => ((draft = p!.description), (editing = true))}><Icon name="pencil" size={13} />编辑</button>{/if}</h2>
          {#if editing}
            <textarea bind:value={draft} rows="6" maxlength="500" placeholder="说说这套阵容的思路、运营要点、适合的首领……"></textarea>
            <div class="edrow">
              <span class="muted">{[...draft].length} / 500</span>
              <button class="btn ghost sm" onclick={() => (editing = false)}>取消</button>
              <button class="btn primary sm" disabled={saving} onclick={save}>{saving ? '保存中…' : '保存'}</button>
            </div>
          {:else if p.description}
            <p class="desc">{p.description}</p>
          {:else}
            <p class="muted">作者没有写简介。</p>
          {/if}
        </section>

        <section>
          <h2 class="sec-h">干员<small>Operators · {l.members.length}</small></h2>
          <div class="members">
            {#each l.members as m (m.name)}
              {@const o = op(m.name)}
              <div class="mem" class:key={m.key}>
                <OpAvatar name={m.name} size={56} isKey={m.key} />
                <div class="mi">
                  <div class="mn"><b>{m.name}</b><span class="tier" style:--c="var(--tier-{o.tier})">{ROMAN[o.tier]}</span>{#if m.key}<span class="keyt">关键</span>{/if}</div>
                  <div class="mb">{[...o.core, ...o.extra].map((c) => cov(c).name).join(' · ')}</div>
                  {#if m.equipment?.length}
                    <div class="me">
                      {#each m.equipment as x (x.name)}
                        <span class="eqc" class:pri={x.priority} title="{x.name}（{ROMAN[eq(x.name).tier]} 阶）{x.priority ? ' · 优先' : ' · 候选'}"><img src={asset.equipment(x.name)} alt="" />{x.name}</span>
                      {/each}
                    </div>
                  {/if}
                </div>
              </div>
            {/each}
          </div>
          <p class="legend"><i class="lk"></i>关键干员 <i class="lp"></i>优先装备 <i class="lc"></i>候选装备</p>
        </section>

        {#if l.equipment.length}
          <section>
            <h2 class="sec-h">通用装备<small>Equipment</small></h2>
            <div class="eqs">
              {#each l.equipment as n (n)}<span class="eqc"><img src={asset.equipment(n)} alt="" />{n}</span>{/each}
            </div>
          </section>
        {/if}
      </div>

      <aside class="side">
        <section>
          <h2 class="sec-h">目标盟约<small>Pacts</small></h2>
          {#if goalRows.length}
            <div class="goals">{#each goalRows as g (g.id)}<BondChip id={g.id} level={g.level} have={g.have} need={g.need} />{/each}</div>
          {:else}
            <p class="muted">没有设目标盟约。</p>
          {/if}
        </section>
        <section>
          <h2 class="sec-h">策略<small>Strategy</small></h2>
          {#if st}
            <div class="strat">
              <img src={asset.strategy(st.id)} alt="" />
              <div><b>{st.name}</b><span>{st.initiator}</span></div>
            </div>
            <p class="sdesc">{st.description.replace(/<[^>]+>/g, '').replace(/\\n/g, '\n')}</p>
          {:else}
            <p class="muted">没有选策略。</p>
          {/if}
        </section>
        <section>
          <h2 class="sec-h">分享码<small>Code</small></h2>
          <code class="code">{p.code}</code>
        </section>
        {#if mine}
          <section class="danger">
            {#if armDelete}
              <button class="btn danger sm" onclick={remove}><Icon name="trash" size={13} />确认删除</button>
              <button class="btn ghost sm" onclick={() => (armDelete = false)}>不删</button>
            {:else}
              <button class="btn ghost sm" onclick={() => (armDelete = true)}><Icon name="trash" size={13} />删除这个阵容</button>
            {/if}
          </section>
        {/if}
      </aside>
    </div>
  {/if}
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
    margin: 20px 0 14px;
    color: var(--fg-3);
  }
  .back:hover {
    color: var(--fg-1);
  }
  .state {
    padding: 80px 16px;
    text-align: center;
    color: var(--fg-3);
  }
  .banner {
    margin-bottom: 14px;
    padding: 10px 14px;
    color: var(--gold);
    background: rgb(243 179 34 / 0.08);
    box-shadow: inset 2px 0 0 var(--gold);
  }
  .banner.bad {
    color: #f2b8ba;
    background: var(--red-tint);
    box-shadow: inset 2px 0 0 var(--red);
  }
  .head {
    display: flex;
    flex-wrap: wrap;
    align-items: flex-end;
    gap: 16px;
    justify-content: space-between;
  }
  h1 {
    display: flex;
    align-items: center;
    gap: 10px;
    margin: 0;
    font-size: 26px;
  }
  .feat {
    display: inline-flex;
    align-items: center;
    gap: 3px;
    padding: 2px 7px;
    font-size: 12px;
    color: var(--gold-ink);
    background: var(--gold);
  }
  .by {
    margin-top: 4px;
    color: var(--fg-3);
    font-size: 13px;
  }
  .acts {
    display: flex;
    align-items: center;
    gap: 8px;
  }
  .acts .off {
    opacity: 0.45;
    pointer-events: none;
  }
  .like.on {
    color: #ff8a9a;
    box-shadow: inset 0 0 0 1px rgb(255 138 154 / 0.5);
  }
  .imp {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    padding: 0 6px;
    font-family: var(--font-term);
    color: var(--fg-3);
  }
  .how {
    margin: 10px 0 22px;
    font-size: 12.5px;
    color: var(--fg-3);
  }
  .cols {
    display: grid;
    grid-template-columns: 1fr 340px;
    gap: 24px;
    align-items: start;
  }
  section {
    margin-bottom: 24px;
  }
  .sec-h .ed {
    margin-left: auto;
  }
  .desc {
    margin: 0;
    white-space: pre-line;
    line-height: 1.8;
    color: var(--fg-1);
  }
  textarea {
    width: 100%;
    resize: vertical;
    line-height: 1.7;
  }
  .edrow {
    display: flex;
    align-items: center;
    justify-content: flex-end;
    gap: 8px;
    margin-top: 8px;
  }
  .edrow .muted {
    margin-right: auto;
    font-size: 12px;
  }
  .members {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
    gap: 8px;
  }
  .mem {
    display: flex;
    gap: 10px;
    padding: 8px;
    background: var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line-soft);
  }
  .mem.key {
    box-shadow: inset 0 0 0 1px rgb(243 179 34 / 0.35);
  }
  .mi {
    flex: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 3px;
  }
  .mn {
    display: flex;
    align-items: center;
    gap: 6px;
  }
  .tier {
    font-family: var(--font-term);
    font-size: 11px;
    padding: 0 4px;
    color: var(--c);
    box-shadow: inset 0 0 0 1px var(--c);
  }
  .keyt {
    font-size: 11px;
    padding: 0 5px;
    color: var(--gold-ink);
    background: var(--gold);
  }
  .mb {
    font-size: 12px;
    color: var(--fg-3);
  }
  .me,
  .eqs {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
    margin-top: 2px;
  }
  .eqc {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    height: 24px;
    padding: 0 7px 0 2px;
    font-size: 12px;
    color: var(--fg-2);
    background: var(--bg-2);
    box-shadow: inset 0 -2px 0 var(--fg-4);
  }
  .eqc.pri {
    color: var(--fg-1);
    background: rgb(243 179 34 / 0.12);
    box-shadow: inset 0 -2px 0 var(--gold);
  }
  .eqc img {
    width: 20px;
    height: 20px;
    object-fit: contain;
  }
  .legend {
    display: flex;
    align-items: center;
    gap: 6px;
    margin: 10px 0 0;
    font-size: 12px;
    color: var(--fg-4);
  }
  .legend i {
    width: 10px;
    height: 10px;
    margin-left: 8px;
  }
  .legend i:first-child {
    margin-left: 0;
  }
  .lk {
    background: linear-gradient(135deg, var(--gold) 50%, transparent 50%);
  }
  .lp {
    background: rgb(243 179 34 / 0.25);
    box-shadow: inset 0 -2px 0 var(--gold);
  }
  .lc {
    background: var(--bg-2);
    box-shadow: inset 0 -2px 0 var(--fg-4);
  }
  .goals {
    display: flex;
    flex-wrap: wrap;
    gap: 6px;
  }
  .strat {
    display: flex;
    align-items: center;
    gap: 10px;
  }
  .strat img {
    width: 44px;
    height: 44px;
    object-fit: cover;
  }
  .strat div {
    display: flex;
    flex-direction: column;
  }
  .strat span {
    font-size: 12px;
    color: var(--fg-3);
  }
  .sdesc {
    margin: 8px 0 0;
    font-size: 12.5px;
    line-height: 1.7;
    color: var(--fg-2);
    white-space: pre-line;
  }
  .code {
    display: block;
    padding: 10px;
    font-family: Consolas, monospace;
    font-size: 12px;
    word-break: break-all;
    color: var(--fg-2);
    background: var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line-soft);
    user-select: all;
  }
  .danger {
    display: flex;
    gap: 8px;
  }
  @media (max-width: 900px) {
    .cols {
      grid-template-columns: 1fr;
    }
  }
</style>
