<script lang="ts">
  import { app } from '../lib/state.svelte'

  // 更新横幅：点一下直接下载更新，不再跳到设置；想先看更新内容点右边。
  // 从这里点过更新又失败了，横幅留着说原因，点开设置的「关于」看详情、换更新源
  let { rail = false }: { rail?: boolean } = $props()
  const u = $derived(app.update)
  const failed = $derived(u.state === 'failed' && app.updateTried)
</script>

{#if u.state === 'available' || u.state === 'downloading' || u.state === 'ready' || failed}
  <div class="bar" class:rail class:bad={failed}>
    {#if u.state === 'available'}
      <button class="main" onclick={() => app.applyUpdate()} title="下载新版本，完成后自动重启">
        <b class="term">Update</b><span>新版本 v{u.version}，点一下更新</span>
      </button>
      <button class="side" onclick={() => app.openSettings('about')}>更新了什么</button>
    {:else if u.state === 'downloading'}
      <span class="main"><b class="term">Update</b><span>正在下载 v{u.version}… <em class="term">{Math.round((u.progress ?? 0) * 100)}%</em></span></span>
      <i class="prog" style:width="{(u.progress ?? 0) * 100}%"></i>
    {:else if u.state === 'ready'}
      <span class="main"><b class="term">Update</b><span>下载完成，即将重启</span></span>
    {:else}
      <button class="main" onclick={() => app.openSettings('about')}>
        <b class="term">Update</b><span>更新失败：{u.message}，点这里看看</span>
      </button>
    {/if}
  </div>
{/if}

<style>
  .bar {
    position: relative;
    flex: none;
    display: flex;
    align-items: stretch;
    min-height: 28px;
    font-size: var(--fs-base);
    color: var(--gold);
    background: rgb(var(--gold-rgb) / 0.1);
    border-bottom: 1px solid rgb(var(--gold-rgb) / 0.3);
  }
  .main {
    flex: 1;
    min-width: 0;
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 4px 10px;
    text-align: left;
    color: inherit;
  }
  button.main:hover {
    background: rgb(var(--gold-rgb) / 0.12);
  }
  .main b {
    flex: none;
    font-size: var(--fs-2xs);
    padding: 2px 4px 1px;
    color: var(--gold-ink);
    background: var(--gold);
  }
  .main em {
    font-style: normal;
  }
  .side {
    flex: none;
    padding: 0 10px;
    font-size: var(--fs-sm);
    color: var(--fg-3);
    border-left: 1px solid rgb(var(--gold-rgb) / 0.25);
  }
  .side:hover {
    color: var(--gold);
  }
  .prog {
    position: absolute;
    left: 0;
    bottom: -1px;
    height: 2px;
    background: var(--gold);
    transition: width var(--t-mid);
  }
  .bad {
    color: var(--red);
    background: rgb(var(--red-rgb) / 0.1);
    border-color: rgb(var(--red-rgb) / 0.3);
  }
  .bad .main b {
    color: var(--fg-0);
    background: var(--red);
  }
  /* 工作台左栏底部：上边框、字小一号，可以换行 */
  .rail {
    font-size: var(--fs-sm);
    line-height: 1.4;
    border-bottom: none;
    border-top: 1px solid rgb(var(--gold-rgb) / 0.3);
  }
  .rail .main {
    padding: 8px 12px;
  }
  .rail .prog {
    top: -1px;
    bottom: auto;
  }
</style>
