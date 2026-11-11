<script lang="ts">
  import { app } from '../lib/state.svelte'
  import { asset } from '../lib/assets'
  import Icon from './Icon.svelte'

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

<button class="bond" class:on class:focus class:clickable={!!onclick} style:--s="{size}px" {onclick} title={cov.name}>
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
    color: #8a8f93;
    background: radial-gradient(circle at 50% 40%, #3b3f43, #26292c 70%);
    box-shadow:
      inset 0 0 0 1.5px #44494d,
      inset 0 -3px 6px rgb(0 0 0 / 0.5),
      0 0 0 2px rgb(0 0 0 / 0.55);
    transition:
      background var(--t-mid),
      box-shadow var(--t-mid),
      transform var(--t-fast) var(--ease-out);
  }
  .clickable:hover .disc {
    transform: scale(1.06);
  }
  .on .disc {
    color: var(--mint-ink);
    background: radial-gradient(circle at 50% 38%, var(--mint-hi), var(--mint) 55%, var(--mint-lo));
    box-shadow:
      inset 0 0 0 1.5px rgb(255 255 255 / 0.25),
      inset 0 -3px 6px rgb(0 60 45 / 0.45),
      0 0 0 2px rgb(0 0 0 / 0.55),
      var(--mint-glow);
  }
  .disc > .glyph {
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
    stroke: rgb(255 255 255 / 0.07);
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
    box-shadow: 0 0 0 1.5px rgb(0 0 0 / 0.6);
  }
  .name {
    font-size: 12px;
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
