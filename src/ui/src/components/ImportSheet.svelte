<script lang="ts">
  import { sheet } from '../lib/sheet'
  import { fade } from 'svelte/transition'
  import { untrack } from 'svelte'
  import { app } from '../lib/state.svelte'
  import { api, type SklandGuide, type SklandRecord } from '../lib/api'
  import { decodeLineup } from '../lib/share'
  import { plaza, postToLineup, type Post } from '../lib/plaza.svelte'
  import { recordTitle, recordToLineup, sklandLink, guideToLineup } from '../lib/skland'
  import { allStages, stageLabel, stageView } from '../lib/stages'
  import LineupCard from './LineupCard.svelte'
  import Icon from './Icon.svelte'
  import ErrorNote from './ErrorNote.svelte'

  // 导入阵容：底部面板。粘贴分享码马上解码预览（整句转发的消息也行）；粘贴森空岛攻略链接就去读那篇攻略。
  // 名字可以先改，再导入或导入并设为目标
  let text = $state('')
  let name = $state('')
  let box: HTMLTextAreaElement | undefined = $state()
  const link = $derived(sklandLink(text))
  // 阵容广场的帖子链接（v0.9.2）：网站复制分享码时带上，读帖子拿简介；只粘链接也能导入。只按 id 向广场读，不访问链接里的域名
  const postId = $derived(link ? null : (text.match(/https?:\/\/[^\s/]+\/p\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})/i)?.[1] ?? null))
  let post = $state<Post | null>(null)
  let postErr = $state('')
  $effect(() => {
    const id = postId
    post = null
    postErr = ''
    if (!id) return
    plaza
      .post(id)
      .then((p) => id === postId && (post = p))
      .catch((e) => id === postId && (postErr = (e as Error).message))
  })
  const fromPost = $derived(post?.current ? postToLineup(post) : null)
  const result = $derived(!link && text.trim() ? decodeLineup(text, app.catalog) : null)

  // 森空岛：链接变了就重新读；读的过程中又换了链接，旧结果丢掉
  let guide = $state<SklandGuide | null>(null)
  let reading = $state(false)
  let readErr = $state('')
  let stageIx = $state(0)
  let retry = $state(0)
  $effect(() => {
    void retry
    const url = link
    guide = null
    readErr = ''
    if (!url) return
    reading = true
    api
      .sklandGuide(url)
      .then((g) => {
        if (url !== link) return
        guide = g
        stageIx = -1
      })
      .catch((e) => url === link && (readErr = (e as Error).message))
      .finally(() => url === link && (reading = false))
  })
  // 森空岛历史战绩：宿主弹森空岛小窗口，用户登录后读回最近的对局，在这里挑一局
  let records = $state<SklandRecord[] | null>(null)
  let picked = $state<SklandRecord | null>(null)
  let histBusy = $state(false)
  let histErr = $state('')
  // 森空岛上次读到的角色，换账号用（v0.8.0）
  let account = $state<{ nickname: string } | null>(null)
  async function history() {
    histBusy = true
    histErr = ''
    try {
      const r = await api.sklandHistory()
      account = await api.sklandAccount()
      if (!r) return
      records = r
      picked = null
      text = ''
    } catch (e) {
      histErr = (e as Error).message
    } finally {
      histBusy = false
    }
  }

  // 换账号：退出森空岛登录，马上弹窗让用户登录另一个
  async function switchAccount() {
    await api.sklandLogout()
    account = null
    records = null
    picked = null
    history()
  }

  const sk = $derived(
    picked ? recordToLineup(picked, app.catalog) : guide ? guideToLineup(guide, app.catalog) : null,
  )

  const base = $derived(sk?.lineup ?? fromPost ?? (result?.ok ? result.lineup : null))
  // 从广场帖子导入的记一次导入数（和广场里点导入一样）
  const doImport = (asTarget: boolean) => {
    if (fromPost && post) plaza.imported(post.id).catch(() => {})
    app.importLineup(lineup!, asTarget)
  }
  const lineup = $derived(base ? { ...base, name: name.trim() || base.name } : null)
  const close = () => (app.importOpen = false)

  // 每次打开清空，并把光标放进输入框，直接 Ctrl+V
  $effect(() => {
    if (!app.importOpen) return
    // 只跟着「打开」走：不追踪 importPrefill，否则下面清空它会让这里重跑、把刚填的文字也清掉
    text = untrack(() => app.importPrefill)
    app.importPrefill = ''
    name = ''
    records = null
    picked = null
    histErr = ''
    api.sklandAccount().then((a) => (account = a))
    queueMicrotask(() => box?.focus())
  })
  // 解出新的阵容时，名字框填上码里（或攻略）的名字
  $effect(() => {
    if (base) name = base.name
  })
</script>

<svelte:window onkeydown={(e) => app.importOpen && e.key === 'Escape' && close()} />

{#if app.importOpen}
  <div class="scrim" transition:fade={{ duration: 160 }} onclick={close} role="presentation"></div>
  <aside class="sheet win" transition:sheet={{ y: 320, duration: 240 }}>
    <header>
      <span class="hi"><Icon name="paste" size={16} /></span>
      <h3>导入阵容</h3>
      <button class="x" onclick={close} aria-label="关闭"><Icon name="close" size={16} /></button>
    </header>
    <div class="scroll">
    <p class="hint">粘贴别人发来的分享码，整句消息也可以；也可以粘贴阵容广场或森空岛攻略的链接。</p>
    <textarea bind:this={box} bind:value={text} rows="3" placeholder="【卫戍协议助手阵容】叙拉古 · 家族 SA2-…" spellcheck="false" oninput={() => (picked = null)}></textarea>
    <div class="hist">
      <button class="lnk" disabled={histBusy} onclick={history}><Icon name="download" size={12} />{histBusy ? '等待森空岛窗口…' : '从森空岛历史战绩导入'}</button>
      {#if account}
        <span class="acct">森空岛：{account.nickname}<button class="sw" disabled={histBusy} onclick={switchAccount}>换账号</button></span>
      {:else}
        <span>会弹出森空岛窗口，没登录的话在里面登录</span>
      {/if}
    </div>
    {#if histErr}
      <ErrorNote title="读取森空岛历史战绩失败" error={histErr}>
        {#snippet actions()}<button onclick={history} disabled={histBusy}><Icon name="refresh" size={12} />重试</button>{/snippet}
      </ErrorNote>
    {/if}
    {#if records && !link}
      {#if records.length}
        <ul class="recs">
          {#each records as r (r.id)}
            <li>
              <button class:on={picked?.id === r.id} onclick={() => (picked = r)}>
                <b>{recordTitle(r)}</b>
                <span>{new Date(r.time).toLocaleString('zh-CN', { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' })} · {r.operators.length} 名干员</span>
              </button>
            </li>
          {/each}
        </ul>
      {:else}
        <p class="hint">森空岛里还没有这个赛季的历史战绩。</p>
      {/if}
    {/if}

    {#if link && reading}
      <p class="reading"><i class="spin"></i>正在从森空岛读取攻略…</p>
    {:else if link && readErr}
      <ErrorNote title="读取森空岛攻略失败" error={readErr} context={{ 链接: link }}>
        {#snippet actions()}
          <button onclick={() => retry++}><Icon name="refresh" size={12} />重试</button>
          <button onclick={() => api.openUrl(link!)}>在浏览器里打开<Icon name="arrowRight" size={12} /></button>
        {/snippet}
      </ErrorNote>
    {:else if postId && !post && !postErr && !(result?.ok)}
      <p class="reading"><i class="spin"></i>正在从阵容广场读取…</p>
    {:else if postId && postErr && !(result?.ok)}
      <p class="err">读不到这篇广场阵容：{postErr}</p>
    {:else if post && !post.current && !(result?.ok)}
      <p class="err">这篇广场阵容是旧赛季的，导入不了。</p>
    {:else if result && !result.ok && !postId}
      <p class="err">{result.error}</p>
    {/if}
    {#if fromPost && post}
      <div class="src">
        <span class="tag term">PLAZA</span>
        <span class="by">阵容广场 · {post.author.nickname}</span>
      </div>
    {/if}

    {#if sk}
      {#if guide && !picked}
        <div class="src">
          <span class="tag term">SKLAND</span>
          <span class="by">森空岛 · {guide.author}</span>
          <!-- 多阶段攻略整篇导入：回合阶段 + 成型阵容；这里只切换预览看哪个阶段 -->
          {#if lineup?.stages?.length}
            <div class="seg">
              {#each allStages(lineup) as s, i (i)}
                <button class:on={(stageIx < 0 ? allStages(lineup).length - 1 : stageIx) === i} onclick={() => (stageIx = i)}>{s.final ? '成型阵容' : stageLabel(s)}</button>
              {/each}
            </div>
          {/if}
        </div>
      {/if}
      {#if picked && sk.unknown.operators && !sk.unknown.equipment}
        <!-- 历史战绩里的自选干员（编号 chess_char_…_diy…，如凯尔希·思衡托）不在助手的干员表里，联机单人都有 -->
        <p class="warn">有 {sk.unknown.operators} 名干员不在助手的干员表里，多半是自选干员，没有导入。</p>
      {:else if sk.unknown.operators || sk.unknown.equipment}
        <p class="warn">
          有 {[sk.unknown.operators && `${sk.unknown.operators} 名干员`, sk.unknown.equipment && `${sk.unknown.equipment} 件装备`].filter(Boolean).join('、')}没认出，没有导入，可能要更新助手。
        </p>
      {/if}
    {/if}

    {#if lineup}
      <label class="nm">
        <span>名称</span>
        <input bind:value={name} maxlength="30" />
      </label>
      <div class="preview"><LineupCard lineup={lineup.stages?.length && stageIx >= 0 ? stageView(lineup, stageIx) : lineup} menu={false} /></div>
      {#if (sk || fromPost) && lineup.description}<p class="desc">{lineup.description}</p>{/if}
    {/if}

    </div>
    <footer>
      <button class="ghost" onclick={close}>取消</button>
      <span class="sp"></span>
      <button class="secondary" disabled={!lineup} onclick={() => doImport(false)}>导入</button>
      <button class="primary" disabled={!lineup} onclick={() => doImport(true)}>导入并设为目标</button>
    </footer>
  </aside>
{/if}

<style>
  .scrim {
    position: absolute;
    inset: 0;
    background: var(--scrim);
    z-index: 20;
  }
  .sheet {
    position: absolute;
    left: 0;
    right: 0;
    bottom: 0;
    max-height: 86%;
    overflow: hidden;
    z-index: 21;
    display: flex;
    flex-direction: column;
    background:
      var(--noise),
      linear-gradient(180deg, var(--sheet-top), var(--sheet-bottom) 120px);
    box-shadow:
      0 -1px 0 var(--line),
      0 -12px 30px rgb(var(--shade-rgb) / 0.6);
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
    font-size: var(--fs-xl);
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
  .hint {
    margin: -4px 0 0;
    font-size: var(--fs-sm);
    color: var(--fg-4);
  }
  textarea {
    resize: none;
    padding: 8px;
    font: inherit;
    font-size: var(--fs-base);
    line-height: 1.5;
    color: var(--fg-1);
    background: var(--bg-1);
    border: 0;
    outline: 0;
    box-shadow: inset 0 0 0 1px var(--line);
    user-select: text;
    word-break: break-all;
  }
  textarea:focus {
    box-shadow: inset 0 0 0 1px var(--mint-lo);
  }
  textarea::placeholder {
    color: var(--fg-4);
  }
  .err {
    margin: 0;
    font-size: var(--fs-base);
    line-height: 1.5;
    color: var(--red-fg);
    padding: 6px 8px;
    background: var(--red-tint);
    box-shadow: inset 2px 0 0 var(--red);
  }
  .hist {
    display: flex;
    align-items: center;
    gap: 8px;
    margin-top: -2px;
    font-size: var(--fs-sm);
    color: var(--fg-4);
  }
  .hist .lnk {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    font-size: var(--fs-base);
    color: var(--mint);
  }
  .hist .lnk:disabled {
    color: var(--fg-4);
  }
  .hist .acct {
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .hist .sw {
    margin-left: 6px;
    color: var(--fg-2);
    text-decoration: underline;
    text-underline-offset: 2px;
  }
  .hist .sw:hover:not(:disabled) {
    color: var(--mint);
  }
  .recs {
    display: grid;
    gap: 2px;
    max-height: 200px;
    overflow-y: auto;
    margin: 0;
    padding: 0;
    list-style: none;
  }
  .recs button {
    width: 100%;
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    gap: 8px;
    padding: 6px 10px;
    font-size: var(--fs-base);
    text-align: left;
    color: var(--fg-2);
    background: var(--bg-2);
  }
  .recs button:hover {
    background: var(--bg-3);
  }
  .recs button.on {
    color: var(--fg-1);
    background: var(--bg-3);
    box-shadow: inset 2px 0 0 var(--mint);
  }
  .recs span {
    font-size: var(--fs-sm);
    color: var(--fg-4);
  }
  .reading {
    display: flex;
    align-items: center;
    gap: 8px;
    margin: 0;
    padding: 6px 8px;
    font-size: var(--fs-base);
    color: var(--fg-2);
    background: var(--well);
    box-shadow: inset 2px 0 0 var(--mint);
  }
  .spin {
    width: 12px;
    height: 12px;
    border: 2px solid var(--mint-lo);
    border-top-color: var(--mint);
    border-radius: 50%;
    animation: spin var(--t-spin) linear infinite;
  }
  @keyframes spin {
    to {
      transform: rotate(360deg);
    }
  }
  .src {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    gap: 6px;
    font-size: var(--fs-base);
  }
  .tag {
    padding: 0 5px;
    font-size: var(--fs-xs);
    color: var(--mint-ink);
    background: var(--mint);
  }
  .by {
    color: var(--fg-3);
  }
  .seg {
    display: flex;
    gap: 2px;
    margin-left: auto;
  }
  .seg button {
    height: 24px;
    padding: 0 8px;
    font-size: var(--fs-base);
    color: var(--fg-3);
    background: var(--bg-2);
  }
  .seg button.on {
    color: var(--mint-ink);
    background: var(--mint);
    font-weight: 700;
  }
  .warn {
    margin: 0;
    font-size: var(--fs-base);
    padding: 6px 8px;
    color: var(--gold);
    background: rgb(var(--gold-rgb) / 0.08);
    box-shadow: inset 2px 0 0 var(--gold);
  }
  .desc {
    margin: 0;
    max-height: 120px;
    overflow-y: auto;
    padding: 8px 10px;
    font-size: var(--fs-base);
    line-height: 1.7;
    white-space: pre-line;
    user-select: text;
    color: var(--fg-2);
    background: var(--well);
    box-shadow: inset 2px 0 0 var(--mint-lo);
  }
  .nm {
    display: flex;
    align-items: center;
    gap: 8px;
    font-size: var(--fs-base);
    color: var(--fg-3);
  }
  .nm input {
    flex: 1;
    min-width: 0;
    height: 28px;
    padding: 0 8px;
    font-size: var(--fs-md);
    font-weight: 700;
    background: var(--bg-1);
    border: 0;
    outline: 0;
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .nm input:focus {
    box-shadow: inset 0 0 0 1px var(--mint-lo);
  }
  /* margin-top: auto：工作台里面板是右侧全高，内容少时底栏也贴底边 */
  /* 内容区自己滚；底部按钮栏固定在下面 */
  .scroll {
    display: flex;
    flex-direction: column;
    gap: 8px;
  }
  footer {
    flex: none;
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 10px 12px;
    border-top: 1px solid var(--line);
    background: var(--bg-1);
  }
  .sp {
    flex: 1;
  }
  .ghost {
    font-size: var(--fs-base);
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
    font-size: var(--fs-md);
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
