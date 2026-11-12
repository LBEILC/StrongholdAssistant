<script lang="ts">
  import { api, REPORT_REASONS, type AdminComment, type AdminPost, type AdminReport, type AdminUser, type ReportReason } from '../api'
  import { link } from '../router.svelte'
  import { toast } from '../session.svelte'
  import { ago } from '../data'
  import Icon from '../components/Icon.svelte'

  // 后台：Cloudflare Access 保护（/admin 和 /api/admin/*），能打开这页说明已经过了 Access 登录
  let tab = $state<'reports' | 'posts' | 'comments' | 'users'>('reports')

  // 危险操作点两次：第一次按钮变红「确认…」，3 秒内再点才执行
  let armed = $state<string | null>(null)
  let armTimer = 0
  function confirmTwice(key: string, run: () => void) {
    if (armed === key) {
      armed = null
      return run()
    }
    armed = key
    clearTimeout(armTimer)
    armTimer = window.setTimeout(() => (armed = null), 3000)
  }

  // 帖子
  let status = $state<AdminPost['status'] | null>(null)
  let posts = $state<AdminPost[]>([])
  let cursor = $state<string | null>(null)
  let loading = $state(false)
  let error = $state('')
  async function loadPosts(more = false) {
    loading = true
    error = ''
    try {
      const r = await api.admin.posts(status, more ? cursor : null)
      posts = more ? [...posts, ...r.posts] : r.posts
      cursor = r.cursor
    } catch (e) {
      error = (e as Error).message
    } finally {
      loading = false
    }
  }
  $effect(() => {
    void status
    loadPosts()
  })
  async function patch(p: AdminPost, change: { status?: AdminPost['status']; featured?: boolean }) {
    try {
      const r = await api.admin.updatePost(p.id, change)
      posts = posts.map((x) => (x.id === p.id ? r : x))
    } catch (e) {
      toast.show((e as Error).message, true)
    }
  }

  // 举报：按目标归并，待处理的在前
  let rStatus = $state<AdminReport['status']>('open')
  let reports = $state<AdminReport[]>([])
  let rCursor = $state<string | null>(null)
  let openCount = $state(0)
  let rLoading = $state(false)
  let rError = $state('')
  async function loadReports(more = false) {
    rLoading = true
    rError = ''
    try {
      const r = await api.admin.reports(rStatus, more ? rCursor : null)
      reports = more ? [...reports, ...r.reports] : r.reports
      rCursor = r.cursor
      openCount = r.openCount
    } catch (e) {
      rError = (e as Error).message
    } finally {
      rLoading = false
    }
  }
  $effect(() => {
    void rStatus
    loadReports()
  })
  const rKey = (r: AdminReport) => `${r.target.kind}:${r.target.id}`
  async function resolve(r: AdminReport, action: 'dismiss' | 'hide' | 'delete') {
    try {
      await api.admin.resolveReport(r.target, action)
      reports = reports.filter((x) => rKey(x) !== rKey(r))
      if (rStatus === 'open') openCount--
      toast.show({ dismiss: '已标记为不处理。', hide: '已下架。', delete: '已删除。' }[action])
    } catch (e) {
      toast.show((e as Error).message, true)
    }
  }
  const reasonList = (r: AdminReport) => (Object.entries(r.reasons) as [ReportReason, number][]).sort((a, b) => b[1] - a[1])

  // 评论
  let cStatus = $state<AdminComment['status'] | null>(null)
  let comments = $state<AdminComment[]>([])
  let cCursor = $state<string | null>(null)
  let cLoading = $state(false)
  let cError = $state('')
  async function loadComments(more = false) {
    cLoading = true
    cError = ''
    try {
      const r = await api.admin.comments(cStatus, more ? cCursor : null)
      comments = more ? [...comments, ...r.comments] : r.comments
      cCursor = r.cursor
    } catch (e) {
      cError = (e as Error).message
    } finally {
      cLoading = false
    }
  }
  $effect(() => {
    if (tab !== 'comments') return
    void cStatus
    loadComments()
  })
  async function patchComment(c: AdminComment, status: AdminComment['status']) {
    try {
      await api.admin.updateComment(c.id, status)
      comments = comments.map((x) => (x.id === c.id ? { ...x, status } : x))
    } catch (e) {
      toast.show((e as Error).message, true)
    }
  }

  // 账号
  let q = $state('')
  let users = $state<AdminUser[] | null>(null)
  async function search() {
    try {
      users = await api.admin.users(q.trim())
    } catch (e) {
      toast.show((e as Error).message, true)
    }
  }
  async function ban(u: AdminUser) {
    try {
      const r = await api.admin.setBanned(u.id, !u.banned)
      users = users!.map((x) => (x.id === u.id ? r : x))
      toast.show(r.banned ? '已封禁。' : '已解封（帖子不会自动恢复）。')
    } catch (e) {
      toast.show((e as Error).message, true)
    }
  }
  const statusName = { visible: '可见', hidden: '已下架', deleted: '已删除' }
</script>

<div class="in">
  <div class="hd">
    <h1><Icon name="shield" size={22} />后台</h1>
    <div class="tabs">
      <button class:on={tab === 'reports'} onclick={() => (tab = 'reports')}>举报{#if openCount}<em class="badge">{openCount}</em>{/if}</button>
      <button class:on={tab === 'posts'} onclick={() => (tab = 'posts')}>阵容</button>
      <button class:on={tab === 'comments'} onclick={() => (tab = 'comments')}>评论</button>
      <button class:on={tab === 'users'} onclick={() => (tab = 'users')}>账号</button>
    </div>
  </div>

  {#if tab === 'reports'}
    <div class="bar">
      <button class="chip" class:on={rStatus === 'open'} onclick={() => (rStatus = 'open')}>待处理</button>
      <button class="chip" class:on={rStatus === 'resolved'} onclick={() => (rStatus = 'resolved')}>已处理</button>
    </div>
    {#if rError}
      <p class="err">{rError}</p>
    {:else}
      <div class="reports">
        {#each reports as r (rKey(r))}
          <article class="rp">
            <div class="rh">
              <span class="kind">{r.target.kind === 'post' ? '阵容' : '评论'}</span>
              <b class="cnt">{r.count} 人举报</b>
              <span class="sub">最近 {ago(r.lastAt)}{#if r.firstAt !== r.lastAt} · 最早 {ago(r.firstAt)}{/if}</span>
            </div>
            <div class="rbody">
              {#if r.comment}
                <div class="quote">
                  <div class="sub">{r.comment.author.nickname} · {r.comment.authorEmailMasked}{#if r.comment.status !== 'visible'} · {statusName[r.comment.status]}{/if}</div>
                  <p>{r.comment.body}</p>
                </div>
                <div class="sub">在 <a href="/p/{r.post.id}" use:link class="pn">{r.post.lineup.name}</a> 下</div>
              {:else}
                <a href="/p/{r.post.id}" use:link class="pn">{r.post.lineup.name}</a>
                <span class="sub"> · {r.post.author.nickname} · {r.post.authorEmailMasked}{#if r.post.status !== 'visible'} · {statusName[r.post.status]}{/if}</span>
                {#if r.post.description}<div class="pd">{r.post.description}</div>{/if}
              {/if}
            </div>
            <div class="why">
              {#each reasonList(r) as [k, n] (k)}<span class="rz">{REPORT_REASONS[k]} <b>{n}</b></span>{/each}
            </div>
            {#if r.details.length}
              <ul class="dt">{#each r.details as d, i (i)}<li>{d}</li>{/each}</ul>
            {/if}
            {#if r.status === 'open'}
              <div class="ops">
                <button class="btn sm ghost" onclick={() => resolve(r, 'dismiss')}><Icon name="check" size={12} />不处理</button>
                <button class="btn sm ghost" onclick={() => resolve(r, 'hide')}><Icon name="eyeOff" size={12} />下架</button>
                <button class="btn sm" class:ghost={armed !== `rdel:${rKey(r)}`} class:danger={armed === `rdel:${rKey(r)}`} onclick={() => confirmTwice(`rdel:${rKey(r)}`, () => resolve(r, 'delete'))}><Icon name="trash" size={12} />{armed === `rdel:${rKey(r)}` ? '确认删除' : '删除'}</button>
              </div>
            {/if}
          </article>
        {:else}
          <p class="empty">{rLoading ? '加载中…' : rStatus === 'open' ? '没有待处理的举报。' : '没有已处理的举报。'}</p>
        {/each}
      </div>
      {#if rCursor}<button class="btn ghost more" disabled={rLoading} onclick={() => loadReports(true)}>加载更多</button>{/if}
    {/if}
  {:else if tab === 'comments'}
    <div class="bar">
      {#each [[null, '全部'], ['visible', '可见'], ['hidden', '已下架'], ['deleted', '已删除']] as [v, label] (label)}
        <button class="chip" class:on={cStatus === v} onclick={() => (cStatus = v as AdminComment['status'] | null)}>{label}</button>
      {/each}
    </div>
    {#if cError}
      <p class="err">{cError}</p>
    {:else}
      <table>
        <thead><tr><th>评论</th><th>阵容</th><th>作者</th><th>状态</th><th>时间</th><th></th></tr></thead>
        <tbody>
          {#each comments as c (c.id)}
            <tr class:dim={c.status !== 'visible'}>
              <td>
                {#if c.replyTo}<div class="sub">回复 {c.replyTo.nickname}</div>{/if}
                <div class="cb">{c.body}</div>
              </td>
              <td><a href="/p/{c.postId}" use:link class="pn">{c.postTitle}</a></td>
              <td>{c.author.nickname}<div class="sub">{c.authorEmailMasked}</div></td>
              <td>{statusName[c.status]}</td>
              <td class="sub">{ago(c.createdAt)}</td>
              <td class="ops">
                {#if c.status === 'visible'}
                  <button class="btn sm ghost" onclick={() => patchComment(c, 'hidden')}><Icon name="eyeOff" size={12} />下架</button>
                {:else}
                  <button class="btn sm ghost" onclick={() => patchComment(c, 'visible')}><Icon name="eye" size={12} />恢复</button>
                {/if}
                {#if c.status !== 'deleted'}
                  <button class="btn sm" class:ghost={armed !== `cdel:${c.id}`} class:danger={armed === `cdel:${c.id}`} onclick={() => confirmTwice(`cdel:${c.id}`, () => patchComment(c, 'deleted'))}><Icon name="trash" size={12} />{armed === `cdel:${c.id}` ? '确认删除' : '删除'}</button>
                {/if}
              </td>
            </tr>
          {:else}
            <tr><td colspan="6" class="empty">{cLoading ? '加载中…' : '没有评论。'}</td></tr>
          {/each}
        </tbody>
      </table>
      {#if cCursor}<button class="btn ghost more" disabled={cLoading} onclick={() => loadComments(true)}>加载更多</button>{/if}
    {/if}
  {:else if tab === 'posts'}
    <div class="bar">
      {#each [[null, '全部'], ['visible', '可见'], ['hidden', '已下架'], ['deleted', '已删除']] as [v, label] (label)}
        <button class="chip" class:on={status === v} onclick={() => (status = v as AdminPost['status'] | null)}>{label}</button>
      {/each}
    </div>
    {#if error}
      <p class="err">{error}</p>
    {:else}
      <table>
        <thead><tr><th>阵容</th><th>作者</th><th>状态</th><th>赞 / 导入</th><th>时间</th><th></th></tr></thead>
        <tbody>
          {#each posts as p (p.id)}
            <tr class:dim={p.status !== 'visible'}>
              <td>
                <a href="/p/{p.id}" use:link class="pn">{p.lineup.name}</a>
                {#if p.featured}<span class="feat">精选</span>{/if}
                {#if p.description}<div class="pd">{p.description}</div>{/if}
              </td>
              <td>{p.author.nickname}<div class="sub">{p.authorEmailMasked}</div></td>
              <td>{statusName[p.status]}</td>
              <td class="num">{p.likes} / {p.imports}</td>
              <td class="sub">{ago(p.createdAt)}</td>
              <td class="ops">
                <button class="btn sm ghost" onclick={() => patch(p, { featured: !p.featured })}><Icon name="star" size={12} />{p.featured ? '取消精选' : '精选'}</button>
                {#if p.status === 'visible'}
                  <button class="btn sm ghost" onclick={() => patch(p, { status: 'hidden' })}><Icon name="eyeOff" size={12} />下架</button>
                {:else}
                  <button class="btn sm ghost" onclick={() => patch(p, { status: 'visible' })}><Icon name="eye" size={12} />恢复</button>
                {/if}
                {#if p.status !== 'deleted'}
                  <button class="btn sm" class:ghost={armed !== `del:${p.id}`} class:danger={armed === `del:${p.id}`} onclick={() => confirmTwice(`del:${p.id}`, () => patch(p, { status: 'deleted' }))}><Icon name="trash" size={12} />{armed === `del:${p.id}` ? '确认删除' : '删除'}</button>
                {/if}
              </td>
            </tr>
          {:else}
            <tr><td colspan="6" class="empty">{loading ? '加载中…' : '没有阵容。'}</td></tr>
          {/each}
        </tbody>
      </table>
      {#if cursor}<button class="btn ghost more" disabled={loading} onclick={() => loadPosts(true)}>加载更多</button>{/if}
    {/if}
  {:else}
    <div class="bar">
      <input bind:value={q} placeholder="按昵称或邮箱片段搜索" onkeydown={(e) => e.key === 'Enter' && search()} />
      <button class="btn primary sm" onclick={search}>搜索</button>
    </div>
    {#if users}
      <table>
        <thead><tr><th>昵称</th><th>邮箱</th><th>帖子</th><th>注册</th><th>状态</th><th></th></tr></thead>
        <tbody>
          {#each users as u (u.id)}
            <tr class:dim={u.banned}>
              <td>{u.nickname ?? '（没起昵称）'}</td>
              <td class="sub">{u.emailMasked}</td>
              <td class="num">{u.posts}</td>
              <td class="sub">{ago(u.createdAt)}</td>
              <td>{u.banned ? '已封禁' : '正常'}</td>
              <td class="ops">
                {#if u.banned}
                  <button class="btn sm" onclick={() => ban(u)}><Icon name="forbid" size={12} />解封</button>
                {:else}
                  <button class="btn sm" class:danger={armed === `ban:${u.id}`} title="封禁后他的帖子和评论全部下架，登录凭据失效" onclick={() => confirmTwice(`ban:${u.id}`, () => ban(u))}><Icon name="forbid" size={12} />{armed === `ban:${u.id}` ? '确认封禁' : '封禁'}</button>
                {/if}
              </td>
            </tr>
          {:else}
            <tr><td colspan="6" class="empty">没有找到。</td></tr>
          {/each}
        </tbody>
      </table>
    {/if}
  {/if}
</div>

<style>
  .in {
    width: min(1200px, 100% - 32px);
    margin: 0 auto;
    padding-top: 24px;
  }
  .hd {
    display: flex;
    align-items: center;
    gap: 20px;
    margin-bottom: 16px;
  }
  h1 {
    display: flex;
    align-items: center;
    gap: 8px;
    margin: 0;
    font-size: 22px;
    color: var(--gold);
  }
  .tabs {
    display: flex;
    gap: 2px;
  }
  .tabs button,
  .chip {
    height: 30px;
    padding: 0 14px;
    color: var(--fg-2);
    background: var(--bg-2);
  }
  .tabs button.on,
  .chip.on {
    color: var(--mint-ink);
    background: var(--mint);
    font-weight: 700;
  }
  .bar {
    display: flex;
    gap: 6px;
    margin-bottom: 12px;
  }
  .bar input {
    width: 280px;
  }
  table {
    width: 100%;
    border-collapse: collapse;
    font-size: 13px;
  }
  th {
    text-align: left;
    font-weight: 600;
    font-size: 12px;
    color: var(--fg-3);
    padding: 8px;
    border-bottom: 1px solid var(--line);
  }
  td {
    padding: 8px;
    vertical-align: top;
    border-bottom: 1px solid var(--line-soft);
  }
  tr.dim td {
    color: var(--fg-4);
  }
  .pn {
    font-weight: 600;
    color: var(--fg-1);
  }
  .pn:hover {
    color: var(--mint);
  }
  .pd {
    max-width: 420px;
    margin-top: 2px;
    font-size: 12px;
    color: var(--fg-3);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .feat {
    margin-left: 6px;
    font-size: 11px;
    padding: 0 5px;
    color: var(--gold-ink);
    background: var(--gold);
  }
  .sub {
    font-size: 12px;
    color: var(--fg-3);
  }
  .num {
    font-family: var(--font-term);
  }
  .ops {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
    justify-content: flex-end;
  }
  .empty {
    padding: 40px;
    text-align: center;
    color: var(--fg-3);
  }
  .more {
    margin-top: 12px;
  }
  .badge {
    margin-left: 6px;
    padding: 0 5px;
    font-style: normal;
    font-size: 11px;
    font-family: var(--font-term);
    color: #fff;
    background: var(--red);
  }
  .reports {
    display: grid;
    gap: 8px;
  }
  .rp {
    padding: 12px 14px;
    background: var(--bg-1);
    box-shadow: inset 2px 0 0 var(--red);
  }
  .rh {
    display: flex;
    align-items: baseline;
    gap: 10px;
    margin-bottom: 8px;
  }
  .kind {
    padding: 0 6px;
    font-size: 11px;
    color: var(--fg-2);
    background: var(--bg-3);
  }
  .cnt {
    color: #f2b8ba;
  }
  .rbody {
    margin-bottom: 8px;
  }
  .quote {
    margin-bottom: 4px;
    padding: 6px 10px;
    background: var(--bg-2);
  }
  .quote p,
  .cb {
    margin: 2px 0 0;
    max-width: 640px;
    white-space: pre-line;
    overflow-wrap: anywhere;
    color: var(--fg-2);
  }
  .why {
    display: flex;
    flex-wrap: wrap;
    gap: 6px;
    margin-bottom: 6px;
  }
  .rz {
    padding: 2px 8px;
    font-size: 12px;
    color: var(--fg-2);
    background: var(--bg-2);
  }
  .rz b {
    margin-left: 2px;
    font-family: var(--font-term);
    color: var(--fg-1);
  }
  .dt {
    margin: 0 0 8px;
    padding-left: 18px;
    font-size: 12px;
    color: var(--fg-3);
  }
  .rp .ops {
    justify-content: flex-start;
  }
</style>
