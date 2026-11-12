<script lang="ts">
  import { api, type AdminPost, type AdminUser } from '../api'
  import { link } from '../router.svelte'
  import { toast } from '../session.svelte'
  import { ago } from '../data'
  import Icon from '../components/Icon.svelte'

  // 后台：Cloudflare Access 保护（/admin 和 /api/admin/*），能打开这页说明已经过了 Access 登录
  let tab = $state<'posts' | 'users'>('posts')

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
      <button class:on={tab === 'posts'} onclick={() => (tab = 'posts')}>阵容</button>
      <button class:on={tab === 'users'} onclick={() => (tab = 'users')}>账号</button>
    </div>
  </div>

  {#if tab === 'posts'}
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
                  <button class="btn sm" class:danger={armed === `ban:${u.id}`} title="封禁后他的帖子全部下架，登录凭据失效" onclick={() => confirmTwice(`ban:${u.id}`, () => ban(u))}><Icon name="forbid" size={12} />{armed === `ban:${u.id}` ? '确认封禁' : '封禁'}</button>
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
</style>
