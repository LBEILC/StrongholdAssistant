<script lang="ts">
  import type { Snippet } from 'svelte'
  import Icon from './Icon.svelte'

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
      linear-gradient(180deg, #2a2d31, #202326);
    box-shadow:
      inset 0 1px 0 rgb(255 255 255 / 0.05),
      inset 0 -1px 0 rgb(0 0 0 / 0.6);
  }
  .sh::after {
    /* 做旧边缘：右下角的磨损斑点 */
    content: '';
    position: absolute;
    inset: 0;
    background:
      radial-gradient(circle at 92% 80%, rgb(0 0 0 / 0.35) 0 2px, transparent 3px),
      radial-gradient(circle at 63% 15%, rgb(0 0 0 / 0.3) 0 1px, transparent 2px),
      radial-gradient(circle at 35% 90%, rgb(255 255 255 / 0.05) 0 1px, transparent 2px);
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
      linear-gradient(180deg, #303338, #25282b);
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
  .folded .chev {
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
    font-size: 15px;
    font-weight: 700;
    letter-spacing: 0.04em;
  }
  .tag {
    white-space: nowrap;
    font-size: 11px;
    color: var(--fg-2);
    background: #34383c;
    padding: 1px 6px;
    border-radius: var(--r-1);
  }
  .tag.warn {
    color: #f0d6d7;
    background: rgb(229 72 77 / 0.28);
  }
  .spacer {
    flex: 1;
  }
  .en {
    font-size: 7.5px;
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
</style>
