<script lang="ts">
  import Icon from './Icon.svelte'

  // 网页里的侧栏演示：/demo/ 是助手界面本体的构建产物，浏览器里自动用示例数据。点开才加载（约 3MB），不占首屏流量
  let { open = $bindable(false) }: { open?: boolean } = $props()

  const SKINS = [
    { id: 'terminal', label: '卫戍协议终端' },
    { id: 'ursus', label: '乌萨斯齐心团' },
    { id: 'closure', label: '可露希尔' },
  ]
  let skin = $state('terminal')
  let dlg: HTMLDialogElement
  let loaded = $state(false)

  $effect(() => {
    if (open && !dlg.open) {
      // 示例数据一打开会弹「已更新到…」，先记为看过（和演示同源）
      try {
        localStorage.setItem('stronghold.seenVersion', '0.4.2')
      } catch {}
      loaded = false
      dlg.showModal()
    } else if (!open && dlg.open) dlg.close()
  })
</script>

<dialog bind:this={dlg} onclose={() => (open = false)} onclick={(e) => e.target === dlg && (open = false)}>
  <div class="box">
    <div class="head">
      <div class="tt">
        <span class="lbl mint">Live demo</span>
        <b>侧栏演示</b>
      </div>
      <button class="x" aria-label="关闭" onclick={() => (open = false)}><Icon name="close" size={20} /></button>
    </div>
    <div class="skins" role="tablist" aria-label="界面主题">
      {#each SKINS as s (s.id)}
        <button role="tab" aria-selected={skin === s.id} class:on={skin === s.id} onclick={() => ((skin = s.id), (loaded = false))}>{s.label}</button>
      {/each}
    </div>
    <div class="frame">
      {#if open}
        {#key skin}
          <iframe title="卫戍协议助手侧栏演示" src="/demo/?demo&skin={skin}#match" onload={() => (loaded = true)}></iframe>
        {/key}
      {/if}
      {#if !loaded}<div class="loading"><span class="live-dot"></span><span class="lbl">Connecting…</span></div>{/if}
    </div>
    <p class="note">这就是助手的界面本体，数据是示例。点标签页、点头像、右键都能试；连游戏、识别要在电脑上装了才行。</p>
  </div>
</dialog>

<style>
  dialog {
    padding: 0;
    border: 0;
    background: transparent;
    color: inherit;
    max-width: 100vw;
    max-height: 100dvh;
  }
  dialog::backdrop {
    background: rgb(var(--shade-rgb) / 0.72);
    backdrop-filter: blur(6px);
  }
  dialog[open] {
    animation: pop var(--t-mid) var(--ease-out);
  }
  @keyframes pop {
    from {
      opacity: 0;
      transform: translateY(12px) scale(0.98);
    }
  }
  .box {
    width: 412px;
    padding: 16px;
    background: linear-gradient(180deg, var(--bg-2a), var(--bg-1a));
    border: 1px solid var(--line-strong);
    box-shadow: 0 30px 80px rgb(var(--shade-rgb) / 0.6);
  }
  .head {
    display: flex;
    align-items: center;
    justify-content: space-between;
  }
  .tt {
    display: flex;
    align-items: baseline;
    gap: 10px;
  }
  .tt b {
    font-size: 17px;
  }
  .x {
    display: grid;
    place-items: center;
    width: 34px;
    height: 34px;
    color: var(--fg-2);
    border-radius: var(--r-1);
  }
  .x:hover {
    color: var(--fg-0);
    background: var(--bg-4);
  }
  .skins {
    display: flex;
    gap: 4px;
    margin: 12px 0;
  }
  .skins button {
    flex: 1;
    height: 30px;
    font-size: 12px;
    color: var(--fg-3);
    background: var(--bg-3);
    border-radius: var(--r-1);
  }
  .skins button:hover {
    color: var(--fg-1);
  }
  .skins .on {
    color: var(--mint-ink);
    background: var(--mint);
    font-weight: 600;
  }
  .frame {
    position: relative;
    width: 380px;
    height: min(860px, calc(100dvh - 210px));
    background: var(--bg-0);
  }
  iframe {
    display: block;
    width: 100%;
    height: 100%;
    border: 0;
  }
  .loading {
    position: absolute;
    inset: 0;
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 10px;
    background: var(--bg-0);
  }
  .note {
    margin-top: 12px;
    font-size: 12px;
    line-height: 1.6;
    color: var(--fg-3);
  }
  @media (max-width: 440px) {
    .box {
      width: 100vw;
      height: 100dvh;
      padding: 12px var(--gutter);
      display: flex;
      flex-direction: column;
      border: 0;
    }
    .frame {
      width: 100%;
      flex: 1;
      height: auto;
    }
  }
</style>
