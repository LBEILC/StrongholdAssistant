<script lang="ts">
  import { plaza, type Comment, type Post } from '../lib/plaza.svelte'
  import Icon from './Icon.svelte'
  import { api } from '../lib/api'
  import { app } from '../lib/state.svelte'
  import { openMenu, type MenuEntry } from '../lib/menu.svelte'

  // 广场详情面板里的评论：只有一层，回复也排在同一列表里写「回复 某某」，新到旧。
  // 没登录给「登录」，没昵称就地起一个
  let { post }: { post: Post } = $props()

  let list = $state<Comment[]>([])
  let cursor = $state<string | null>(null)
  let loading = $state(true)
  let err = $state('')
  async function load(more = false) {
    loading = true
    err = ''
    try {
      const r = await plaza.comments(post.id, more ? cursor : null)
      list = more ? [...list, ...r.comments] : r.comments
      cursor = r.cursor
    } catch (e) {
      err = (e as Error).message
    } finally {
      loading = false
    }
  }
  $effect(() => {
    void post.id
    list = []
    plaza.check()
    load()
  })

  let body = $state('')
  let replyTo = $state<Comment | null>(null)
  let nick = $state('')
  let busy = $state(false)
  let box: HTMLTextAreaElement | undefined = $state()
  const len = $derived([...body.trim()].length)

  async function run(f: () => Promise<unknown>) {
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
  const send = () =>
    run(async () => {
      const c = await plaza.comment(post.id, body.trim(), replyTo?.id)
      list = [c, ...list]
      post.comments++
      body = ''
      replyTo = null
    })
  function reply(c: Comment) {
    replyTo = c
    box?.focus()
  }

  // 删除点两次：第一次变红「确认删除」，3 秒内再点才删
  let armed = $state<string | null>(null)
  let armTimer = 0
  function remove(c: Comment) {
    if (armed !== c.id) {
      armed = c.id
      clearTimeout(armTimer)
      armTimer = window.setTimeout(() => (armed = null), 3000)
      return
    }
    armed = null
    run(async () => {
      await plaza.deleteComment(c.id)
      list = list.filter((x) => x.id !== c.id)
      post.comments--
    })
  }

  // 右键一条评论：回复、复制、举报、删除（删除在菜单里点两次）
  function commentMenu(ev: MouseEvent, c: Comment) {
    const items: MenuEntry[] = []
    if (plaza.me?.nickname) items.push({ label: '回复', icon: 'reply', run: () => reply(c) })
    items.push({ label: '复制内容', icon: 'copy', run: () => api.copyText(c.body).then(() => app.showToast('已复制')) })
    if (c.author.id !== plaza.me?.id) items.push('sep', { label: '举报', icon: 'flag', tone: 'danger', run: () => (plaza.reporting = { target: { kind: 'comment', id: c.id }, what: `${c.author.nickname}：${c.body}` }) })
    if (c.canDelete) items.push('sep', { label: '删除', icon: 'trash', tone: 'danger', confirm: '再点一次删除', run: () => ((armed = c.id), remove(c)) })
    openMenu(ev, { title: c.author.nickname, sub: '评论', items })
  }

  function ago(iso: string) {
    const m = Math.floor((Date.now() - Date.parse(iso)) / 60000)
    if (m < 1) return '刚刚'
    if (m < 60) return `${m} 分钟前`
    if (m < 1440) return `${Math.floor(m / 60)} 小时前`
    if (m < 43200) return `${Math.floor(m / 1440)} 天前`
    return iso.slice(0, 10)
  }
</script>

<section class="cm">
  <div class="hd"><Icon name="chat" size={14} />评论<b class="term">{post.comments}</b></div>

  {#if !plaza.checked}
    <!-- 等登录状态 -->
  {:else if !plaza.me}
    <div class="gate">
      <span>登录后可以评论</span>
      <button class="sm" disabled={plaza.loggingIn} onclick={() => run(() => plaza.login())}>{plaza.loggingIn ? '等待登录…' : '登录'}</button>
    </div>
  {:else if !plaza.me.nickname}
    <div class="gate">
      <input bind:value={nick} maxlength="16" placeholder="先起个昵称（2～16 个字）" />
      <button class="sm" disabled={busy || [...nick.trim()].length < 2} onclick={() => run(() => plaza.setNickname(nick.trim()))}>保存</button>
    </div>
  {:else}
    <div class="compose">
      {#if replyTo}
        <div class="rt">
          <Icon name="reply" size={12} />回复 <b>{replyTo.author.nickname}</b>
          <button onclick={() => (replyTo = null)} aria-label="不回复了"><Icon name="close" size={12} /></button>
        </div>
      {/if}
      <textarea bind:this={box} bind:value={body} maxlength="300" placeholder="说说用这套阵容的体验，或者问问作者……" onkeydown={(e) => e.key === 'Enter' && e.ctrlKey && len && !busy && send()}></textarea>
      <div class="row">
        <span>{len} / 300 · Ctrl + Enter 发送</span>
        <button class="sm" disabled={!len || busy} onclick={send}>发表</button>
      </div>
    </div>
  {/if}

  {#if err}<p class="err">{err}</p>{/if}

  {#if list.length}
    <ul>
      {#each list as c (c.id)}
        <li oncontextmenu={(ev) => commentMenu(ev, c)}>
          <div class="top">
            <b class:me={c.author.id === plaza.me?.id}>{c.author.nickname}</b>
            {#if c.replyTo}<span class="to"><Icon name="reply" size={11} />{c.replyTo.nickname}</span>{/if}
            <span class="t">{ago(c.createdAt)}</span>
          </div>
          <p class="body">{c.body}</p>
          <div class="ops">
            {#if plaza.me?.nickname}<button onclick={() => reply(c)}><Icon name="reply" size={12} />回复</button>{/if}
            {#if c.author.id !== plaza.me?.id}<button onclick={() => (plaza.reporting = { target: { kind: 'comment', id: c.id }, what: `${c.author.nickname}：${c.body}` })}><Icon name="flag" size={12} />举报</button>{/if}
            {#if c.canDelete}<button class:armed={armed === c.id} onclick={() => remove(c)}><Icon name="trash" size={12} />{armed === c.id ? '确认删除' : '删除'}</button>{/if}
          </div>
        </li>
      {/each}
    </ul>
    {#if cursor}<button class="more" disabled={loading} onclick={() => load(true)}>{loading ? '加载中…' : '更多评论'}</button>{/if}
  {:else if !err}
    <p class="empty">{loading ? '加载中…' : '还没有评论。'}</p>
  {/if}
</section>

<style>
  .cm {
    display: flex;
    flex-direction: column;
    gap: 6px;
  }
  .hd {
    display: flex;
    align-items: center;
    gap: 5px;
    padding: 5px 8px;
    font-size: 12.5px;
    font-weight: 700;
    background: linear-gradient(90deg, var(--bg-3), transparent);
    box-shadow: inset 2px 0 0 var(--mint);
  }
  .hd b {
    font-weight: 400;
    font-size: 11px;
    color: var(--fg-4);
  }
  .gate {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 6px 8px;
    font-size: 12px;
    color: var(--fg-3);
    background: rgb(0 0 0 / 0.25);
  }
  .gate span {
    flex: 1;
  }
  .gate input {
    flex: 1;
    min-width: 0;
    height: 26px;
    padding: 0 8px;
    background: var(--bg-1);
    border: 0;
    outline: 0;
    box-shadow: inset 0 0 0 1px var(--line);
    user-select: text;
  }
  .sm {
    height: 26px;
    padding: 0 10px;
    font-size: 12px;
    font-weight: 700;
    color: var(--mint-ink);
    background: var(--mint);
    white-space: nowrap;
  }
  .sm:hover:not(:disabled) {
    background: var(--mint-hi);
  }
  .sm:disabled {
    color: var(--fg-4);
    background: var(--bg-3);
    cursor: default;
  }
  .rt {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    align-self: flex-start;
    margin-bottom: 4px;
    padding: 2px 2px 2px 6px;
    font-size: 11px;
    color: var(--fg-2);
    background: var(--bg-3);
    box-shadow: inset 2px 0 0 var(--mint);
  }
  .rt button {
    display: grid;
    place-items: center;
    width: 18px;
    height: 18px;
    color: var(--fg-3);
  }
  .compose {
    display: flex;
    flex-direction: column;
  }
  textarea {
    resize: none;
    field-sizing: content;
    min-height: 52px;
    max-height: 140px;
    padding: 6px 8px;
    font-size: 12px;
    line-height: 1.6;
    color: var(--fg-1);
    background: var(--bg-1);
    border: 0;
    outline: 0;
    box-shadow: inset 0 0 0 1px var(--line);
    user-select: text;
  }
  textarea:focus {
    box-shadow: inset 0 0 0 1px var(--mint-lo);
  }
  .row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    margin-top: 4px;
    font-size: 11px;
    color: var(--fg-4);
  }
  .err {
    margin: 0;
    font-size: 12px;
    color: #f2b8ba;
    padding: 6px 8px;
    background: var(--red-tint);
    box-shadow: inset 2px 0 0 var(--red);
  }
  ul {
    margin: 0;
    padding: 0;
    list-style: none;
  }
  li {
    padding: 7px 2px;
    border-bottom: 1px solid var(--line-soft);
  }
  .top {
    display: flex;
    align-items: baseline;
    gap: 6px;
    font-size: 12px;
  }
  .top b {
    color: var(--fg-1);
  }
  .top b.me {
    color: var(--mint);
  }
  .to {
    display: inline-flex;
    align-items: center;
    gap: 2px;
    font-size: 11px;
    color: var(--fg-3);
  }
  .t {
    margin-left: auto;
    font-size: 11px;
    color: var(--fg-4);
  }
  .body {
    margin: 3px 0;
    font-size: 12px;
    line-height: 1.6;
    white-space: pre-line;
    overflow-wrap: anywhere;
    user-select: text;
    color: var(--fg-2);
  }
  .ops {
    display: flex;
    gap: 10px;
  }
  .ops button {
    display: inline-flex;
    align-items: center;
    gap: 2px;
    font-size: 11px;
    color: var(--fg-4);
  }
  .ops button:hover {
    color: var(--fg-1);
  }
  .ops button.armed {
    color: #f2b8ba;
  }
  .more {
    align-self: flex-start;
    font-size: 12px;
    color: var(--mint);
  }
  .empty {
    margin: 0;
    font-size: 12px;
    color: var(--fg-4);
  }
</style>
