<script lang="ts">
  import { fly, fade } from 'svelte/transition'
  import { cubicOut } from 'svelte/easing'
  import { app } from '../lib/state.svelte'
  import { decodeLineup } from '../lib/share'
  import LineupCard from './LineupCard.svelte'
  import Icon from './Icon.svelte'

  // 导入分享码：底部面板。粘贴后马上解码预览（整句转发的消息也行），名字可以先改，再导入或导入并设为目标
  let text = $state('')
  let name = $state('')
  let box: HTMLTextAreaElement | undefined = $state()
  const result = $derived(text.trim() ? decodeLineup(text, app.catalog) : null)
  const lineup = $derived(result?.ok ? { ...result.lineup, name: name.trim() || result.lineup.name } : null)
  const close = () => (app.importOpen = false)

  // 每次打开清空，并把光标放进输入框，直接 Ctrl+V
  $effect(() => {
    if (!app.importOpen) return
    text = ''
    name = ''
    queueMicrotask(() => box?.focus())
  })
  // 解出新的阵容时，名字框填上码里的名字
  $effect(() => {
    if (result?.ok) name = result.lineup.name
  })
</script>

<svelte:window onkeydown={(e) => app.importOpen && e.key === 'Escape' && close()} />

{#if app.importOpen}
  <div class="scrim" transition:fade={{ duration: 160 }} onclick={close} role="presentation"></div>
  <aside class="sheet" transition:fly={{ y: 320, duration: 240, easing: cubicOut }}>
    <header>
      <span class="hi"><Icon name="paste" size={16} /></span>
      <h3>导入阵容</h3>
      <button class="x" onclick={close} aria-label="关闭"><Icon name="close" size={16} /></button>
    </header>
    <p class="hint">粘贴别人发来的分享码，整句消息也可以。</p>
    <textarea bind:this={box} bind:value={text} rows="3" placeholder="【卫戍协议助手阵容】叙拉古 · 家族 SA1-…" spellcheck="false"></textarea>

    {#if result && !result.ok}
      <p class="err">{result.error}</p>
    {:else if lineup}
      <label class="nm">
        <span>名称</span>
        <input bind:value={name} maxlength="30" />
      </label>
      <div class="preview"><LineupCard {lineup} menu={false} /></div>
    {/if}

    <footer>
      <button class="ghost" onclick={close}>取消</button>
      <span class="sp"></span>
      <button class="secondary" disabled={!lineup} onclick={() => app.importLineup(lineup!, false)}>导入</button>
      <button class="primary" disabled={!lineup} onclick={() => app.importLineup(lineup!, true)}>导入并设为目标</button>
    </footer>
  </aside>
{/if}

<style>
  .scrim {
    position: absolute;
    inset: 0;
    background: rgb(0 0 0 / 0.55);
    z-index: 20;
  }
  .sheet {
    position: absolute;
    left: 0;
    right: 0;
    bottom: 0;
    max-height: 86%;
    overflow-y: auto;
    z-index: 21;
    display: flex;
    flex-direction: column;
    gap: 8px;
    padding: 12px 12px 0;
    background:
      var(--noise),
      linear-gradient(180deg, #202326, #16181a 120px);
    box-shadow:
      0 -1px 0 var(--line),
      0 -12px 30px rgb(0 0 0 / 0.6);
  }
  .sheet::before {
    content: '';
    position: absolute;
    top: 0;
    left: 12px;
    width: 64px;
    height: 2px;
    background: var(--mint);
    box-shadow: var(--mint-glow);
  }
  header {
    display: flex;
    align-items: center;
    gap: 8px;
  }
  .hi {
    display: grid;
    color: var(--mint);
  }
  h3 {
    margin: 0;
    font-size: 15px;
  }
  .x {
    margin-left: auto;
    width: 28px;
    height: 28px;
    display: grid;
    place-items: center;
    color: var(--fg-3);
  }
  .x:hover {
    color: var(--fg-1);
    background: var(--bg-4);
  }
  .hint {
    margin: -4px 0 0;
    font-size: 11px;
    color: var(--fg-4);
  }
  textarea {
    resize: none;
    padding: 8px;
    font: inherit;
    font-size: 12px;
    line-height: 1.5;
    color: var(--fg-1);
    background: var(--bg-1);
    border: 0;
    outline: 0;
    box-shadow: inset 0 0 0 1px var(--line);
    user-select: text;
    word-break: break-all;
  }
  textarea:focus {
    box-shadow: inset 0 0 0 1px var(--mint-lo);
  }
  textarea::placeholder {
    color: var(--fg-4);
  }
  .err {
    margin: 0;
    font-size: 12px;
    line-height: 1.5;
    color: #f2b8ba;
    padding: 6px 8px;
    background: var(--red-tint);
    box-shadow: inset 2px 0 0 var(--red);
  }
  .nm {
    display: flex;
    align-items: center;
    gap: 8px;
    font-size: 12px;
    color: var(--fg-3);
  }
  .nm input {
    flex: 1;
    min-width: 0;
    height: 28px;
    padding: 0 8px;
    font-size: 13px;
    font-weight: 700;
    background: var(--bg-1);
    border: 0;
    outline: 0;
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .nm input:focus {
    box-shadow: inset 0 0 0 1px var(--mint-lo);
  }
  footer {
    position: sticky;
    bottom: 0;
    display: flex;
    align-items: center;
    gap: 6px;
    margin: 4px -12px 0;
    padding: 10px 12px;
    border-top: 1px solid var(--line);
    background: var(--bg-1);
  }
  .sp {
    flex: 1;
  }
  .ghost {
    font-size: 12px;
    color: var(--fg-3);
    padding: 5px 8px;
  }
  .ghost:hover {
    color: var(--fg-1);
  }
  .primary,
  .secondary {
    height: 30px;
    padding: 0 12px;
    font-size: 13px;
    font-weight: 700;
    white-space: nowrap;
  }
  .primary {
    color: var(--mint-ink);
    background: var(--mint);
  }
  .primary:hover:not(:disabled) {
    background: var(--mint-hi);
  }
  .secondary {
    color: var(--fg-1);
    background: var(--bg-4);
  }
  .primary:disabled,
  .secondary:disabled {
    color: var(--fg-4);
    background: var(--bg-3);
    cursor: default;
  }
</style>
