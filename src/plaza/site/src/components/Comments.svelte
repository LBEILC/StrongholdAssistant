<script lang="ts">
  import { api, type Comment, type ReportTarget } from '../api'
  import { link } from '../router.svelte'
  import { session, toast } from '../session.svelte'
  import { ago } from '../data'
  import Icon from './Icon.svelte'
  import { tick } from 'svelte'

  // 评论区：只有一层，回复也排在同一列表里，写「回复 某某」；新到旧
  let {
    postId,
    count,
    oncount,
    onreport,
  }: {
    postId: string
    count: number
    oncount: (delta: number) => void
    onreport: (target: ReportTarget, what: string) => void
  } = $props()

  const demo = $derived(import.meta.env.DEV && postId.startsWith('demo-'))
  let list = $state<Comment[]>([])
  let cursor = $state<string | null>(null)
  let loading = $state(true)
  let error = $state('')

  // 从站内消息点进来：地址带 #c-<评论 id>，滚到那条并闪一下（不在第一页就不管）
  let flash = $state<string | null>(null)
  async function focusHash() {
    const id = location.hash.startsWith('#c-') ? location.hash.slice(3) : ''
    if (!id || !list.some((c) => c.id === id)) return
    flash = id
    await tick()
    document.getElementById('c-' + id)?.scrollIntoView({ block: 'center', behavior: 'smooth' })
    setTimeout(() => flash === id && (flash = null), 2400)
  }

  async function load(more = false) {
    loading = true
    error = ''
    try {
      const r = demo ? { comments: (await import('../demo')).demoComments.map((c) => ({ ...c, postId })), cursor: null } : await api.comments(postId, more ? cursor : null)
      list = more ? [...list, ...r.comments] : r.comments
      cursor = r.cursor
      if (!more) focusHash()
    } catch (e) {
      error = (e as Error).message
    } finally {
      loading = false
    }
  }
  $effect(() => {
    void postId
    load()
  })

  // 写评论
  let body = $state('')
  let replyTo = $state<Comment | null>(null)
  let sending = $state(false)
  let box: HTMLTextAreaElement | undefined = $state()
  const len = $derived([...body.trim()].length)
  async function send() {
    sending = true
    try {
      const c = await api.comment(postId, body.trim(), replyTo?.id)
      list = [c, ...list]
      oncount(1)
      body = ''
      replyTo = null
    } catch (e) {
      toast.show((e as Error).message, true)
    } finally {
      sending = false
    }
  }
  function reply(c: Comment) {
    replyTo = c
    box?.focus()
  }

  // 删除点两次：第一次变红「确认删除」，3 秒内再点才删
  let armed = $state<string | null>(null)
  let armTimer = 0
  async function remove(c: Comment) {
    if (armed !== c.id) {
      armed = c.id
      clearTimeout(armTimer)
      armTimer = window.setTimeout(() => (armed = null), 3000)
      return
    }
    armed = null
    try {
      await api.deleteComment(c.id)
      list = list.filter((x) => x.id !== c.id)
      oncount(-1)
    } catch (e) {
      toast.show((e as Error).message, true)
    }
  }
  const here = () => encodeURIComponent(location.pathname)
</script>

<section>
  <h2 class="sec-h">评论<small>Comments · {count}</small></h2>

  {#if !session.ready}
    <!-- 等登录状态 -->
  {:else if !session.me}
    <p class="gate"><a href="/login?next={here()}" use:link>登录</a>后可以评论。</p>
  {:else if !session.me.nickname}
    <p class="gate">先到<a href="/me" use:link>「我的」</a>起个昵称，再来评论。</p>
  {:else}
    <div class="compose">
      {#if replyTo}
        <div class="rt">
          <Icon name="reply" size={13} />回复 <b>{replyTo.author.nickname}</b>
          <button onclick={() => (replyTo = null)} title="不回复了"><Icon name="close" size={13} /></button>
        </div>
      {/if}
      <textarea bind:this={box} bind:value={body} maxlength="300" placeholder="说说你用这套阵容的体验，或者问问作者……" onkeydown={(e) => e.key === 'Enter' && (e.ctrlKey || e.metaKey) && len && !sending && send()}></textarea>
      <div class="row">
        <span class="muted">{len} / 300 · Ctrl + Enter 发送</span>
        <button class="btn primary sm" disabled={!len || sending} onclick={send}>{sending ? '发送中…' : '发表'}</button>
      </div>
    </div>
  {/if}

  {#if error}
    <p class="err">{error}</p>
  {:else if !list.length}
    <p class="muted empty">{loading ? '加载中…' : '还没有评论。'}</p>
  {:else}
    <ul>
      {#each list as c (c.id)}
        <li id="c-{c.id}" class:flash={flash === c.id}>
          <div class="top">
            <b class:me={c.author.id === session.me?.id}>{c.author.nickname}</b>
            {#if c.replyTo}<span class="to"><Icon name="reply" size={12} />{c.replyTo.nickname}</span>{/if}
            <span class="t">{ago(c.createdAt)}</span>
          </div>
          <p class="body">{c.body}</p>
          <div class="ops">
            {#if session.me?.nickname}<button onclick={() => reply(c)}><Icon name="reply" size={13} />回复</button>{/if}
            {#if c.author.id !== session.me?.id}<button onclick={() => onreport({ kind: 'comment', id: c.id }, `${c.author.nickname}：${c.body}`)}><Icon name="flag" size={13} />举报</button>{/if}
            {#if c.canDelete}<button class:armed={armed === c.id} onclick={() => remove(c)}><Icon name="trash" size={13} />{armed === c.id ? '确认删除' : '删除'}</button>{/if}
          </div>
        </li>
      {/each}
    </ul>
    {#if cursor}<button class="btn ghost sm more" disabled={loading} onclick={() => load(true)}>{loading ? '加载中…' : '更多评论'}</button>{/if}
  {/if}
</section>

<style>
  .gate {
    margin: 0 0 12px;
    padding: 12px 14px;
    color: var(--fg-3);
    background: var(--bg-1);
  }
  .gate a {
    color: var(--mint);
  }
  .compose {
    margin-bottom: 14px;
  }
  .rt {
    display: inline-flex;
    align-items: center;
    gap: 5px;
    margin-bottom: 6px;
    padding: 3px 4px 3px 8px;
    font-size: 12px;
    color: var(--fg-2);
    background: var(--bg-2);
    box-shadow: inset 2px 0 0 var(--mint);
  }
  .rt button {
    display: grid;
    place-items: center;
    width: 20px;
    height: 20px;
    color: var(--fg-3);
  }
  .rt button:hover {
    color: var(--fg-1);
  }
  textarea {
    width: 100%;
    resize: none;
    field-sizing: content;
    min-height: 72px;
    max-height: 240px;
    line-height: 1.6;
  }
  .row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 8px;
    margin-top: 6px;
    font-size: 12px;
  }
  .empty {
    margin: 4px 0 0;
  }
  ul {
    margin: 0;
    padding: 0;
    list-style: none;
  }
  li.flash {
    background: color-mix(in srgb, var(--mint) 14%, transparent);
    transition-duration: 0ms;
  }
  li {
    transition: background 600ms var(--ease-out);
    padding: 12px 2px;
    border-bottom: 1px solid var(--line-soft);
  }
  .top {
    display: flex;
    align-items: baseline;
    gap: 8px;
    font-size: 13px;
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
    gap: 3px;
    font-size: 12px;
    color: var(--fg-3);
  }
  .t {
    margin-left: auto;
    font-size: 12px;
    color: var(--fg-4);
  }
  .body {
    margin: 6px 0 4px;
    white-space: pre-line;
    line-height: 1.7;
    color: var(--fg-2);
    overflow-wrap: anywhere;
  }
  .ops {
    display: flex;
    gap: 12px;
  }
  .ops button {
    display: inline-flex;
    align-items: center;
    gap: 3px;
    font-size: 12px;
    color: var(--fg-4);
  }
  .ops button:hover {
    color: var(--fg-1);
  }
  .ops button.armed {
    color: #f2b8ba;
  }
  .more {
    margin-top: 10px;
  }
</style>
