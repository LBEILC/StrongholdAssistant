<script lang="ts">
  import { app } from '../lib/state.svelte'
  import { asset } from '../lib/assets'
  import Icon from './Icon.svelte'
  import { openMenu, covenantMenu } from '../lib/menu.svelte'

  interface Props {
    id: string
    size?: number
    /** on：薄荷绿发光；off：灰 */
    on?: boolean
    /** 有所含干员被禁用：右上角小标记 */
    incomplete?: boolean
    /** 0–1，画外圈进度；不传不画 */
    progress?: number
    name?: boolean
    onclick?: () => void
  }
  let { id, size = 44, on = false, incomplete = false, progress, name = false, onclick }: Props = $props()
  const cov = $derived(app.cov(id))
  const focus = $derived(app.focusCovs.has(id))
  const R = 47
  const C = 2 * Math.PI * R
</script>

<button
  class="bond"
  class:on
  class:focus
  class:clickable={!!onclick}
  style:--s="{size}px"
  style:--disc-on="url({asset.skin('bond_on')})"
  style:--disc-off="url({asset.skin('bond_off')})"
  {onclick} oncontextmenu={(e) => openMenu(e, covenantMenu(id))} title={name ? undefined : cov.name}>
  <span class="disc">
    {#if progress !== undefined}
      <svg class="ring" viewBox="0 0 100 100">
        <circle cx="50" cy="50" r={R} class="track" />
        <circle cx="50" cy="50" r={R} class="fill" stroke-dasharray="{C * Math.min(progress, 1)} {C}" />
      </svg>
    {/if}
    <span class="glyph" style:--src="url({asset.bond(id)})"></span>
    {#if focus}<span class="fav" title="特别关注"><Icon name="heartFill" size={Math.max(8, Math.round(size * 0.2))} /></span>{/if}
    {#if incomplete}
      <img class="mark" src={asset.stage('img_bond_banned')} alt="阵容不完整" />
    {/if}
  </span>
  {#if name}<span class="name">{cov.name}</span>{/if}
</button>

<style>
  .bond {
    display: inline-flex;
    flex-direction: column;
    align-items: center;
    gap: 4px;
    cursor: default;
    min-width: 0;
  }
  .clickable {
    cursor: pointer;
  }
  .disc {
    position: relative;
    width: var(--s);
    height: var(--s);
    border-radius: 50%;
    display: grid;
    place-items: center;
    isolation: isolate;
    color: var(--bond-off);
    transition:
      color var(--t-mid),
      transform var(--t-fast) var(--ease-out);
  }
  /* 圆盘用皮肤位图：未激活、激活两层交叉淡入淡出。
     素材带一圈发光，圆盘本体在 75px 图里直径 39、65px 图里直径 31，放大到本体正好等于 --s */
  .disc::before,
  .disc::after {
    content: '';
    position: absolute;
    z-index: -1;
    pointer-events: none;
    transition: opacity var(--t-mid);
  }
  .disc::before {
    inset: calc(var(--s) * -17 / 31);
    background: var(--disc-off) center / 100% 100% no-repeat;
  }
  .disc::after {
    inset: calc(var(--s) * -18 / 39);
    background: var(--disc-on) center / 100% 100% no-repeat;
    opacity: 0;
  }
  .on .disc::before {
    opacity: 0;
  }
  .on .disc::after {
    opacity: 1;
  }
  /* 经典（助手原来的样子）：圆盘用 CSS 画，不用位图 */
  :global([data-skin='classic']) .disc {
    background: radial-gradient(circle at 50% 40%, var(--bond-off-top), var(--bond-off-bottom) 70%);
    box-shadow:
      inset 0 0 0 1.5px var(--bond-off-ring),
      inset 0 -3px 6px rgb(var(--shade-rgb) / 0.5),
      0 0 0 2px rgb(var(--shade-rgb) / 0.55);
    transition:
      color var(--t-mid),
      background var(--t-mid),
      box-shadow var(--t-mid),
      transform var(--t-fast) var(--ease-out);
  }
  :global([data-skin='classic']) .on .disc {
    background: radial-gradient(circle at 50% 38%, var(--mint-hi), var(--mint) 55%, var(--mint-lo));
    box-shadow:
      inset 0 0 0 1.5px rgb(var(--hl-rgb) / 0.25),
      inset 0 -3px 6px var(--bond-on-shade),
      0 0 0 2px rgb(var(--shade-rgb) / 0.55),
      var(--mint-glow);
  }
  :global([data-skin='classic']) .disc::before,
  :global([data-skin='classic']) .disc::after {
    display: none;
  }
  .clickable:hover .disc {
    transform: scale(1.06);
  }
  .on .disc {
    color: var(--bond-on-fg);
  }
  .disc > .glyph {
    position: relative;
    width: 64%;
    height: 64%;
  }
  .ring {
    position: absolute;
    inset: -5px;
    width: calc(100% + 10px);
    height: calc(100% + 10px);
    transform: rotate(-90deg);
  }
  .ring circle {
    fill: none;
    stroke-width: 4;
  }
  .ring .track {
    stroke: rgb(var(--hl-rgb) / 0.07);
  }
  .ring .fill {
    stroke: var(--mint);
    transition: stroke-dasharray var(--t-mid) var(--ease-out);
  }
  .mark {
    /* 游戏里「部分盟约所含干员阵容不完整」的角标 */
    position: absolute;
    top: -5px;
    right: -9px;
    width: max(16px, calc(var(--s) * 0.48));
    height: max(16px, calc(var(--s) * 0.48));
  }
  /* 特别关注：金色外圈 + 左下角爱心 */
  .focus .disc {
    outline: 1.5px solid var(--gold);
    outline-offset: 2px;
  }
  .fav {
    position: absolute;
    left: -4px;
    bottom: -3px;
    width: max(13px, calc(var(--s) * 0.32));
    height: max(13px, calc(var(--s) * 0.32));
    display: grid;
    place-items: center;
    border-radius: 50%;
    color: var(--gold-ink);
    background: var(--gold);
    box-shadow: 0 0 0 1.5px rgb(var(--shade-rgb) / 0.6);
  }
  .name {
    font-size: var(--fs-base);
    color: var(--fg-2);
    white-space: nowrap;
    max-width: calc(var(--s) + 22px);
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .on .name {
    color: var(--fg-1);
  }
</style>
