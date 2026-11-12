<script lang="ts">
  import { app } from '../lib/state.svelte'
  import Icon from './Icon.svelte'
  import { api } from '../lib/api'
  import { cloud } from '../lib/sync.svelte'
  import { plaza } from '../lib/plaza.svelte'

  // 标题栏：产品里是无边框窗口的拖动区
  const conn = $derived(app.connection)
  const onTop = $derived(!!conn.overlay || app.settings.alwaysOnTop)
  // 云同步状态（v0.7.0）：打开了才显示；点一下立即同步，出错时打开设置看原因
  const syncTip = $derived(
    !plaza.token ? '云同步：登录阵容广场后才会同步' : cloud.status === 'syncing' ? '正在同步…' : cloud.status === 'error' ? `同步失败：${cloud.error}（点开设置）` : cloud.lastAt ? '已同步，点一下立即同步' : '云同步已打开，点一下立即同步',
  )
</script>

<div class="bar">
  <span class="logo" aria-hidden="true"><i></i></span>
  <span class="title">卫戍协议助手</span>
  <span class="sub term">Stronghold<br />Assistant</span>
  <span class="spacer"></span>
  <!-- 连上游戏只亮绿点（分辨率放进提示里，标题栏窄，显示了会挤掉「覆盖」）；没连上才写字 -->
  <!-- 找到游戏但用不了（MuMu 分辨率不对、多开没选）时写「无法连接」，原因放提示里 -->
  <span
    class="conn"
    class:ok={conn.state === 'connected'}
    class:warn={!!conn.problem}
    title={conn.state === 'connected' ? `已连接${conn.source === 'mumu' ? ' MuMu 模拟器' : '游戏窗口'} · ${conn.resolution}` : (conn.problem ?? '寻找游戏窗口…')}
  >
    <span class="dot"></span>
    {#if conn.state !== 'connected'}<span class="txt">{conn.problem ? '无法连接' : '寻找游戏窗口…'}</span>{/if}
    {#if conn.overlay}<em class="fs" title="侧栏盖在游戏画面左侧。买东西、布阵时可以点右边的按钮收成细条">覆盖</em>{/if}
  </span>
  <!-- 工作台：宽窗口编阵容、逛广场；工作台里不收起，只有「收回侧栏」 -->
  {#if app.workbench}
    <button class="wbtn on" title="收回侧栏，回到游戏旁边" aria-label="收回侧栏" onclick={() => app.setWorkbench(false)}><Icon name="sidebar" size={15} /></button>
  {:else}
    {#if !app.collapsed}
      <button class="wbtn" title="展开为工作台（宽窗口，编阵容、逛广场更方便）" aria-label="展开为工作台" onclick={() => app.setWorkbench(true)}><Icon name="workbench" size={15} /></button>
    {/if}
    <!-- 收起：按设置里的收起方式；向上收起后这里变成展开 -->
  {/if}
  {#if app.workbench}<!-- 工作台里不收起 -->{:else if app.collapsed === 'up'}
    <button class="wbtn on" title="展开侧栏" aria-label="展开侧栏" onclick={() => app.setCollapsed(false)}><Icon name="arrowDown" size={16} /></button>
  {:else if app.settings.collapseMode === 'up'}
    <button class="wbtn" title="向上收起，只留追踪列表" aria-label="向上收起" onclick={() => app.collapse()}><Icon name="arrowUp" size={16} /></button>
  {:else}
    <button class="wbtn" title="收起成细条" aria-label="收起成细条" onclick={() => app.collapse()}><Icon name="contractLeft" size={15} /></button>
  {/if}
  <!-- 覆盖模式盖在游戏上，宿主总是置顶，按钮只显示状态 -->
  <button
    class="wbtn"
    class:pinned={onTop}
    title={conn.overlay ? '覆盖模式下总是置顶' : onTop ? '已置顶，点击取消' : '置顶'}
    aria-label={conn.overlay ? '覆盖模式下总是置顶' : onTop ? '取消置顶' : '置顶'}
    aria-pressed={onTop}
    disabled={conn.overlay}
    onclick={() => app.toggleOnTop()}
  >
    <Icon name={onTop ? 'pinFill' : 'pin'} size={15} />
  </button>
  {#if app.settings.cloudSync.enabled}
    <button
      class="wbtn sync"
      class:busy={cloud.status === 'syncing'}
      class:bad={cloud.status === 'error' || !plaza.token}
      title={syncTip}
      aria-label={syncTip}
      onclick={() => (cloud.status === 'error' || !plaza.token ? (app.settingsOpen = true) : cloud.run())}
    >
      <Icon name={cloud.status === 'error' || !plaza.token ? 'cloudOff' : cloud.status === 'syncing' ? 'refresh' : 'cloud'} size={15} />
    </button>
  {/if}
  <button class="wbtn set" class:dot-new={app.update.state === 'available'} title="设置" aria-label="设置" onclick={() => (app.collapsed && app.setCollapsed(false), (app.settingsOpen = true))}>
    <Icon name="settings" size={15} />
  </button>
  <button class="wbtn" title="最小化" aria-label="最小化" onclick={() => api.windowCommand('minimize')}><Icon name="subtract" size={15} /></button>
  <button class="wbtn close" title="关闭" aria-label="关闭" onclick={() => api.windowCommand('close')}><Icon name="close" size={16} /></button>
</div>

<style>
  .bar {
    /* WebView2 非客户区：标题栏可拖动窗口，按钮除外 */
    -webkit-app-region: drag;
    display: flex;
    align-items: center;
    gap: 8px;
    height: 34px;
    padding-left: 10px;
    background: var(--bg-0);
    border-bottom: 1px solid var(--line-soft);
    flex: none;
  }
  .logo {
    flex: none;
    position: relative;
    width: 16px;
    height: 16px;
    border: 1.5px solid var(--mint);
    transform: rotate(45deg);
    box-shadow: 0 0 6px rgb(34 227 178 / 0.4);
  }
  .logo i {
    position: absolute;
    inset: 3px;
    background: var(--mint);
  }
  .title {
    white-space: nowrap;
    font-weight: 700;
    font-size: 13px;
    letter-spacing: 0.06em;
  }
  /* 装饰英文最先让出宽度 */
  .sub {
    min-width: 0;
    overflow: hidden;
    flex-shrink: 100;
    white-space: nowrap;
    font-size: 7px;
    line-height: 1.1;
    color: var(--fg-4);
  }
  .spacer {
    flex: 1;
  }
  /* 侧栏很窄时去掉装饰英文 */
  @media (max-width: 360px) {
    .sub {
      display: none;
    }
  }
  /* 宽度不够时连接状态先省略，窗口按钮始终留在行内 */
  .conn {
    min-width: 0;
    white-space: nowrap;
    font-family: var(--font-term);
    display: flex;
    align-items: center;
    gap: 5px;
    font-size: 11px;
    color: var(--fg-3);
    margin-right: 2px;
  }
  .txt {
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .fs {
    flex: none;
    font-style: normal;
    font-family: var(--font-ui);
    font-size: 10px;
    padding: 0 4px;
    color: var(--gold-ink);
    background: var(--gold);
  }
  .dot {
    flex: none;
    width: 6px;
    height: 6px;
    border-radius: 50%;
    background: var(--fg-4);
  }
  .warn {
    color: var(--gold);
  }
  .warn .dot {
    background: var(--gold);
  }
  .ok .dot {
    background: var(--mint);
    box-shadow: 0 0 6px var(--mint);
  }
  .wbtn {
    -webkit-app-region: no-drag;
    flex: none;
    width: 30px;
    height: 34px;
    display: grid;
    place-items: center;
    color: var(--fg-3);
  }
  /* 有新版本时齿轮右上角的金色小点 */
  .set {
    position: relative;
  }
  .dot-new::after {
    content: '';
    position: absolute;
    right: 7px;
    top: 8px;
    width: 6px;
    height: 6px;
    background: var(--gold);
  }
  .wbtn:hover {
    color: var(--fg-1);
    background: var(--bg-3);
  }
  .wbtn.pinned,
  .wbtn.on {
    color: var(--mint);
  }
  .wbtn:disabled {
    cursor: default;
    background: none;
  }
  .wbtn.close:hover {
    background: #c42b1c;
  }
  .sync.bad {
    color: var(--red);
  }
  .sync.busy :global(svg) {
    animation: sync-spin 0.9s linear infinite;
  }
  @keyframes sync-spin {
    to {
      transform: rotate(360deg);
    }
  }
</style>
