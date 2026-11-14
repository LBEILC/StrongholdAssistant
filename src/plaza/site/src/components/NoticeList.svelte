<script lang="ts">
  import { api, type Notice } from '../api'
  import { link } from '../router.svelte'
  import { session } from '../session.svelte'
  import { ago } from '../data'
  import Icon from './Icon.svelte'

  // 站内消息（v0.9.3）：打开就把列表里的都标成已读；这次打开时还没读的几条保持高亮。
  // 点一条进帖子页，评论类带 #c-<评论 id> 让评论区定位过去
  let list = $state<Notice[] | null>(null)
  let cursor = $state<string | null>(null)
  let loading = $state(false)
  let error = $state('')
  let fresh = $state(new Set<string>())

  async function load(more = false) {
    loading = true
    error = ''
    try {
      const r = await api.notices(more ? cursor : null)
      list = more ? [...(list ?? []), ...r.notices] : r.notices
      cursor = r.cursor
      const unread = r.notices.filter((n) => !n.read)
      if (unread.length) fresh = new Set([...fresh, ...unread.map((n) => n.id)])
      if (!more && unread.length) api.readNotices(r.notices[0].createdAt).then(() => (session.unread = 0), () => {})
      else if (!more) session.unread = 0
    } catch (e) {
      error = (e as Error).message
    } finally {
      loading = false
    }
  }
  load()

  const ICON = { comment: 'chat', reply: 'reply', favorite: 'star', like: 'heartFill', featured: 'award', post_hidden: 'eyeOff', comment_hidden: 'eyeOff' } as const
  function who(n: Notice) {
    const names = n.actors.map((a) => a.nickname || '一位玩家')
    return n.actorCount > names.length ? `${names.join('、')} 等 ${n.actorCount} 人` : names.join('、')
  }
  function says(n: Notice) {
    switch (n.kind) {
      case 'comment': return `${who(n)} 评论了你的阵容`
      case 'reply': return `${who(n)} 回复了你的评论`
      case 'favorite': return `${who(n)} 收藏了你的阵容`
      case 'like': return `${who(n)} 赞了你的阵容`
      case 'featured': return '你的阵容被设为精选'
      case 'post_hidden': return '你的阵容被管理员下架了'
      case 'comment_hidden': return '你的评论被管理员下架了'
    }
  }
  const href = (n: Notice) => `/p/${encodeURIComponent(n.post.id)}` + ((n.kind === 'comment' || n.kind === 'reply') && n.comment ? `#c-${n.comment.id}` : '')
</script>

{#if error && !list}
  <p class="err">{error}</p>
{:else if !list}
  <p class="muted">加载中…</p>
{:else if !list.length}
  <p class="muted">还没有消息。别人评论、回复、收藏、点赞你的阵容时会出现在这里；在卫戍协议助手里也能看。</p>
{:else}
  <p class="muted small">只保留最近 90 天。</p>
  <ul class="nl">
    {#each list as n (n.id)}
      <li>
        <a class="n k-{n.kind}" class:new={fresh.has(n.id)} href={href(n)} use:link>
          <span class="ni"><Icon name={ICON[n.kind]} size={16} /></span>
          <span class="nb">
            <span class="ns">{says(n)}<span class="np">「{n.post.name}」</span></span>
            {#if n.comment}<span class="nc">{n.comment.body}</span>{/if}
          </span>
          <span class="nt">{ago(n.createdAt)}</span>
        </a>
      </li>
    {/each}
  </ul>
  {#if error}<p class="err">{error}</p>{/if}
  {#if cursor}<button class="btn ghost sm more" disabled={loading} onclick={() => load(true)}>{loading ? '加载中…' : '更早的消息'}</button>{/if}
{/if}

<style>
  .nl {
    display: flex;
    flex-direction: column;
    margin: 0;
    padding: 0;
    list-style: none;
    border-top: 1px solid var(--line-soft);
  }
  .n {
    display: flex;
    align-items: flex-start;
    gap: 12px;
    padding: 12px 10px;
    color: inherit;
    text-decoration: none;
    border-bottom: 1px solid var(--line-soft);
  }
  .n:hover {
    background: var(--bg-2);
  }
  .n.new {
    background: color-mix(in srgb, var(--mint) 8%, transparent);
    box-shadow: inset 2px 0 0 var(--mint);
  }
  .ni {
    display: grid;
    place-items: center;
    width: 30px;
    height: 30px;
    flex: none;
    color: var(--mint);
    background: var(--bg-2);
    border-radius: var(--r-1);
  }
  .k-like .ni {
    color: var(--like, #ff6b8b);
  }
  .k-favorite .ni,
  .k-featured .ni {
    color: var(--gold);
  }
  .k-post_hidden .ni,
  .k-comment_hidden .ni {
    color: var(--red-fg);
  }
  .nb {
    display: flex;
    flex-direction: column;
    gap: 4px;
    flex: 1;
    min-width: 0;
  }
  .ns {
    color: var(--fg-1);
  }
  .np {
    margin-left: 4px;
    color: var(--fg-3);
  }
  .nc {
    padding: 6px 10px;
    font-size: 13px;
    line-height: 1.7;
    color: var(--fg-2);
    white-space: pre-line;
    background: var(--well, var(--bg-1));
    box-shadow: inset 2px 0 0 var(--line);
    display: -webkit-box;
    -webkit-line-clamp: 3;
    line-clamp: 3;
    -webkit-box-orient: vertical;
    overflow: hidden;
  }
  .nt {
    flex: none;
    font-size: 12px;
    color: var(--fg-4);
  }
  .more {
    margin: 12px auto 0;
    display: flex;
  }
</style>
