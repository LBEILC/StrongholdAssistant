<script lang="ts">
  import { app, type Tab } from '../lib/state.svelte'

  const tabs: { id: Tab; label: string; en: string }[] = [
    { id: 'match', label: '本局', en: 'Match' },
    { id: 'lineup', label: '阵容', en: 'Lineup' },
    { id: 'codex', label: '图鉴', en: 'Archive' },
    { id: 'history', label: '历史', en: 'Record' },
  ]
  const idx = $derived(tabs.findIndex((t) => t.id === app.tab))
  // vertical：工作台左边的竖排导航
  let { vertical = false }: { vertical?: boolean } = $props()
</script>

<nav class="tabs" class:vertical style:--n={tabs.length} style:--i={idx}>
  {#each tabs as t (t.id)}
    <button class:on={app.tab === t.id} onclick={() => app.setTab(t.id)}>
      <span class="in"><span class="lb">{t.label}</span><span class="en term">{t.en}</span></span>
    </button>
  {/each}
  <!-- 底部亮条：一条，切换时滑到目标标签 -->
  <span class="ink" style:transform={vertical ? `translateY(${idx * 100}%)` : `translateX(${idx * 100}%)`}></span>
</nav>

<style>
  .tabs {
    position: relative;
    flex: none;
    display: grid;
    grid-template-columns: repeat(var(--n), 1fr);
    background: var(--bg-1);
    border-bottom: 1px solid var(--line);
  }
  button {
    position: relative;
    display: flex;
    align-items: center;
    justify-content: center;
    height: 38px;
    padding-bottom: 1px;
    color: var(--fg-3);
    transition: color var(--t-mid);
  }
  button:hover {
    color: var(--fg-1);
  }
  .in {
    display: flex;
    align-items: baseline;
    gap: 5px;
    line-height: 1;
  }
  .lb {
    font-size: var(--fs-lg);
    font-weight: 700;
    letter-spacing: 0.1em;
  }
  .en {
    font-size: var(--fs-3xs);
    color: var(--fg-4);
  }
  button.on {
    color: var(--fg-1);
    background: linear-gradient(180deg, transparent 40%, var(--mint-tint));
  }
  button.on .en {
    color: var(--mint);
  }
  .ink {
    position: absolute;
    left: 0;
    bottom: -1px;
    width: calc(100% / var(--n));
    height: 3px;
    pointer-events: none;
    transition: transform var(--t-mid) var(--ease-out);
  }
  .ink::after {
    /* 斜切亮条 */
    content: '';
    position: absolute;
    left: 18%;
    right: 18%;
    top: 0;
    bottom: 0;
    background: var(--mint);
    clip-path: polygon(3px 0, calc(100% - 3px) 0, 100% 100%, 0 100%);
    box-shadow: var(--mint-glow);
  }
  /* 竖排：每项一行，亮条在左边 */
  .vertical {
    grid-template-columns: none;
    grid-auto-rows: 46px;
    align-content: start;
    border-bottom: none;
    border-right: 1px solid var(--line);
  }
  .vertical button {
    height: auto;
    justify-content: flex-start;
    padding: 0 0 0 18px;
  }
  .vertical button.on {
    background: linear-gradient(90deg, var(--mint-tint), transparent 70%);
  }
  .vertical .in {
    flex-direction: column;
    align-items: flex-start;
    gap: 3px;
  }
  .vertical .ink {
    bottom: auto;
    top: 0;
    width: 3px;
    height: 46px;
  }
  .vertical .ink::after {
    left: 0;
    right: 0;
    top: 10px;
    bottom: 10px;
    clip-path: polygon(0 0, 100% 3px, 100% calc(100% - 3px), 0 100%);
  }

  /* 可露希尔：文件夹页签。每个标签是一个文件夹（左上角折角），选中的是亮青文件夹（游戏回合数字底 round_num_back），右上角闪光星 */
  :global([data-skin='closure']) .tabs {
    gap: 4px;
    padding: 6px 6px 0;
    background: var(--bg-0);
    border-bottom: 2px solid var(--mint);
  }
  :global([data-skin='closure']) button {
    height: 34px;
    color: var(--fg-2);
    background: var(--bg-3);
    clip-path: polygon(0 0, 42% 0, 50% 7px, 100% 7px, 100% 100%, 0 100%);
    padding-top: 6px;
  }
  :global([data-skin='closure']) button:hover {
    color: var(--fg-0);
    background: var(--bg-4);
  }
  :global([data-skin='closure']) button.on {
    color: var(--mint-ink);
    background: var(--mint);
  }
  :global([data-skin='closure']) .en {
    font-size: 12px;
    letter-spacing: 0;
    text-transform: none;
    color: var(--fg-3);
  }
  :global([data-skin='closure']) button.on .en {
    color: rgb(var(--bg-0-rgb) / 0.6);
  }
  :global([data-skin='closure']) .ink {
    display: none;
  }
  /* 闪光星不能放在按钮里（按钮有 clip-path 会被裁掉），放在导航上，跟着选中的标签移动 */
  :global([data-skin='closure']) .tabs::after {
    content: '';
    position: absolute;
    top: 0;
    /* 左右各 6px 内边距、标签之间 4px：第 i 个标签的右上角 */
    left: calc(6px + (100% - 12px - 4px * (var(--n) - 1)) / var(--n) * (var(--i) + 1) + 4px * var(--i) - 10px);
    width: 16px;
    height: 16px;
    background: var(--win-pink);
    mask: var(--skin-sparkle) center / contain no-repeat;
    pointer-events: none;
    transition: left var(--t-mid) var(--ease-out);
    animation: tab-twinkle 2.4s ease-in-out infinite;
  }
  @keyframes tab-twinkle {
    50% {
      transform: scale(0.6) rotate(20deg);
      opacity: 0.5;
    }
  }
  :global([data-skin='closure']) .vertical {
    padding: 6px 0 0 6px;
    border-bottom: none;
    border-right: 2px solid var(--mint);
  }
  :global([data-skin='closure']) .vertical button {
    clip-path: polygon(0 0, 100% 0, 100% 100%, 0 100%);
    margin-bottom: 4px;
    padding-top: 0;
  }
  :global([data-skin='closure']) .vertical::after {
    display: none;
  }

  /* 乌萨斯：一排斜切的平行四边形，选中的是铁锈红块（带做旧颗粒）配米白斜体字，底下铁锈红粗线 */
  :global([data-skin='ursus']) .tabs {
    gap: 0;
    padding: 6px 4px 0;
    background: var(--bg-0);
    border-bottom: 3px solid var(--rust);
  }
  :global([data-skin='ursus']) button {
    height: 32px;
    margin: 0 -3px;
    color: var(--fg-3);
    background: var(--bg-3);
    clip-path: polygon(var(--slant) 0, 100% 0, calc(100% - var(--slant)) 100%, 0 100%);
  }
  :global([data-skin='ursus']) button:hover {
    color: var(--cream);
    background: var(--bg-4);
  }
  :global([data-skin='ursus']) button.on {
    color: var(--cream);
    background:
      var(--noise),
      linear-gradient(180deg, var(--rust-hi), var(--rust));
  }
  :global([data-skin='ursus']) .lb {
    font-style: italic;
    letter-spacing: 0.04em;
  }
  :global([data-skin='ursus']) .en {
    font-style: italic;
    font-weight: 700;
    color: var(--fg-4);
  }
  :global([data-skin='ursus']) button.on .en {
    color: rgb(240 228 200 / 0.7);
  }
  :global([data-skin='ursus']) .ink {
    display: none;
  }
  :global([data-skin='ursus']) .vertical {
    padding: 6px 0 0 4px;
    border-bottom: none;
    border-right: 3px solid var(--rust);
  }
  :global([data-skin='ursus']) .vertical button {
    margin: 0 0 3px;
    clip-path: polygon(0 0, 100% 0, calc(100% - var(--slant)) 100%, 0 100%);
  }
</style>
