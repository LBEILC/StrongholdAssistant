<script lang="ts">
  import type { Snippet } from 'svelte'
  import Icon from './Icon.svelte'
  import { skin } from '../lib/skin.svelte'

  // 仿游戏开局信息页的分区标题条：做旧底、左侧图标、标题、标签，右侧英文小字
  interface Props {
    title: string
    en?: string
    tag?: string
    tagTone?: 'plain' | 'warn'
    icon?: Snippet
    right?: Snippet
    /** 传入后整条标题可以点击折叠，右端显示箭头 */
    ontoggle?: () => void
    folded?: boolean
  }
  let { title, en = 'STRONGHOLD PROTOCOL', tag, tagTone = 'plain', icon, right, ontoggle, folded = false }: Props = $props()
</script>

<header class="sh" class:fold={ontoggle} class:folded>
  {#if ontoggle}<button class="hit" onclick={ontoggle} aria-expanded={!folded} aria-label="{folded ? '展开' : '收起'}{title}"></button>{/if}
  {#if icon}<span class="icon">{@render icon()}</span>{/if}
  <h2>{title}</h2>
  {#if tag}<span class="tag" class:warn={tagTone === 'warn'}>{tag}</span>{/if}
  <span class="spacer"></span>
  {#if right}
    {@render right()}
  {:else if skin.id === 'closure'}
    <!-- 可露希尔：窗口右上角的三个小方块（游戏 rest_window_back） -->
    <span class="winbtns" aria-hidden="true"><i></i><i></i><i></i></span>
  {:else if skin.id === 'ursus'}
    <!-- 乌萨斯：游戏「联防阶段」横幅下的一排箭头 -->
    <span class="arrows" aria-hidden="true"><i></i><i></i><i></i><i></i></span>
  {:else}
    <span class="en term"><i></i><i></i>RHODES ISLAND<br />{en}</span>
  {/if}
  {#if ontoggle}<span class="chev"><Icon name="arrowDown" size={16} /></span>{/if}
</header>

<style>
  .sh {
    position: relative;
    display: flex;
    align-items: center;
    gap: 8px;
    height: 34px;
    padding: 0 10px 0 8px;
    background:
      var(--noise),
      linear-gradient(180deg, var(--head-top), var(--head-bottom));
    box-shadow:
      inset 0 1px 0 rgb(var(--hl-rgb) / 0.05),
      inset 0 -1px 0 rgb(var(--shade-rgb) / 0.6);
  }
  .sh::after {
    /* 做旧边缘：右下角的磨损斑点 */
    content: '';
    position: absolute;
    inset: 0;
    background:
      radial-gradient(circle at 92% 80%, rgb(var(--shade-rgb) / 0.35) 0 2px, transparent 3px),
      radial-gradient(circle at 63% 15%, rgb(var(--shade-rgb) / 0.3) 0 1px, transparent 2px),
      radial-gradient(circle at 35% 90%, rgb(var(--hl-rgb) / 0.05) 0 1px, transparent 2px);
    pointer-events: none;
  }
  .hit {
    position: absolute;
    inset: 0;
    z-index: 0;
  }
  .fold:hover {
    background:
      var(--noise),
      linear-gradient(180deg, var(--head-hi-top), var(--head-hi-bottom));
  }
  /* 整条可点：文字不接点击，右侧按钮盖在点击区之上 */
  .fold > :global(:not(button)) {
    pointer-events: none;
  }
  .sh > :global(button:not(.hit)) {
    position: relative;
    z-index: 1;
  }
  .chev {
    display: grid;
    color: var(--fg-3);
    margin-right: -4px;
    transition: transform var(--t-mid) var(--ease-out);
  }
  .fold:hover .chev {
    color: var(--fg-1);
  }
  /* 折叠时只转里面的箭头，按钮本身不转（乌萨斯的斜切块、可露希尔的方块转了会变形） */
  .chev :global(svg) {
    transition: transform var(--t-mid) var(--ease-out);
  }
  .folded .chev :global(svg) {
    transform: rotate(-90deg);
  }
  .icon {
    display: grid;
    place-items: center;
    width: 20px;
    height: 20px;
    color: var(--fg-1);
  }
  h2 {
    white-space: nowrap;
    margin: 0;
    font-size: var(--fs-xl);
    font-weight: 700;
    letter-spacing: 0.04em;
  }
  .tag {
    white-space: nowrap;
    font-size: var(--fs-sm);
    color: var(--fg-2);
    background: var(--bg-5);
    padding: 1px 6px;
    border-radius: var(--r-1);
  }
  .tag.warn {
    color: var(--red-fg-hi);
    background: rgb(var(--red-rgb) / 0.28);
  }
  .spacer {
    flex: 1;
  }
  .en {
    font-size: var(--fs-3xs);
    line-height: 1.15;
    color: var(--fg-4);
    text-align: left;
    position: relative;
    padding-left: 30px;
  }
  @media (max-width: 360px) {
    .en {
      display: none;
    }
  }
  .en i {
    position: absolute;
    top: 2px;
    width: 11px;
    height: 13px;
    border: 1px solid var(--fg-4);
  }
  .en i:first-child {
    left: 0;
  }
  .en i:nth-child(2) {
    left: 14px;
    background: var(--hatch);
  }

  /* 可露希尔：小窗口的标题栏，蓝色渐变，下沿一道窗口边；右边三个小方块，折叠箭头也收进一个方块 */
  :global([data-skin='closure']) .sh {
    height: 30px;
    background: var(--win-title);
    box-shadow:
      inset 0 1px 0 rgb(var(--hl-rgb) / 0.25),
      0 2px 0 var(--win-edge);
    border-radius: var(--r-1) var(--r-1) 0 0;
  }
  :global([data-skin='closure']) .sh::after {
    display: none;
  }
  :global([data-skin='closure']) .fold:hover {
    background: var(--win-title-hi);
  }
  :global([data-skin='closure']) h2 {
    font-size: var(--fs-lg);
    color: var(--fg-0);
  }
  :global([data-skin='closure']) .icon {
    color: var(--win-cyan);
  }
  :global([data-skin='closure']) .tag {
    color: #c9ccff;
    background: rgb(var(--shade-rgb) / 0.3);
    border-radius: var(--r-pill);
    padding: 0 8px;
  }
  :global([data-skin='closure']) .tag.warn {
    color: #fff0f7;
    background: rgb(var(--red-rgb) / 0.55);
  }
  .winbtns {
    display: flex;
    gap: 3px;
  }
  .winbtns i,
  :global([data-skin='closure']) .chev {
    width: 16px;
    height: 16px;
    box-shadow:
      inset 1px 1px 0 rgb(var(--hl-rgb) / 0.35),
      inset -1px -1px 0 rgb(var(--shade-rgb) / 0.5);
  }
  .winbtns i {
    background: var(--win-btn);
  }
  .winbtns i:nth-child(2) {
    background: var(--win-pink);
  }
  .winbtns i:nth-child(3) {
    background: var(--win-cyan);
  }
  :global([data-skin='closure']) .chev {
    place-items: center;
    margin-right: 0;
    color: #c9ccff;
    background: var(--win-btn);
  }
  /* 可折叠时折叠方块就是「最小化」，排在最左；装饰方块只留粉、青两个 */
  :global([data-skin='closure']) .chev {
    order: 1;
  }
  .winbtns {
    order: 2;
  }
  .fold .winbtns i:first-child {
    display: none;
  }

  /* 乌萨斯：深色底上压一面橙红标语条（游戏 text_bg，左边带缺口），标题米白粗斜体；右边一排箭头；折叠按钮是斜切块 */
  :global([data-skin='ursus']) .sh {
    height: 34px;
    background:
      var(--skin-flag) left center / auto 100% no-repeat,
      linear-gradient(180deg, #241c18, #191411);
    box-shadow: inset 0 -2px 0 rgb(var(--rust-rgb) / 0.5);
  }
  :global([data-skin='ursus']) .sh::after {
    display: none;
  }
  :global([data-skin='ursus']) .fold:hover {
    background:
      var(--skin-flag) left center / auto 100% no-repeat,
      linear-gradient(180deg, #2e241e, #1f1814);
  }
  :global([data-skin='ursus']) .icon {
    color: var(--cream);
  }
  :global([data-skin='ursus']) h2 {
    font-style: italic;
    color: var(--cream);
    text-shadow: 1px 1px 0 rgb(var(--shade-rgb) / 0.45);
  }
  :global([data-skin='ursus']) .tag {
    font-style: italic;
    color: var(--cream);
    background: rgb(var(--shade-rgb) / 0.4);
    border-radius: 0;
    clip-path: polygon(4px 0, 100% 0, calc(100% - 4px) 100%, 0 100%);
    padding: 1px 9px;
  }
  .arrows {
    display: flex;
    gap: 2px;
    order: 1;
  }
  .arrows i {
    width: 9px;
    height: 12px;
    background: var(--rust-hi);
    clip-path: polygon(0 0, 45% 0, 100% 50%, 45% 100%, 0 100%, 55% 50%);
    opacity: 0.35;
    animation: ursus-march 1.6s steps(1) infinite;
  }
  .arrows i:nth-child(2) {
    animation-delay: 0.4s;
  }
  .arrows i:nth-child(3) {
    animation-delay: 0.8s;
  }
  .arrows i:nth-child(4) {
    animation-delay: 1.2s;
  }
  @keyframes ursus-march {
    0%,
    24% {
      opacity: 1;
    }
    25% {
      opacity: 0.35;
    }
  }
  :global([data-skin='ursus']) .chev {
    order: 2;
    width: 24px;
    height: 18px;
    place-items: center;
    margin-right: 0;
    color: var(--cream);
    background: var(--rust);
    clip-path: polygon(5px 0, 100% 0, calc(100% - 5px) 100%, 0 100%);
  }
</style>
