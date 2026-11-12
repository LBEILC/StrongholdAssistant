<script lang="ts">
  import type { Snippet } from 'svelte'
  import { slide } from 'svelte/transition'
  import { app } from '../lib/state.svelte'
  import Icon from './Icon.svelte'

  // 可折叠的错误提示（v0.7.1，用户 2026-11-12 要求）：醒目的标题 + 给人看的原因 + 操作；「详情」展开技术细节，能复制下来反馈。
  // 宿主的错误约定：第一行给人看，第二行起是技术细节（见 lib/api/bridge.ts）
  interface Props {
    /** 做什么失败了，如「读取森空岛攻略失败」 */
    title: string
    /** 错误信息（宿主原样传来的，可以多行） */
    error: string
    /** 复制时一起带上的现场信息，如 { 链接: url } */
    context?: Record<string, string>
    actions?: Snippet
    /** 传了就在右上角显示关闭 */
    onclose?: () => void
  }
  let { title, error, context = {}, actions, onclose }: Props = $props()
  const lines = $derived(error.split('\n').map((l) => l.trim()).filter(Boolean))
  const reason = $derived(lines[0] ?? '')
  const detail = $derived(lines.slice(1))
  let open = $state(false)

  const report = $derived(
    [`卫戍协议助手 v${app.appInfo.version}`, title, ...lines, ...Object.entries(context).map(([k, v]) => `${k}：${v}`)].join('\n'),
  )
  async function copy() {
    try {
      await navigator.clipboard.writeText(report)
      app.showToast('已复制错误信息，发到 QQ 群或 GitHub Issues 就行')
    } catch {
      app.showToast('复制失败，手动选中详情里的文字复制吧', true)
    }
  }
</script>

<div class="en" role="alert">
  <div class="top">
    <span class="ic"><Icon name="forbid" size={15} /></span>
    <div class="txt">
      <b>{title}</b>
      <span>{reason}</span>
    </div>
    <button class="tg" class:on={open} onclick={() => (open = !open)} aria-expanded={open}>详情<Icon name="arrowDown" size={12} /></button>
    {#if onclose}<button class="x" aria-label="关闭" onclick={onclose}><Icon name="close" size={13} /></button>{/if}
  </div>
  {#if actions}<div class="acts">{@render actions()}</div>{/if}
  {#if open}
    <div class="more" transition:slide={{ duration: 160 }}>
      <pre>{report}</pre>
      <div class="mf">
        <button class="cp" onclick={copy}><Icon name="copy" size={12} />复制错误信息</button>
        <span>{detail.length ? '' : '没有更多技术细节。'}遇到问题可以发到 QQ 交流群或 GitHub Issues。</span>
      </div>
    </div>
  {/if}
</div>

<style>
  .en {
    background: var(--red-tint);
    box-shadow:
      inset 3px 0 0 var(--red),
      inset 0 0 0 1px rgb(232 72 79 / 0.35);
  }
  .top {
    display: flex;
    align-items: flex-start;
    gap: 8px;
    padding: 8px 8px 6px 10px;
  }
  .ic {
    flex: none;
    display: grid;
    place-items: center;
    width: 22px;
    height: 22px;
    margin-top: 1px;
    color: #fff;
    background: var(--red);
  }
  .txt {
    flex: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 2px;
  }
  .txt b {
    font-size: 13px;
    color: #ffd9da;
  }
  .txt span {
    font-size: 12px;
    line-height: 1.5;
    color: #f2b8ba;
  }
  .tg {
    flex: none;
    display: inline-flex;
    align-items: center;
    gap: 2px;
    height: 22px;
    padding: 0 6px;
    font-size: 11px;
    color: #f2b8ba;
    box-shadow: inset 0 0 0 1px rgb(242 184 186 / 0.35);
  }
  .tg :global(svg) {
    transition: transform 160ms;
  }
  .tg.on :global(svg) {
    transform: rotate(180deg);
  }
  .tg:hover {
    color: #fff;
  }
  .x {
    flex: none;
    display: grid;
    place-items: center;
    width: 22px;
    height: 22px;
    color: #f2b8ba;
  }
  .x:hover {
    color: #fff;
  }
  .acts {
    display: flex;
    flex-wrap: wrap;
    gap: 6px;
    padding: 0 8px 8px 40px;
  }
  .acts :global(button) {
    display: inline-flex;
    align-items: center;
    gap: 3px;
    height: 24px;
    padding: 0 8px;
    font-size: 12px;
    color: var(--fg-1);
    background: rgb(0 0 0 / 0.3);
  }
  .acts :global(button:hover) {
    background: rgb(0 0 0 / 0.5);
  }
  .more {
    margin: 0 8px 8px 40px;
    padding: 8px;
    background: rgb(0 0 0 / 0.35);
  }
  pre {
    margin: 0;
    max-height: 140px;
    overflow: auto;
    /* 英文数字等宽，中文用界面字体（只写 Consolas 时中文会退回宋体） */
    font-family: 'Consolas', 'MiSans', 'Microsoft YaHei UI', monospace;
    font-size: 11px;
    line-height: 1.5;
    white-space: pre-wrap;
    word-break: break-all;
    color: var(--fg-2);
    user-select: text;
  }
  .mf {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    gap: 8px;
    margin-top: 6px;
    font-size: 11px;
    color: var(--fg-4);
  }
  .cp {
    display: inline-flex;
    align-items: center;
    gap: 3px;
    font-size: 12px;
    color: var(--mint);
  }
</style>
