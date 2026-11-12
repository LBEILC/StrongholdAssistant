<script lang="ts">
  import { sheet } from '../lib/sheet'
  import { fade } from 'svelte/transition'
  import { app } from '../lib/state.svelte'
  import { api } from '../lib/api'
  import { plaza, postToLineup, PLAZA, REPORT_REASONS, type ReportReason } from '../lib/plaza.svelte'
  import PlazaComments from './PlazaComments.svelte'
  import LineupCard from './LineupCard.svelte'
  import LineupBreakdown from './LineupBreakdown.svelte'
  import Icon from './Icon.svelte'

  // 广场的底部面板，两种用途：看广场里的一个阵容（导入、点赞、看简介），发布本地阵容（写简介）。
  // 没登录时面板里直接给「登录」，没昵称时先起昵称
  const post = $derived(plaza.viewing)
  const pub = $derived(plaza.publishing)
  const open = $derived(!!post || !!pub)
  const close = () => {
    plaza.viewing = null
    plaza.publishing = null
    plaza.reporting = null
  }

  let desc = $state('')
  let nick = $state('')
  let busy = $state(false)
  let err = $state('')
  $effect(() => {
    if (pub) {
      desc = pub.description ?? ''
      err = ''
      plaza.check()
    }
  })
  $effect(() => {
    if (post) err = ''
  })
  const descLen = $derived([...desc].length)

  async function run(f: () => Promise<void>) {
    busy = true
    err = ''
    try {
      await f()
    } catch (e) {
      err = (e as Error).message
    } finally {
      busy = false
    }
  }

  const login = () => run(async () => void (await plaza.login()))
  const saveNick = () => run(() => plaza.setNickname(nick.trim()))
  const publish = () =>
    run(async () => {
      const p = await plaza.publish($state.snapshot(pub!), desc, app.catalog)
      plaza.publishing = null
      app.showToast(`已发布「${p.lineup.name}」到阵容广场`)
      plaza.viewing = p
    })

  const doImport = (asTarget: boolean) =>
    run(async () => {
      const p = post!
      plaza.imported(p.id).catch(() => {})
      plaza.viewing = null
      await app.importLineup(postToLineup(p), asTarget)
    })
  const like = () =>
    run(async () => {
      if (!plaza.me && !(await plaza.login())) return
      const r = await plaza.like(post!.id, !post!.liked)
      post!.likes = r.likes
      post!.liked = r.liked
    })
  const fav = () =>
    run(async () => {
      if (!plaza.me && !(await plaza.login())) return
      post!.favorited = (await plaza.favorite(post!.id, !post!.favorited)).favorited
      app.showToast(post!.favorited ? '已收藏，在广场「收藏」里找得到' : '已取消收藏')
    })
  const mine = $derived(!!post && plaza.me?.id === post.author.id)
  // 从别人的森空岛攻略导入的（简介开头「来自森空岛 · 作者」），发布前提醒征得原作者同意；自己的历史战绩不用
  const fromSkland = $derived(!!pub?.description?.startsWith('来自森空岛 · '))

  // 举报：面板切成举报页，提交或返回后回到详情
  const rep = $derived(plaza.reporting)
  let reason = $state<ReportReason | null>(null)
  let detail = $state('')
  $effect(() => {
    if (rep) {
      reason = null
      detail = ''
      err = ''
      plaza.check()
    }
  })
  const reasons = $derived((Object.keys(REPORT_REASONS) as ReportReason[]).filter((r) => r !== 'broken' || rep?.target.kind === 'post'))
  const sendReport = () =>
    run(async () => {
      if (!plaza.me && !(await plaza.login())) return
      await plaza.report({ target: rep!.target, reason: reason!, ...(detail.trim() && { detail: detail.trim() }) })
      plaza.reporting = null
      app.showToast('已收到举报，管理员会尽快处理')
    })
</script>

<svelte:window onkeydown={(e) => open && e.key === 'Escape' && (rep ? (plaza.reporting = null) : close())} />

{#if open}
  <div class="scrim dock" transition:fade={{ duration: 160 }} onclick={close} role="presentation"></div>
  <aside class="sheet" transition:sheet={{ y: 320, duration: 240 }}>
    {#if post && rep}
      <header>
        <span class="hi"><Icon name="flag" size={16} /></span>
        <h3>举报{rep.target.kind === 'post' ? '阵容' : '评论'}</h3>
        <button class="x" onclick={close} aria-label="关闭"><Icon name="close" size={16} /></button>
      </header>
      <p class="what">{rep.what}</p>
      <div class="rs" role="radiogroup" aria-label="举报理由">
        {#each reasons as r (r)}
          <button class="r" class:on={reason === r} role="radio" aria-checked={reason === r} onclick={() => (reason = r)}><i class="dot"></i>{REPORT_REASONS[r]}</button>
        {/each}
      </div>
      <textarea class="detail" bind:value={detail} maxlength="200" placeholder="补充说明（选填）"></textarea>
      <p class="hint">举报不会公开，只有管理员能看到。{plaza.me ? '' : '提交前要先登录阵容广场。'}</p>
      {#if err}<p class="err">{err}</p>{/if}
      <footer>
        <button class="ghost" onclick={() => (plaza.reporting = null)}>返回</button>
        <span class="sp"></span>
        <button class="primary" disabled={!reason || busy || plaza.loggingIn} onclick={sendReport}>{busy ? '提交中…' : '提交'}</button>
      </footer>
    {:else if post}
      <header>
        <span class="hi"><Icon name="grid" size={16} /></span>
        <h3>阵容广场</h3>
        <span class="term en">Plaza</span>
        <button class="x" onclick={close} aria-label="关闭"><Icon name="close" size={16} /></button>
      </header>
      {#if !post.current}<p class="warn">这是旧赛季的阵容，数据对不上，只能看，不能导入。</p>{/if}
      <div class="preview">
        <LineupCard lineup={postToLineup(post)} menu={false} headOnly>
          {#snippet sub()}
            <span class="by">{post.author.nickname}{#if mine}（我）{/if}{#if post.featured} · <b class="feat">精选</b>{/if}</span>
          {/snippet}
        </LineupCard>
      </div>
      <LineupBreakdown lineup={postToLineup(post)} />
      {#if post.description}<p class="desc">{post.description}</p>{/if}
      <div class="stats">
        <button class="like" class:on={post.liked} disabled={busy} onclick={like} title={plaza.me ? '' : '登录后点赞'}>
          <Icon name={post.liked ? 'heartFill' : 'heart'} size={14} />{post.likes}
        </button>
        <span title="导入次数"><Icon name="download" size={14} />{post.imports}</span>
        <button class="like" class:on={post.favorited} disabled={busy} onclick={fav} title={plaza.me ? '收藏后在广场「收藏」里找得到' : '登录后收藏'}>
          <Icon name={post.favorited ? 'starFill' : 'star'} size={14} />{post.favorited ? '已收藏' : '收藏'}
        </button>
        <span class="sp"></span>
        {#if !mine}<button class="lnk rep" onclick={() => (plaza.reporting = { target: { kind: 'post', id: post.id }, what: `${post.lineup.name} · ${post.author.nickname}` })}><Icon name="flag" size={12} />举报</button>{/if}
        <button class="lnk" onclick={() => api.openUrl(`${PLAZA}/p/${post.id}`)}>在网站上看<Icon name="arrowRight" size={12} /></button>
      </div>
      {#if err}<p class="err">{err}</p>{/if}
      <PlazaComments {post} />
      <footer>
        <button class="ghost" onclick={close}>关闭</button>
        <span class="sp"></span>
        <button class="secondary" disabled={!post.current || busy} onclick={() => doImport(false)}>导入</button>
        <button class="primary" disabled={!post.current || busy} onclick={() => doImport(true)}>导入并设为目标</button>
      </footer>
    {:else if pub}
      <header>
        <span class="hi"><Icon name="upload" size={16} /></span>
        <h3>发布到阵容广场</h3>
        <button class="x" onclick={close} aria-label="关闭"><Icon name="close" size={16} /></button>
      </header>
      <div class="preview"><LineupCard lineup={pub} menu={false} /></div>
      {#if fromSkland}<p class="warn">这套阵容来自森空岛，发布前请先征得原作者同意（森空岛规则不允许转载、搬运他人攻略）。</p>{/if}

      {#if !plaza.checked}
        <p class="hint">正在检查登录状态…</p>
      {:else if !plaza.me}
        <div class="acct">
          <p>发布要先登录阵容广场。会弹出一个登录小窗口，用邮箱验证码或密码登录。</p>
          <button class="primary" disabled={busy || plaza.loggingIn} onclick={login}>{plaza.loggingIn ? '等待登录…' : '登录'}</button>
        </div>
      {:else if !plaza.me.nickname}
        <div class="acct">
          <p>发布的阵容会用昵称署名，先起一个（2～16 个字，以后可以在网站上改）。</p>
          <div class="row">
            <input bind:value={nick} maxlength="16" placeholder="昵称" />
            <button class="primary" disabled={busy || [...nick.trim()].length < 2} onclick={saveNick}>保存</button>
          </div>
        </div>
      {:else}
        <label class="dl">
          <span>简介<em>{descLen} / 500 · 会保存到广场，不会改动本地阵容</em></span>
          <textarea bind:value={desc} rows="5" maxlength="500" placeholder="说说这套阵容的思路、运营要点、适合打哪个首领……可以不写"></textarea>
        </label>
        <p class="hint">以「{plaza.me.nickname}」署名公开发布，发布后在网站「我的」里可以改简介、删除。同一套阵容再发一次只会更新简介。</p>
      {/if}
      {#if err}<p class="err">{err}</p>{/if}
      <footer>
        <button class="ghost" onclick={close}>取消</button>
        <span class="sp"></span>
        <button class="primary" disabled={busy || !plaza.me?.nickname || descLen > 500} onclick={publish}>{busy ? '发布中…' : '发布'}</button>
      </footer>
    {/if}
  </aside>
{/if}

<style>
  .scrim {
    position: absolute;
    inset: 0;
    background: rgb(0 0 0 / 0.55);
    z-index: 20;
  }
  .sheet {
    position: absolute;
    left: 0;
    right: 0;
    bottom: 0;
    max-height: 88%;
    overflow-y: auto;
    z-index: 21;
    display: flex;
    flex-direction: column;
    gap: 8px;
    padding: 12px 12px 0;
    background:
      var(--noise),
      linear-gradient(180deg, #202326, #16181a 120px);
    box-shadow:
      0 -1px 0 var(--line),
      0 -12px 30px rgb(0 0 0 / 0.6);
  }
  .sheet::before {
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
    gap: 8px;
  }
  .hi {
    display: grid;
    color: var(--mint);
  }
  h3 {
    margin: 0;
    font-size: 15px;
  }
  .en {
    font-size: 10px;
    color: var(--fg-4);
  }
  .x {
    margin-left: auto;
    width: 28px;
    height: 28px;
    display: grid;
    place-items: center;
    color: var(--fg-3);
  }
  .x:hover {
    color: var(--fg-1);
    background: var(--bg-4);
  }
  .by {
    font-size: 11px;
    color: var(--fg-3);
  }
  .feat {
    color: var(--gold);
  }
  .desc {
    margin: 0;
    padding: 8px 10px;
    font-size: 12.5px;
    line-height: 1.7;
    white-space: pre-line;
    user-select: text;
    color: var(--fg-1);
    background: rgb(0 0 0 / 0.25);
    box-shadow: inset 2px 0 0 var(--mint-lo);
  }
  .stats {
    display: flex;
    align-items: center;
    gap: 12px;
    font-size: 12px;
    color: var(--fg-3);
  }
  .stats span,
  .like {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    font-family: var(--font-term);
  }
  .like {
    height: 24px;
    padding: 0 8px;
    color: var(--fg-2);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .like.on {
    color: #ff8a9a;
    box-shadow: inset 0 0 0 1px rgb(255 138 154 / 0.5);
  }
  .lnk {
    display: inline-flex;
    align-items: center;
    gap: 2px;
    font-size: 12px;
    color: var(--mint);
  }
  .lnk.rep {
    color: var(--fg-4);
  }
  .lnk.rep:hover {
    color: #f2b8ba;
  }
  .what {
    margin: 0;
    font-size: 12px;
    color: var(--fg-3);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .rs {
    display: grid;
    gap: 2px;
  }
  .r {
    display: flex;
    align-items: center;
    gap: 8px;
    height: 32px;
    padding: 0 10px;
    font-size: 12.5px;
    text-align: left;
    color: var(--fg-2);
    background: var(--bg-2);
  }
  .r:hover {
    color: var(--fg-1);
    background: var(--bg-3);
  }
  .r.on {
    color: var(--fg-1);
    background: var(--bg-3);
    box-shadow: inset 2px 0 0 var(--mint);
  }
  .dot {
    width: 11px;
    height: 11px;
    border-radius: 50%;
    box-shadow: inset 0 0 0 1px var(--fg-4);
  }
  .on .dot {
    background: var(--mint);
    box-shadow:
      inset 0 0 0 3px #1d2023,
      0 0 0 1px var(--mint);
  }
  .detail {
    field-sizing: content;
    min-height: 52px;
    max-height: 120px;
  }
  .warn {
    margin: 0;
    font-size: 12px;
    padding: 6px 8px;
    color: var(--gold);
    background: rgb(243 179 34 / 0.08);
    box-shadow: inset 2px 0 0 var(--gold);
  }
  .acct {
    display: flex;
    flex-direction: column;
    align-items: flex-start;
    gap: 8px;
    padding: 10px;
    background: rgb(0 0 0 / 0.25);
    box-shadow: inset 2px 0 0 var(--mint);
  }
  .acct p {
    margin: 0;
    font-size: 12px;
    line-height: 1.6;
    color: var(--fg-2);
  }
  .row {
    display: flex;
    gap: 6px;
    width: 100%;
  }
  .row input {
    flex: 1;
    min-width: 0;
    height: 30px;
    padding: 0 8px;
    background: var(--bg-1);
    border: 0;
    outline: 0;
    box-shadow: inset 0 0 0 1px var(--line);
    user-select: text;
  }
  .dl {
    display: flex;
    flex-direction: column;
    gap: 4px;
    font-size: 12px;
    color: var(--fg-2);
  }
  .dl em {
    margin-left: 8px;
    font-style: normal;
    font-size: 11px;
    color: var(--fg-4);
  }
  textarea {
    resize: none;
    padding: 8px;
    font: inherit;
    font-size: 12.5px;
    line-height: 1.6;
    color: var(--fg-1);
    background: var(--bg-1);
    border: 0;
    outline: 0;
    box-shadow: inset 0 0 0 1px var(--line);
    user-select: text;
  }
  textarea:focus,
  .row input:focus {
    box-shadow: inset 0 0 0 1px var(--mint-lo);
  }
  .hint {
    margin: 0;
    font-size: 11px;
    line-height: 1.6;
    color: var(--fg-4);
  }
  .err {
    margin: 0;
    font-size: 12px;
    line-height: 1.5;
    color: #f2b8ba;
    padding: 6px 8px;
    background: var(--red-tint);
    box-shadow: inset 2px 0 0 var(--red);
  }
  /* margin-top: auto：工作台里面板是右侧全高，内容少时底栏也贴底边 */
  footer {
    position: sticky;
    bottom: 0;
    display: flex;
    align-items: center;
    gap: 6px;
    margin: auto -12px 0;
    padding: 10px 12px;
    border-top: 1px solid var(--line);
    background: var(--bg-1);
  }
  .sp {
    flex: 1;
  }
  .ghost {
    font-size: 12px;
    color: var(--fg-3);
    padding: 5px 8px;
  }
  .ghost:hover {
    color: var(--fg-1);
  }
  .primary,
  .secondary {
    height: 30px;
    padding: 0 12px;
    font-size: 13px;
    font-weight: 700;
    white-space: nowrap;
  }
  .primary {
    color: var(--mint-ink);
    background: var(--mint);
  }
  .primary:hover:not(:disabled) {
    background: var(--mint-hi);
  }
  .secondary {
    color: var(--fg-1);
    background: var(--bg-4);
  }
  .primary:disabled,
  .secondary:disabled {
    color: var(--fg-4);
    background: var(--bg-3);
    cursor: default;
  }
</style>
