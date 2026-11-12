<script lang="ts">
  import { api, type Post } from '../api'
  import { router, link } from '../router.svelte'
  import { session, toast } from '../session.svelte'
  import { ago } from '../data'
  import PostCard from '../components/PostCard.svelte'
  import Icon from '../components/Icon.svelte'

  // 我的：昵称、我发的阵容（含旧赛季）、退出登录、注销账号
  $effect(() => {
    if (session.ready && !session.me) router.go('/login?next=/me')
  })

  let nickname = $state('')
  let editingName = $state(false)
  let busy = $state(false)
  let posts = $state<Post[] | null>(null)
  let error = $state('')

  $effect(() => {
    const me = session.me
    if (!me) return
    if (!me.nickname) editingName = true
    api
      .posts({ author: me.id, season: 'all', sort: 'new' })
      .then((r) => (posts = r.posts))
      .catch((e) => (error = e.message))
  })

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
    router.go('/')
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
      router.go('/')
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
          {me.hasPassword ? '已设置。登录时可以选「密码登录」，也可以继续用验证码登录。' : '没有设置，登录只能用邮箱验证码。设了密码以后可以直接用密码登录，验证码登录照样能用。'}
        </p>
        <div class="pwacts">
          <button class="btn sm" onclick={() => (pwMode = 'set')}>{me.hasPassword ? '修改密码' : '设置密码'}</button>
          {#if me.hasPassword}<button class="btn ghost sm" onclick={() => (pwMode = 'delete')}>删除密码</button>{/if}
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

    <h2 class="sec-h">我发布的阵容<small>My lineups</small></h2>
    {#if error}
      <p class="err">{error}</p>
    {:else if !posts}
      <p class="muted">加载中…</p>
    {:else if !posts.length}
      <p class="muted">还没有发布过。<a href="/new" use:link class="hl">新建一个阵容</a>，或者在卫戍协议助手里右键阵容卡「发布到广场」。</p>
    {:else}
      <div class="grid">{#each posts as p (p.id)}<PostCard post={p} />{/each}</div>
    {/if}

    <h2 class="sec-h danger">注销账号<small>Delete account</small></h2>
    <p class="muted small">注销会删除你的邮箱、昵称、发布的所有阵容和点赞，不能恢复。</p>
    <button class="btn sm" class:danger={arm > 0} disabled={busy} onclick={deleteAccount}>
      {arm === 0 ? '注销账号' : arm === 1 ? '确定要注销吗？再点一次' : '最后确认：注销并删除全部数据'}
    </button>
    {#if arm}<button class="btn ghost sm" onclick={() => (arm = 0)}>算了</button>{/if}
    <p class="muted small back"><a href="/privacy" use:link>隐私说明</a></p>
  </div>
{/if}

<style>
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
  }
</style>
