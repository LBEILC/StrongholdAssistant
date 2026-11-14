<script lang="ts">
  import Icon from './components/Icon.svelte'
  import Home from './pages/Home.svelte'
  import Changelog from './pages/Changelog.svelte'
  import Faq from './pages/Faq.svelte'
  import About from './pages/About.svelte'
  import logo from '../static/icon-192.png'
  import { LATEST, LINKS, type PageId } from './data'

  let { page }: { page: PageId } = $props()

  const NAV: { id: string; label: string; href: string }[] = [
    { id: 'features', label: '功能', href: '/#features' },
    { id: 'download', label: '下载', href: '/#download' },
    { id: 'changelog', label: '更新日志', href: '/changelog/' },
    { id: 'faq', label: '常见问题', href: '/faq/' },
    { id: 'about', label: '开源与素材', href: '/about/' },
  ]

  let scrolled = $state(false)
  let progress = $state(0)
  let menu = $state(false)
  function onScroll() {
    scrolled = scrollY > 8
    const max = document.documentElement.scrollHeight - innerHeight
    progress = max > 0 ? Math.min(1, scrollY / max) : 0
  }
  $effect(() => {
    document.documentElement.style.overflow = menu ? 'hidden' : ''
  })
</script>

<svelte:window onscroll={onScroll} onkeydown={(e) => e.key === 'Escape' && (menu = false)} />

<header class="top" class:solid={scrolled || menu}>
  <div class="wrap bar">
    <a class="brand" href="/" aria-label="卫戍协议助手 首页">
      <img src={logo} alt="" width="30" height="30" />
      <span class="bt">
        <b>卫戍协议助手</b>
        <span class="lbl">Stronghold Assistant</span>
      </span>
    </a>

    <nav class="links" aria-label="主导航">
      {#each NAV as n (n.id)}
        <a href={n.href} class:on={n.id === page}>{n.label}</a>
      {/each}
      <span class="sep"></span>
      <a href={LINKS.plaza}>阵容广场<Icon name="arrow" size={13} /></a>
      <span class="soon" title="讨论阵容、干员强度，给干员和装备打分：正在筹备" aria-disabled="true">社区<em>筹备中</em></span>
    </nav>

    <a class="btn primary sm dl" href="/#download"><Icon name="download" size={16} />下载<span class="sub">v{LATEST.version}</span></a>
    <button class="burger" aria-label={menu ? '关闭菜单' : '打开菜单'} aria-expanded={menu} onclick={() => (menu = !menu)}>
      <Icon name={menu ? 'close' : 'menu'} size={22} />
    </button>
  </div>
  <i class="prog" style:transform="scaleX({progress})"></i>
</header>

{#if menu}
  <div class="drawer">
    <nav class="wrap" aria-label="主导航">
      {#each NAV as n, i (n.id)}
        <a href={n.href} class:on={n.id === page} onclick={() => (menu = false)}><span class="lbl">0{i + 1}</span>{n.label}</a>
      {/each}
      <a href={LINKS.plaza}><span class="lbl">06</span>阵容广场<Icon name="arrow" size={16} /></a>
      <span class="soon-row"><span class="lbl">07</span>社区<em>筹备中</em></span>
      <a class="btn primary" href="/#download" onclick={() => (menu = false)}><Icon name="download" size={18} />下载 v{LATEST.version}</a>
    </nav>
  </div>
{/if}

<main>
  {#if page === 'home'}
    <Home />
  {:else if page === 'changelog'}
    <Changelog />
  {:else if page === 'faq'}
    <Faq />
  {:else}
    <About />
  {/if}
</main>

<footer class="foot">
  <div class="wrap">
    <div class="signoff">
      <span class="lbl mint">// End of protocol</span>
      <p>协议结束。<span class="en">See you in the next stronghold, Doctor.</span></p>
    </div>
    <div class="cols">
      <div class="col me">
        <a class="brand" href="/">
          <img src={logo} alt="" width="30" height="30" />
          <span class="bt"><b>卫戍协议助手</b><span class="lbl">Stronghold Assistant</span></span>
        </a>
        <p class="dim">《明日方舟》卫戍协议的 PC 辅助侧栏。<br />免费，以 Apache-2.0 开源。</p>
      </div>
      <div class="col">
        <span class="lbl">Product</span>
        <a href="/#download">下载</a>
        <a href="/changelog/">更新日志</a>
        <a href="/faq/">常见问题</a>
        <a href={LINKS.plaza}>阵容广场</a>
      </div>
      <div class="col">
        <span class="lbl">Community</span>
        <a href={LINKS.qq} target="_blank" rel="noopener">QQ 群 {LINKS.qqGroup}</a>
        <a href={LINKS.repo} target="_blank" rel="noopener">GitHub</a>
        <a href={LINKS.issues} target="_blank" rel="noopener">反馈问题</a>
        <span class="dim">社区 · 筹备中</span>
      </div>
      <div class="col">
        <span class="lbl">About</span>
        <a href="/about/#license">开源许可</a>
        <a href="/about/#assets">素材归属</a>
        <a href="/about/#privacy">数据与隐私</a>
      </div>
    </div>
    <p class="legal">
      非官方玩家自制工具，与上海鹰角网络科技有限公司无关。《明日方舟》游戏美术与数据归上海鹰角网络所有，来源与归属见<a href="/about/#assets">素材说明</a>。
    </p>
  </div>
</footer>

<style>
  .top {
    position: fixed;
    inset: 0 0 auto;
    z-index: 50;
    height: var(--head-h);
    border-bottom: 1px solid transparent;
    transition:
      background var(--t-mid),
      border-color var(--t-mid);
  }
  .top.solid {
    background: rgb(var(--bg-0-rgb) / 0.82);
    backdrop-filter: blur(14px) saturate(1.2);
    border-color: var(--line-soft);
  }
  .bar {
    height: 100%;
    display: flex;
    align-items: center;
    gap: 28px;
  }
  .brand {
    display: flex;
    align-items: center;
    gap: 10px;
    flex: none;
  }
  .brand img {
    filter: drop-shadow(0 0 8px rgb(var(--mint-rgb) / 0.35));
  }
  .bt {
    display: flex;
    flex-direction: column;
    line-height: 1.15;
  }
  .bt b {
    font-size: 16px;
    letter-spacing: 0.04em;
  }
  .bt .lbl {
    font-size: 9px;
    letter-spacing: 0.24em;
  }
  .links {
    display: flex;
    align-items: center;
    gap: 2px;
    margin-left: auto;
  }
  .links a,
  .soon {
    display: inline-flex;
    align-items: center;
    gap: 3px;
    height: 34px;
    padding: 0 12px;
    font-size: 14px;
    color: var(--fg-2);
    border-radius: var(--r-1);
    transition:
      color var(--t-fast),
      background var(--t-fast);
  }
  .links a:hover {
    color: var(--fg-0);
    background: rgb(var(--hl-rgb) / 0.05);
  }
  .links a.on {
    color: var(--mint);
  }
  .sep {
    width: 1px;
    height: 16px;
    margin: 0 8px;
    background: var(--line);
  }
  .soon {
    color: var(--fg-4);
    cursor: default;
  }
  em {
    font-style: normal;
    font-size: 10px;
    margin-left: 4px;
    padding: 0 5px;
    line-height: 16px;
    color: var(--fg-3);
    border: 1px solid var(--line);
    border-radius: var(--r-1);
  }
  .dl {
    flex: none;
  }
  .burger {
    display: none;
    width: 40px;
    height: 40px;
    place-items: center;
    margin-right: -8px;
    color: var(--fg-1);
  }
  .prog {
    position: absolute;
    left: 0;
    right: 0;
    bottom: -1px;
    height: 2px;
    background: linear-gradient(90deg, var(--mint-lo), var(--mint));
    box-shadow: 0 0 8px rgb(var(--mint-rgb) / 0.6);
    transform-origin: 0 0;
  }
  @media (max-width: 1080px) {
    .links,
    .dl {
      display: none;
    }
    .burger {
      display: grid;
      margin-left: auto;
    }
  }

  .drawer {
    position: fixed;
    inset: var(--head-h) 0 0;
    z-index: 49;
    background: rgb(var(--bg-0-rgb) / 0.97);
    backdrop-filter: blur(14px);
    overflow-y: auto;
    animation: drop var(--t-mid) var(--ease-out);
  }
  @keyframes drop {
    from {
      opacity: 0;
      transform: translateY(-8px);
    }
  }
  .drawer nav {
    display: flex;
    flex-direction: column;
    padding-top: 16px;
    padding-bottom: 32px;
  }
  .drawer nav > a:not(.btn),
  .soon-row {
    display: flex;
    align-items: center;
    gap: 14px;
    padding: 16px 0;
    font-size: 20px;
    font-weight: 600;
    border-bottom: 1px solid var(--line-soft);
  }
  .drawer a.on {
    color: var(--mint);
  }
  .soon-row {
    color: var(--fg-4);
  }
  .drawer .btn {
    margin-top: 28px;
    height: 52px;
  }

  main {
    flex: 1;
  }

  .foot {
    margin-top: 40px;
    padding: 0 0 40px;
    border-top: 1px solid var(--line-soft);
    background: linear-gradient(180deg, var(--bg-1), var(--bg-0));
  }
  .signoff {
    padding: 56px 0 48px;
    border-bottom: 1px solid var(--line-soft);
  }
  .signoff p {
    margin-top: 10px;
    font-size: var(--d-2);
    font-weight: 700;
    letter-spacing: 0.02em;
  }
  .signoff .en {
    margin-left: 12px;
    font-family: var(--font-term);
    font-weight: 400;
    font-size: 0.7em;
    letter-spacing: 0.08em;
    color: var(--fg-3);
  }
  .cols {
    display: grid;
    grid-template-columns: 2fr 1fr 1fr 1fr;
    gap: 32px;
    padding: 40px 0;
  }
  .col {
    display: flex;
    flex-direction: column;
    gap: 8px;
    font-size: 14px;
  }
  .col .lbl {
    margin-bottom: 6px;
  }
  .col a {
    color: var(--fg-2);
    width: fit-content;
  }
  .col a:hover {
    color: var(--mint);
  }
  .me p {
    margin-top: 12px;
    font-size: 13px;
  }
  .legal {
    font-size: 12px;
    color: var(--fg-4);
  }
  .legal a {
    color: var(--fg-3);
    text-decoration: underline;
  }
  @media (max-width: 720px) {
    .cols {
      grid-template-columns: 1fr 1fr;
    }
    .me {
      grid-column: 1 / -1;
    }
    .signoff .en {
      display: block;
      margin: 6px 0 0;
    }
  }
</style>
