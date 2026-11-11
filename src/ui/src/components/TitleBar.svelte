<script lang="ts">
  import { app } from '../lib/state.svelte'
  import Icon from './Icon.svelte'
  import { api } from '../lib/api'

  // 标题栏：产品里是无边框窗口的拖动区
  const conn = $derived(app.connection)
</script>

<div class="bar">
  <span class="logo" aria-hidden="true"><i></i></span>
  <span class="title">卫戍协议助手</span>
  <span class="sub term">Stronghold<br />Assistant</span>
  <span class="spacer"></span>
  <span class="conn" class:ok={conn.state === 'connected'} title={conn.state === 'connected' ? `已连接游戏窗口 ${conn.resolution}` : '寻找游戏窗口…'}>
    <span class="dot"></span>
    <span class="txt">{conn.state === 'connected' ? conn.resolution : '寻找游戏窗口…'}</span>
    {#if conn.overlay}<em class="fs" title="侧栏盖在游戏画面左侧。买东西、布阵时可以点右边的按钮收成细条">覆盖</em>{/if}
  </span>
  {#if conn.overlay}
    <button class="wbtn" title="收起成细条" aria-label="收起成细条" onclick={() => app.setCollapsed(true)}><Icon name="contractLeft" size={15} /></button>
  {/if}
  <button class="wbtn set" class:dot-new={app.update.state === 'available'} title="设置" aria-label="设置" onclick={() => (app.settingsOpen = true)}>
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
  .wbtn.close:hover {
    background: #c42b1c;
  }
</style>
