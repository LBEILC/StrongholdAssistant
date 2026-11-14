<script lang="ts">
  import { app } from './lib/state.svelte'
  import TitleBar from './components/TitleBar.svelte'
  import BondTracker from './components/BondTracker.svelte'
  import Icon from './components/Icon.svelte'
  import WorkbenchRail from './components/WorkbenchRail.svelte'
  import Tabs from './components/Tabs.svelte'
  import DetailSheet from './components/DetailSheet.svelte'
  import LineupEditor from './components/LineupEditor.svelte'
  import SettingsSheet from './components/SettingsSheet.svelte'
  import MatchPage from './pages/MatchPage.svelte'
  import LineupPage from './pages/LineupPage.svelte'
  import CodexPage from './pages/CodexPage.svelte'
  import HistoryPage from './pages/HistoryPage.svelte'
  import CollapsedStrip from './components/CollapsedStrip.svelte'
  import EconomyBar from './components/EconomyBar.svelte'
  import ContextMenu from './components/ContextMenu.svelte'
  import BanPicker from './components/BanPicker.svelte'
  import ImportSheet from './components/ImportSheet.svelte'
  import PlazaSheet from './components/PlazaSheet.svelte'
  import { plaza } from './lib/plaza.svelte'
  import Toast from './components/Toast.svelte'
  import ChangelogSheet from './components/ChangelogSheet.svelte'
  import WhatsNew from './components/WhatsNew.svelte'
  import PlazaPasswordGate from './components/PlazaPasswordGate.svelte'
  import Tooltip from './components/Tooltip.svelte'
  import { api } from './lib/api'
  import { skin } from './lib/skin.svelte'
  import { asset } from './lib/assets'
  import { fly } from 'svelte/transition'
  import { cubicOut } from 'svelte/easing'
  import UpdateBar from './components/UpdateBar.svelte'
  import { FONT_WEIGHTS, fontStack } from './lib/fonts'

  let upH = $state(0)
  $effect(() => {
    if (app.collapsed === 'up' && upH) api.collapseUp(upH)
  })

  // 界面皮肤：设置里的「界面主题」，默认跟着「游戏主题」走。皮肤位图从 skin.id 取，颜色令牌按 data-skin 覆盖。
  // 样式表里要用的皮肤位图（窗口框、闪光星等）写成根元素上的变量，用绝对地址，免得相对路径按样式表目录解析。
  // 每套皮肤只有自己用到的那几张，其他皮肤下变量指向不存在的文件也没关系（不会用到）
  const SKIN_IMAGES = [
    // 可露希尔
    'sparkle', 'folder',
    // 乌萨斯
    'flag', 'round', 'panel', 'sweep',
  ]
  $effect(() => {
    skin.id = app.settings.uiSkin === 'follow' ? app.gameTheme : app.settings.uiSkin
    const root = document.documentElement
    root.dataset.skin = skin.id
    for (const f of SKIN_IMAGES) root.style.setProperty(`--skin-${f.replace('_', '-')}`, `url(${asset.skin(f)})`)
  })
  // 界面字体和字重（v0.9.5）：选的字体缺字时回落到 MiSans
  $effect(() => {
    const s = document.documentElement.style
    s.setProperty('--font-cn', fontStack(app.settings.uiFont))
    const [base, semi, bold] = FONT_WEIGHTS[app.settings.uiFontWeight]
    s.setProperty('--fw-base', String(base))
    s.setProperty('--fw-semi', String(semi))
    s.setProperty('--fw-bold', String(bold))
  })

  let scroller: HTMLElement | undefined = $state()
  $effect(() => {
    app.tab
    if (scroller) scroller.scrollTop = 0
  })

  // 切换标签时页面从切换方向滑入：往右边的标签切，新页面从右边进来
  const order = ['match', 'lineup', 'codex', 'history']
  let prev = app.tab
  let dir = $state(1)
  $effect.pre(() => {
    const t = app.tab
    dir = order.indexOf(t) >= order.indexOf(prev) ? 1 : -1
    prev = t
  })

  // 广场新消息提醒（v0.9.3）：开着时启动就问一次未读数，之后每 10 分钟一次；关掉就不在后台联网
  $effect(() => {
    if (!app.settings.plazaNotices) return
    plaza.check().then(() => plaza.pollUnread())
    const id = setInterval(() => plaza.pollUnread(), 10 * 60 * 1000)
    return () => clearInterval(id)
  })
  $effect(() => {
    // 登录或换账号后马上问一次
    if (plaza.me && app.settings.plazaNotices) plaza.pollUnread()
  })

  // 网站上点「在助手里打开」：stronghold://import?post=<帖子 id> 打开广场详情，?code=<分享码> 打开导入面板
  $effect(() =>
    api.onDeepLink((url) => {
      let u: URL
      try {
        u = new URL(url)
      } catch {
        return
      }
      const post = u.searchParams.get('post')
      const code = u.searchParams.get('code')
      app.setTab('lineup')
      if (post)
        plaza
          .post(post)
          .then((p) => (plaza.viewing = p))
          .catch((e) => app.showToast(e.message, true))
      else if (code) {
        app.importPrefill = code
        app.importOpen = true
      }
    }),
  )
</script>

{#if app.collapsed === 'strip'}
  <CollapsedStrip />
{:else if app.collapsed === 'up'}
  <!-- 向上收起：只留标题栏和追踪列表，窗口高度跟着内容（追踪增减时再告诉宿主） -->
  <div class="shell up" bind:clientHeight={upH}>
    <TitleBar />
    <EconomyBar compact />
    <BondTracker />
  </div>
{:else}
<div class="shell {app.phase}" class:wb={app.workbench}>
  <TitleBar />
  {#if app.workbench}
    <!-- 工作台：左栏（导航、目标、追踪、更新），右边内容；详情面板停在右侧，不挡左边 -->
    <div class="wbody">
      <WorkbenchRail />
      <div class="wcol">
        {@render body()}
      </div>
    </div>
  {:else}
    <BondTracker />
    <Tabs />
    {@render body()}
  {/if}
</div>
{/if}

{#snippet body()}
  {#if !app.workbench}<UpdateBar />{/if}
  <div class="content" class:wb={app.workbench} class:lineup={app.tab === 'lineup'} class:docked={app.workbench && (!!app.detail || !!plaza.viewing || app.tab === 'codex')}>
    <main bind:this={scroller}>
      {#key app.tab}
        <!-- 历史页在工作台里左右两栏各自滚动，需要撑满滚动区的高度 -->
        <div class:fill={app.tab === 'history' || (app.workbench && app.tab === 'lineup')} in:fly={{ x: 28 * dir, duration: 240, easing: cubicOut }}>
          <!-- 一页渲染出错只挡住这一页，别的页照常能切 -->
          <svelte:boundary onerror={(e) => console.error(e)}>
            {#if app.tab === 'match'}
              <MatchPage />
            {:else if app.tab === 'lineup'}
              <LineupPage />
            {:else if app.tab === 'codex'}
              <CodexPage />
            {:else}
              <HistoryPage />
            {/if}
            {#snippet failed(error, reset)}
              <div class="crash">
                <b>这一页出错了</b>
                <p>可以先切到别的页，或点「重试」。还不行就在设置里导出诊断包发给我们。</p>
                <code>{error instanceof Error ? error.message : String(error)}</code>
                <button onclick={reset}>重试</button>
              </div>
            {/snippet}
          </svelte:boundary>
        </div>
      {/key}
    </main>
    <DetailSheet />
    {#if app.workbench && app.tab === 'codex' && !app.detail}
      <!-- 工作台图鉴：右边常驻详情栏，没选中时给个提示 -->
      <aside class="idle">
        <Icon name="grid" size={28} />
        <p>点一名干员、一件装备、一个策略或首领，<br />详情显示在这里。</p>
      </aside>
    {/if}
    <LineupEditor />
    <BanPicker />
    <ImportSheet />
    <PlazaSheet />
    <Toast />
    <SettingsSheet />
    <ChangelogSheet />
  </div>
{/snippet}
<ContextMenu />
<Tooltip />
{#if app.phase !== 'boot'}<WhatsNew />{/if}
<!-- 阵容广场账号还没密码：先设密码（v0.9.2） -->
<PlazaPasswordGate />

<style>
  /* 启动：标题栏和原生骨架一致、不动，其余区块等宿主显示网页后依次淡入上移 */
  .shell.boot > :global(:not(:first-child)) {
    opacity: 0;
  }
  .shell.in > :global(:not(:first-child)) {
    animation: rise var(--t-enter) var(--ease-out) both;
  }
  .shell.in > :global(:nth-child(3)) {
    animation-delay: var(--t-stagger);
  }
  .shell.in > :global(:nth-child(n + 4)) {
    animation-delay: calc(var(--t-stagger) * 2);
  }
  @keyframes rise {
    from {
      opacity: 0;
      transform: translateY(10px);
    }
  }
  /* 关闭：标题栏留着，其余淡出到底色 */
  .shell.out > :global(:not(:first-child)) {
    opacity: 0;
    transform: translateY(6px);
    transition:
      opacity var(--t-exit) ease-in,
      transform var(--t-exit) ease-in;
  }
  .shell {
    height: 100%;
    display: flex;
    flex-direction: column;
    background:
      var(--noise),
      radial-gradient(120% 60% at 50% 0%, var(--bg-1a), var(--bg-0));
    overflow: hidden;
  }
  /* 向上收起：高度由内容决定，底边一道薄荷绿细线表示下面还收着东西 */
  .shell.up {
    height: auto;
    border-bottom: 1px solid var(--mint-lo);
  }
  .content {
    position: relative;
    flex: 1;
    min-height: 0;
  }
  main > .fill {
    height: 100%;
  }
  .crash {
    display: flex;
    flex-direction: column;
    align-items: flex-start;
    gap: 8px;
    margin: 16px;
    padding: 14px 16px;
    font-size: var(--fs-base);
    color: var(--fg-3);
    background: var(--bg-1);
    box-shadow: inset 2px 0 0 var(--red);
  }
  .crash b {
    font-size: var(--fs-lg);
    color: var(--red-fg);
  }
  .crash p {
    margin: 0;
  }
  .crash code {
    max-width: 100%;
    font-family: var(--font-term);
    font-size: var(--fs-sm);
    color: var(--fg-4);
    overflow-wrap: anywhere;
  }
  .crash button {
    height: 28px;
    padding: 0 14px;
    font-size: var(--fs-base);
    color: var(--fg-1);
    background: var(--bg-3);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .crash button:hover {
    box-shadow: inset 0 0 0 1px var(--fg-4);
  }
  main {
    height: 100%;
    overflow-x: hidden;
    overflow-y: auto;
    /* 页面按自己的可用宽度换多栏（容器查询），侧栏和工作台用同一套页面 */
    container-type: inline-size;
    transition: margin-right var(--t-mid) var(--ease-out);
  }

  /* 工作台 */
  .wbody {
    flex: 1;
    min-height: 0;
    display: flex;
  }
  .wcol {
    flex: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
  }
  .content.wb main > :global(div) {
    max-width: 1440px;
    margin: 0 auto;
  }
  /* 面板都停在右边：详情、广场帖子一栏宽，编辑器和禁用选择宽一些 */
  .content.wb > :global(aside) {
    top: 0;
    bottom: 0;
    left: auto;
    right: 0;
    width: min(440px, 46%);
    max-height: none;
    box-shadow:
      -1px 0 0 var(--line),
      -16px 0 32px rgb(var(--shade-rgb) / 0.45);
  }
  .content.wb > :global(aside.ed) {
    width: min(720px, 66%);
  }
  /* 阵容页：编辑器、禁用选择铺满右边，左边「我的阵容」列表露着 */
  .content.wb.lineup > :global(aside.ed) {
    left: var(--wb-list-w);
    width: auto;
  }
  .content.wb.lineup > :global(.scrim) {
    right: auto;
    width: var(--wb-list-w);
  }
  .content.wb :global(aside .grip) {
    display: none;
  }
  .content.wb > :global(.scrim) {
    background: rgb(var(--shade-rgb) / 0.3);
  }
  /* 详情和广场帖子不遮左边：左边照常能点，点别的卡片直接换内容 */
  .content.wb > :global(.scrim.dock) {
    display: none;
  }
  .idle {
    position: absolute;
    top: 0;
    right: 0;
    bottom: 0;
    width: min(440px, 46%);
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 10px;
    text-align: center;
    font-size: var(--fs-base);
    line-height: 1.6;
    color: var(--fg-4);
    background: var(--bg-1);
    box-shadow: -1px 0 0 var(--line);
  }
  .idle p {
    margin: 0;
  }
  .content.docked main {
    margin-right: min(440px, 46%);
  }
</style>
