<script lang="ts">
  import { router, link } from './router.svelte'
  import { session, toast } from './session.svelte'
  import Home from './pages/Home.svelte'
  import PostPage from './pages/PostPage.svelte'
  import Login from './pages/Login.svelte'
  import MePage from './pages/MePage.svelte'
  import Privacy from './pages/Privacy.svelte'
  import Admin from './pages/Admin.svelte'
  import Editor from './pages/Editor.svelte'
  import Icon from './components/Icon.svelte'
  import logo from '../static/icon-64.png'
  import Tooltip from './components/Tooltip.svelte'
  import PasswordGate from './components/PasswordGate.svelte'

  session.load()
  // 在助手的登录小窗口里只显示页面本身
  const inApp = new URLSearchParams(location.search).get('app') === '1'
  const r = $derived(router.route)
  const REPO = 'https://github.com/LBEILC/StrongholdAssistant'

  // 站内消息未读数：登录后、每次切页面问一次
  $effect(() => {
    void r
    if (session.me && !inApp) session.pollUnread()
  })

  // 标签页标题跟着页面走
  $effect(() => {
    const t = { home: '阵容广场', post: '阵容', login: '登录', me: '我的', new: '新建阵容', privacy: '隐私说明', admin: '后台', notfound: '找不到页面' }[r.page]
    document.title = `${t} · 卫戍协议助手`
  })
</script>

{#if !inApp}
<header class="top">
  <div class="in">
    <a class="brand" href="/" use:link>
      <img class="logo" src={logo} alt="" />
      <span class="bt">
        <b>阵容广场</b>
        <span class="term">Stronghold Assistant · Plaza</span>
      </span>
    </a>
    <nav>
      <a href="/" use:link class:on={r.page === 'home' || r.page === 'post'}>广场</a>
      <a href="/new" use:link class:on={r.page === 'new'}>新建阵容</a>
      <a href={REPO + '/releases/latest'} target="_blank" rel="noopener">下载助手<Icon name="external" size={12} /></a>
      {#if session.ready}
        {#if session.me}
          <a href="/me?tab=notices" use:link class="bell" class:on={r.page === 'me' && location.search.includes('tab=notices')} data-tip={session.unread ? `${session.unread} 条新消息` : '消息'} aria-label={session.unread ? `${session.unread} 条新消息` : '消息'}
            ><Icon name="bell" size={16} />{#if session.unread}<em class="term">{session.unread > 99 ? '99+' : session.unread}</em>{/if}</a
          >
          <a href="/me" use:link class="who" class:on={r.page === 'me'}><Icon name="user" size={15} /><span class="nk">{session.me.nickname ?? '起个昵称'}</span></a>
        {:else}
          <a href="/login" use:link class="who" class:on={r.page === 'login'}>登录</a>
        {/if}
      {/if}
    </nav>
  </div>
</header>
{/if}

<!-- 已登录但还没密码：先设密码（登录页里自己有这一步，隐私说明页不拦） -->
{#if session.me && !session.me.hasPassword && r.page !== 'login' && r.page !== 'privacy'}<PasswordGate />{/if}

<main>
  {#if r.page === 'home'}
    <Home />
  {:else if r.page === 'post'}
    {#key r.id}<PostPage id={r.id} />{/key}
  {:else if r.page === 'login'}
    <Login next={r.next} />
  {:else if r.page === 'me'}
    {#key location.search}<MePage />{/key}
  {:else if r.page === 'new'}
    {#key `${r.edit}|${r.post}`}<Editor edit={r.edit} post={r.post} />{/key}
  {:else if r.page === 'privacy'}
    <Privacy />
  {:else if r.page === 'admin'}
    <Admin />
  {:else}
    <div class="nf">
      <b class="term">404</b>
      <p>没有这个页面。</p>
      <a class="btn ghost" href="/" use:link>回到广场</a>
    </div>
  {/if}
</main>

{#if !inApp}
<footer>
  <div class="in">
    <span>卫戍协议助手 · 阵容广场。非官方粉丝项目，与上海鹰角网络无关；游戏美术与数据归其所有。</span>
    <span class="links">
      <a href="/privacy" use:link>隐私说明</a>
      <a href={REPO} target="_blank" rel="noopener"><Icon name="github" size={13} />源码</a>
    </span>
  </div>
</footer>
{/if}

<Tooltip />

{#if toast.text}
  <div class="toast" class:bad={toast.bad} role="status">{toast.text}</div>
{/if}

<style>
  .in {
    width: min(1200px, 100% - 32px);
    margin: 0 auto;
  }
  .top {
    position: sticky;
    top: 0;
    z-index: 10;
    background: rgb(11 12 13 / 0.86);
    backdrop-filter: blur(8px);
    border-bottom: 1px solid var(--line-soft);
  }
  .top .in {
    display: flex;
    align-items: center;
    gap: 16px;
    height: 56px;
  }
  .brand {
    display: flex;
    align-items: center;
    gap: 12px;
  }
  .logo {
    width: 32px;
    height: 32px;
  }
  .bt {
    display: flex;
    flex-direction: column;
    line-height: 1.15;
  }
  .bt b {
    font-size: 16px;
    letter-spacing: 0.06em;
  }
  .bt .term {
    font-size: 9px;
    color: var(--fg-4);
  }
  nav {
    margin-left: auto;
    display: flex;
    align-items: center;
    gap: 4px;
  }
  nav a {
    display: inline-flex;
    align-items: center;
    gap: 5px;
    height: 32px;
    padding: 0 10px;
    color: var(--fg-2);
    border-radius: var(--r-1);
    white-space: nowrap;
  }
  nav a:hover {
    color: var(--fg-1);
    background: var(--bg-2);
  }
  nav a.on {
    color: var(--mint);
  }
  nav .who {
    box-shadow: inset 0 0 0 1px var(--line);
  }
  nav .bell {
    position: relative;
    padding: 0 8px;
  }
  .bell em {
    position: absolute;
    right: 0;
    top: 2px;
    min-width: 16px;
    height: 16px;
    padding: 0 4px;
    font-size: 11px;
    font-style: normal;
    line-height: 16px;
    text-align: center;
    color: var(--mint-ink);
    background: var(--mint);
    border-radius: 8px;
  }
  .nk {
    max-width: 10em;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  main {
    flex: 1;
  }
  footer {
    margin-top: 48px;
    border-top: 1px solid var(--line-soft);
    font-size: 12px;
    color: var(--fg-4);
  }
  footer .in {
    display: flex;
    flex-wrap: wrap;
    justify-content: space-between;
    gap: 8px 24px;
    padding: 18px 0 28px;
  }
  .links {
    display: flex;
    gap: 16px;
  }
  .links a {
    display: inline-flex;
    align-items: center;
    gap: 4px;
  }
  .links a:hover {
    color: var(--fg-2);
  }
  .nf {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 10px;
    padding: 120px 16px;
  }
  .nf b {
    font-size: 48px;
    color: var(--fg-4);
  }
  .toast {
    position: fixed;
    right: 20px;
    bottom: 20px;
    z-index: 50;
    max-width: calc(100% - 40px);
    padding: 10px 14px;
    background: var(--bg-3);
    box-shadow:
      inset 2px 0 0 var(--mint),
      0 8px 24px rgb(0 0 0 / 0.5);
  }
  .toast.bad {
    box-shadow:
      inset 2px 0 0 var(--red),
      0 8px 24px rgb(0 0 0 / 0.5);
  }
  /* 手机上顶栏放不下：标题只留图标，外链图标去掉，昵称最多显示 5 个字 */
  @media (max-width: 560px) {
    .bt {
      display: none;
    }
    nav {
      gap: 2px;
    }
    nav a {
      padding: 0 6px;
    }
    nav a:not(.bell) > :global(.ri:last-child) {
      display: none;
    }
    .nk {
      max-width: 5em;
    }
  }
</style>
