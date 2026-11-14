<script lang="ts">
  import { fly, fade } from 'svelte/transition'
  import { cubicOut } from 'svelte/easing'
  import { app } from '../lib/state.svelte'
  import { markSeen } from '../lib/changelog'
  import NotesList from './NotesList.svelte'

  // 更新后第一次打开：这一版有什么新东西。重点条目在前，其余收在「还有 N 项」里；关掉就记为看过
  const n = $derived(app.whatsNew)
  const hl = $derived(n?.items.filter((i) => i.highlight) ?? [])
  const rest = $derived(n?.items.filter((i) => !i.highlight) ?? [])
  let all = $state(false)
  const done = () => {
    markSeen(app.appInfo.version)
    app.whatsNew = null
  }
  const openLog = () => {
    done()
    app.changelogOpen = true
  }
</script>

<svelte:window onkeydown={(e) => n && e.key === 'Escape' && done()} />

{#if n}
  <div class="scrim" transition:fade={{ duration: 200 }}></div>
  <div class="wrap">
    <div class="card" role="dialog" aria-label="这一版有什么新东西" in:fly={{ y: 24, duration: 280, easing: cubicOut }} out:fade={{ duration: 150 }}>
      <header>
        <span class="term tag">Updated</span>
        <h3>已更新到 <b class="term">v{n.version}</b></h3>
        <p>这一版有这些新东西</p>
      </header>
      <div class="body">
        <NotesList items={all ? [...hl, ...rest] : hl} />
        {#if rest.length && !all}
          <button class="more" onclick={() => (all = true)}>还有 {rest.length} 项改进和修复</button>
        {/if}
      </div>
      <footer>
        <button class="ghost" onclick={openLog}>全部更新日志</button>
        <button class="primary" onclick={done}>知道了</button>
      </footer>
    </div>
  </div>
{/if}

<style>
  .scrim {
    position: fixed;
    inset: 0;
    z-index: 70;
    background: var(--scrim-strong);
  }
  .wrap {
    position: fixed;
    inset: 0;
    z-index: 71;
    display: grid;
    place-items: center;
    /* 行高固定成窗口高度，卡片的 max-height: 100% 才生效；不然窗口矮时卡片撑出窗口，「知道了」按不到（用户 2026-11-12 反馈） */
    grid-template-rows: minmax(0, 1fr);
    padding: 16px 12px;
  }
  .card {
    position: relative;
    width: 100%;
    max-height: 100%;
    display: flex;
    flex-direction: column;
    background:
      var(--noise),
      linear-gradient(180deg, var(--sheet-top), var(--sheet-bottom) 160px);
    box-shadow:
      inset 0 0 0 1px var(--line-strong),
      0 16px 40px rgb(var(--shade-rgb) / 0.6);
  }
  .card::before {
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
    padding: 14px 14px 8px;
  }
  .tag {
    font-size: var(--fs-2xs);
    padding: 2px 5px 1px;
    color: var(--mint-ink);
    background: var(--mint);
  }
  h3 {
    margin: 8px 0 2px;
    font-size: var(--fs-2xl);
  }
  h3 b {
    color: var(--mint);
  }
  header p {
    margin: 0;
    font-size: var(--fs-base);
    color: var(--fg-3);
  }
  .body {
    flex: 1;
    min-height: 0;
    overflow-y: auto;
    padding: 4px 14px 10px;
  }
  .more {
    margin-top: 8px;
    font-size: var(--fs-base);
    color: var(--mint);
  }
  footer {
    display: flex;
    justify-content: flex-end;
    gap: 6px;
    padding: 10px 14px;
    border-top: 1px solid var(--line);
    background: var(--bg-1);
  }
  .ghost {
    font-size: var(--fs-base);
    color: var(--fg-3);
    padding: 5px 8px;
  }
  .ghost:hover {
    color: var(--fg-1);
  }
  .primary {
    height: 30px;
    padding: 0 18px;
    font-size: var(--fs-md);
    font-weight: var(--fw-bold);
    color: var(--mint-ink);
    background: var(--mint);
  }
  .primary:hover {
    background: var(--mint-hi);
  }
</style>
