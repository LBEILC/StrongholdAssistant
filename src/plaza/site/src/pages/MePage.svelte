<script lang="ts">
  import { api, type CloudLineup, type Post } from '../api'
  import { router, link } from '../router.svelte'
  import { session, toast } from '../session.svelte'
  import { ago } from '../data'
  import { encode } from '../share'
  import OpAvatar from '../components/OpAvatar.svelte'
  import BondChip from '../components/BondChip.svelte'
  import PostCard from '../components/PostCard.svelte'
  import Icon from '../components/Icon.svelte'
  import NoticeList from '../components/NoticeList.svelte'

  // 我的：昵称、我的阵容（云同步，v0.7.0）/ 我发布的（含旧赛季）/ 收藏 / 消息（v0.9.3），退出登录、注销账号
  $effect(() => {
    if (session.ready && !session.me) router.go('/login?next=/me')
  })

  let nickname = $state('')
  let editingName = $state(false)
  let busy = $state(false)
  let posts = $state<Post[] | null>(null)
  let error = $state('')

  type Tab = 'mine' | 'posts' | 'fav' | 'notices'
  let tab = $state<Tab>((new URLSearchParams(location.search).get('tab') as Tab) || 'mine')
  let mine = $state<CloudLineup[] | null>(null)
  let favs = $state<Post[] | null>(null)
  $effect(() => {
    const me = session.me
    if (!me) return
    if (!me.nickname) editingName = true
    api
      .posts({ author: me.id, season: 'all', sort: 'new' })
      .then((r) => (posts = r.posts))
      .catch((e) => (error = e.message))
    api
      .myLineups()
      .then((r) => (mine = r.sort((a, b) => b.updatedAt.localeCompare(a.updatedAt))))
      .catch((e) => (error = e.message))
    api
      .posts({ favorite: true })
      .then((r) => (favs = r.posts))
      .catch((e) => (error = e.message))
  })

  // 我的阵容：复制分享码、删除（点两次）
  async function copyMine(c: CloudLineup) {
    try {
      await navigator.clipboard.writeText(`【卫戍协议助手阵容】${c.data!.name} ${encode(c.data!)}`)
      toast.show('已复制分享码。')
    } catch {
      toast.show('复制失败，这套阵容可能用到了当前数据里没有的干员或装备。', true)
    }
  }
  let armDel = $state<string | null>(null)
  async function removeMine(c: CloudLineup) {
    if (armDel !== c.id) return (armDel = c.id)
    armDel = null
    try {
      await api.sync({ since: new Date().toISOString(), lineups: [{ id: c.id, data: null, updatedAt: new Date().toISOString(), deleted: true }] })
      mine = mine!.filter((x) => x.id !== c.id)
      toast.show('已删除。助手同步后也会删掉。')
    } catch (e) {
      toast.show((e as Error).message, true)
    }
  }
  const tabs: { v: Tab; label: string; n: () => number | null }[] = [
    { v: 'mine', label: '我的阵容', n: () => mine?.length ?? null },
    { v: 'posts', label: '我发布的', n: () => posts?.length ?? null },
    { v: 'fav', label: '收藏', n: () => favs?.length ?? null },
    { v: 'notices', label: '消息', n: () => session.unread || null },
  ]

  async function saveName() {
    busy = true
    try {
      session.me = await api.setNickname(nickname.trim())
      editingName = false
      toast.show('昵称已更新。')
    } catch (e) {
      toast.show((e as Error).message, true)
    } finally {
      busy = false
    }
  }

  async function logout() {
    await session.logout()
    toast.show('已退出登录。')
    router.go('/plaza')
  }

  // 登录密码：可选。已有密码时改 / 删要输当前密码；30 分钟内用验证码登录的可以不填（忘了密码的路径）
  let pwMode = $state<'none' | 'set' | 'delete'>('none')
  let pwCur = $state('')
  let pw1 = $state('')
  let pw2 = $state('')
  let pwErr = $state('')
  const pwOk = $derived(pw1.length >= 8 && pw1.length <= 64 && pw1 === pw2)
  const closePw = () => ((pwMode = 'none'), (pwCur = pw1 = pw2 = pwErr = ''))
  async function savePw() {
    busy = true
    pwErr = ''
    try {
      if (pwMode === 'delete') {
        await api.deletePassword(pwCur || undefined)
        session.me = await api.me()
      } else session.me = await api.setPassword(pw1, pwCur || undefined)
      toast.show(pwMode === 'delete' ? '已删除登录密码，以后用验证码登录。' : '登录密码已保存，其他设备上的登录已退出。')
      closePw()
    } catch (e) {
      pwErr = (e as Error).message
    } finally {
      busy = false
    }
  }

  let arm = $state(0)
  async function deleteAccount() {
    if (arm < 2) return arm++
    busy = true
    try {
      await api.deleteMe()
      session.me = null
      toast.show('账号已注销，你的邮箱、昵称和发布的阵容都已删除。')
      router.go('/plaza')
    } catch (e) {
      toast.show((e as Error).message, true)
    } finally {
      busy = false
    }
  }
</script>

{#if session.me}
  {@const me = session.me}
  <div class="in">
    <div class="profile">
      <span class="av"><Icon name="user" size={28} /></span>
      <div class="pi">
        {#if editingName}
          <div class="nameedit">
            <input bind:value={nickname} maxlength="16" placeholder={me.nickname ?? '起个昵称，2～16 个字'} onkeydown={(e) => e.key === 'Enter' && [...nickname.trim()].length >= 2 && saveName()} />
            <button class="btn primary sm" disabled={busy || [...nickname.trim()].length < 2} onclick={saveName}>保存</button>
            {#if me.nickname}<button class="btn ghost sm" onclick={() => (editingName = false)}>取消</button>{/if}
          </div>
          {#if !me.nickname}<p class="hint">还没有昵称：起好之前不能点赞，发布的阵容也会用它署名。</p>{/if}
        {:else}
          <h1>{me.nickname}<button class="btn ghost sm" onclick={() => ((nickname = me.nickname ?? ''), (editingName = true))}><Icon name="pencil" size={13} />改昵称</button></h1>
        {/if}
        <div class="meta">{me.emailMasked} · 注册于 {ago(me.createdAt)}</div>
      </div>
      <button class="btn ghost sm out" onclick={logout}>退出登录</button>
    </div>

    <h2 class="sec-h">登录密码<small>Password</small></h2>
    <div class="pw">
      {#if pwMode === 'none'}
        <p class="muted small">
          {me.hasPassword ? '已设置。忘了密码可以在登录页点「忘记密码」，用邮箱验证码重设。' : '还没有设置，设好才能继续用广场。'}
        </p>
        <div class="pwacts">
          <button class="btn sm" onclick={() => (pwMode = 'set')}>{me.hasPassword ? '修改密码' : '设置密码'}</button>
        </div>
      {:else}
        <div class="pwform">
          {#if me.hasPassword}
            <label><span>当前密码</span><input type="password" bind:value={pwCur} autocomplete="current-password" placeholder="刚用验证码登录的 30 分钟内可以不填" /></label>
          {/if}
          {#if pwMode === 'set'}
            <label><span>新密码</span><input type="password" bind:value={pw1} autocomplete="new-password" placeholder="8～64 位，不能和邮箱一样" /></label>
            <label><span>再输一次</span><input type="password" bind:value={pw2} autocomplete="new-password" /></label>
            {#if pw2 && pw1 !== pw2}<p class="err">两次输入的不一样。</p>{/if}
          {/if}
          {#if pwErr}<p class="err">{pwErr}</p>{/if}
          <div class="pwacts">
            {#if pwMode === 'set'}
              <button class="btn primary sm" disabled={busy || !pwOk} onclick={savePw}>保存密码</button>
            {:else}
              <button class="btn danger sm" disabled={busy} onclick={savePw}>删除密码</button>
            {/if}
            <button class="btn ghost sm" onclick={closePw}>取消</button>
          </div>
          <p class="muted small">保存后其他设备上的登录会退出，当前这台不受影响。</p>
        </div>
      {/if}
    </div>

    <div class="tabs" role="tablist">
      {#each tabs as t (t.v)}
        <button role="tab" aria-selected={tab === t.v} class:on={tab === t.v} onclick={() => (tab = t.v)}>{t.label}{#if t.n() !== null}<em>{t.n()}</em>{/if}</button>
      {/each}
      <span class="sp"></span>
      <a class="btn sm" href="/new" use:link><Icon name="pencil" size={13} />新建阵容</a>
    </div>
    {#if error}
      <p class="err">{error}</p>
    {:else if tab === 'mine'}
      {#if !mine}
        <p class="muted">加载中…</p>
      {:else if !mine.length}
        <p class="muted">还没有。在卫戍协议助手的设置里打开「同步我的阵容和偏好」，助手里的阵容就会出现在这里；也可以在网页上<a href="/new" use:link class="hl">新建</a>后「保存到我的阵容」。只有你自己看得到。</p>
      {:else}
        <p class="muted small">只有你自己看得到。在助手里打开「同步我的阵容和偏好」后，两边自动同步。</p>
        <div class="grid">
          {#each mine as c (c.id)}
            {@const l = c.data!}
            <div class="ml">
              <div class="mlh">
                <b>{l.name}</b>
                {#if l.stages?.length}<span class="stg">{l.stages.length + 1} 阶段</span>{/if}
                <span class="muted small">{ago(c.updatedAt)}</span>
              </div>
              {#if l.goals.length}<div class="goals">{#each l.goals as g (g.id)}<BondChip id={g.id} level={g.level} />{/each}</div>{/if}
              <div class="ops">
                {#each l.members.slice(0, 14) as m (m.name)}<OpAvatar name={m.name} size={36} isKey={m.key} elite={m.elite} />{/each}
                {#if l.members.length > 14}<span class="muted small">+{l.members.length - 14}</span>{/if}
                {#if !l.members.length}<span class="muted small">没有干员</span>{/if}
              </div>
              {#if l.description}<p class="mld">{l.description}</p>{/if}
              <div class="mla">
                <a class="btn sm" href="/new?edit={encodeURIComponent(c.id)}" use:link><Icon name="pencil" size={13} />编辑</a>
                <button class="btn ghost sm" onclick={() => copyMine(c)}><Icon name="copy" size={13} />分享码</button>
                <span class="sp"></span>
                <button class="btn ghost sm" class:danger={armDel === c.id} onclick={() => removeMine(c)}><Icon name="trash" size={13} />{armDel === c.id ? '确认删除' : '删除'}</button>
              </div>
            </div>
          {/each}
        </div>
      {/if}
    {:else if tab === 'notices'}
      <NoticeList />
    {:else if tab === 'posts'}
      {#if !posts}
        <p class="muted">加载中…</p>
      {:else if !posts.length}
        <p class="muted">还没有发布过。<a href="/new" use:link class="hl">新建一个阵容</a>，或者在卫戍协议助手里右键阵容卡「发布到广场」。</p>
      {:else}
        <div class="grid">{#each posts as p (p.id)}<PostCard post={p} />{/each}</div>
      {/if}
    {:else if !favs}
      <p class="muted">加载中…</p>
    {:else if !favs.length}
      <p class="muted">还没有收藏。打开广场里的阵容，点「收藏」就会出现在这里；助手广场的「收藏」里也看得到。</p>
    {:else}
      <div class="grid">{#each favs as p (p.id)}<PostCard post={p} />{/each}</div>
    {/if}

    <h2 class="sec-h danger">注销账号<small>Delete account</small></h2>
    <p class="muted small">注销会删除你的邮箱、昵称、发布的所有阵容、点赞、收藏和云端的「我的阵容」，不能恢复。</p>
    <button class="btn sm" class:danger={arm > 0} disabled={busy} onclick={deleteAccount}>
      {arm === 0 ? '注销账号' : arm === 1 ? '确定要注销吗？再点一次' : '最后确认：注销并删除全部数据'}
    </button>
    {#if arm}<button class="btn ghost sm" onclick={() => (arm = 0)}>算了</button>{/if}
    <p class="muted small back"><a href="/privacy" use:link>隐私说明</a></p>
  </div>
{/if}

<style>
  .tabs {
    display: flex;
    align-items: center;
    gap: 4px;
    margin: 8px 0 14px;
    border-bottom: 1px solid var(--line-soft);
  }
  .tabs button {
    height: 36px;
    padding: 0 14px;
    font-size: 14px;
    color: var(--fg-3);
    box-shadow: inset 0 -2px 0 transparent;
  }
  .tabs button.on {
    color: var(--fg-1);
    font-weight: 700;
    box-shadow: inset 0 -2px 0 var(--mint);
  }
  .tabs em {
    margin-left: 5px;
    font-style: normal;
    font-size: 12px;
    color: var(--fg-4);
  }
  .tabs .sp,
  .mla .sp {
    flex: 1;
  }
  .ml {
    display: flex;
    flex-direction: column;
    gap: 8px;
    padding: 14px;
    background: linear-gradient(180deg, var(--bg-2), var(--bg-1));
    box-shadow: inset 0 0 0 1px var(--line-soft);
  }
  .mlh {
    display: flex;
    align-items: center;
    gap: 8px;
  }
  .mlh b {
    min-width: 0;
    font-size: 15px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .mlh .small {
    margin-left: auto;
  }
  .stg {
    flex: none;
    font-size: 11px;
    padding: 0 5px;
    color: var(--mint);
    box-shadow: inset 0 0 0 1px var(--mint);
  }
  .goals,
  .ops {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 4px;
  }
  .mld {
    margin: 0;
    font-size: 12.5px;
    color: var(--fg-3);
    display: -webkit-box;
    -webkit-line-clamp: 2;
    line-clamp: 2;
    -webkit-box-orient: vertical;
    overflow: hidden;
    white-space: pre-line;
  }
  .mla {
    display: flex;
    align-items: center;
    gap: 6px;
  }
  .in {
    width: min(1200px, 100% - 32px);
    margin: 0 auto;
    padding-top: 28px;
  }
  .profile {
    display: flex;
    align-items: center;
    gap: 16px;
    padding: 18px;
    margin-bottom: 28px;
    background: linear-gradient(180deg, var(--bg-2), var(--bg-1));
    box-shadow: inset 0 0 0 1px var(--line-soft);
  }
  .av {
    display: grid;
    place-items: center;
    width: 56px;
    height: 56px;
    color: var(--mint);
    background: var(--bg-3);
  }
  .pi {
    flex: 1;
    min-width: 0;
  }
  h1 {
    display: flex;
    align-items: center;
    gap: 10px;
    margin: 0;
    font-size: 22px;
  }
  .nameedit {
    display: flex;
    gap: 6px;
  }
  .hint {
    margin: 6px 0 0;
    font-size: 12px;
    color: var(--gold);
  }
  .meta {
    margin-top: 4px;
    font-size: 12.5px;
    color: var(--fg-3);
  }
  .out {
    align-self: flex-start;
  }
  .pw {
    margin-bottom: 32px;
  }
  .pw p {
    margin: 0 0 10px;
  }
  .pwacts {
    display: flex;
    gap: 6px;
  }
  .pwform {
    display: flex;
    flex-direction: column;
    gap: 10px;
    max-width: 360px;
  }
  .pwform label {
    display: flex;
    flex-direction: column;
    gap: 4px;
  }
  .pwform label span {
    font-size: 12px;
    color: var(--fg-3);
  }
  .pwform .err {
    margin: 0;
  }
  .grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(330px, 1fr));
    gap: 14px;
    margin-bottom: 36px;
  }
  .sec-h.danger {
    margin-top: 36px;
    box-shadow: inset 2px 0 0 var(--red);
  }
  .small {
    font-size: 12.5px;
  }
  .hl {
    color: var(--mint);
    text-decoration: underline;
  }
  .back a {
    text-decoration: underline;
  }
  @media (max-width: 560px) {
    .profile {
      flex-wrap: wrap;
    }
    /* 四个标签加「新建阵容」放不下：标签横向滚动，新建阵容另起一行靠右 */
    .tabs {
      flex-wrap: wrap;
    }
    .tabs button {
      padding: 0 9px;
      white-space: nowrap;
    }
    .tabs .sp {
      display: none;
    }
    .tabs .btn {
      margin: 0 0 6px auto;
    }
  }
</style>
