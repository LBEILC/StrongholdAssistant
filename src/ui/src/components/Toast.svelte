<script lang="ts">
  import { fly } from 'svelte/transition'
  import { cubicOut } from 'svelte/easing'
  import { app } from '../lib/state.svelte'
  import Icon from './Icon.svelte'

  // 底部轻提示：复制、导入这类没有别的界面反馈的操作用，几秒后自己消失
</script>

{#if app.toast}
  {#key app.toast}
    <div class="toast" class:bad={app.toast.bad} role="status" in:fly={{ y: 12, duration: 200, easing: cubicOut }} out:fly={{ y: 6, duration: 150 }}>
      <Icon name={app.toast.bad ? 'close' : 'check'} size={14} />
      <span>{app.toast.text}</span>
    </div>
  {/key}
{/if}

<style>
  .toast {
    position: absolute;
    left: 50%;
    bottom: 16px;
    z-index: 60;
    transform: translateX(-50%);
    max-width: calc(100% - 24px);
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 7px 12px 7px 10px;
    font-size: 12px;
    color: var(--fg-1);
    white-space: nowrap;
    background: #202326;
    box-shadow:
      inset 2px 0 0 var(--mint),
      inset 0 0 0 1px #3a3e43,
      0 6px 20px rgb(0 0 0 / 0.55);
    pointer-events: none;
  }
  .toast :global(.ri) {
    color: var(--mint);
  }
  /* 失败：红色竖条，文字可以换行（错误原因可能较长） */
  .toast.bad {
    white-space: normal;
    box-shadow:
      inset 2px 0 0 var(--red),
      inset 0 0 0 1px #3a3e43,
      0 6px 20px rgb(0 0 0 / 0.55);
  }
  .toast.bad :global(.ri) {
    color: var(--red);
  }
  span {
    overflow: hidden;
    text-overflow: ellipsis;
  }
</style>
