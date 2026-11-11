<script lang="ts">
  import { app } from '../lib/state.svelte'
  import { asset, ROMAN } from '../lib/assets'
  import Icon from './Icon.svelte'

  // 小头像格：阵容、禁用列表里用。点一下切换「已持有」或看详情，由调用方决定
  interface Props {
    name: string
    size?: number
    owned?: boolean
    dim?: boolean
    /** 转职盟约：左下角小徽记 */
    transform?: string
    showName?: boolean
    onclick?: () => void
  }
  let { name, size = 48, owned = false, dim = false, transform, showName = true, onclick }: Props = $props()
  const op = $derived(app.op(name))
  const banned = $derived(app.banned.has(name))
</script>

<button class="tile" class:owned class:dim class:banned style:--s="{size}px" style:--c="var(--tier-{op.tier})" {onclick} title="{name} · {ROMAN[op.tier]} 阶">
  <span class="pic">
    <img class="av" src={asset.operator(name)} alt="" loading="lazy" />
    {#if owned}<span class="check"><Icon name="check" size={11} /></span>{/if}
    {#if banned}<img class="ban" src={asset.stage('img_chess_banned')} alt="禁用" />{/if}
    {#if transform}<span class="tf" title="转职：{app.cov(transform).name}"><span class="glyph" style:--src="url({asset.bond(transform)})"></span></span>{/if}
  </span>
  {#if showName}<span class="nm">{name}</span>{/if}
</button>

<style>
  .tile {
    display: inline-flex;
    flex-direction: column;
    align-items: center;
    gap: 3px;
    width: var(--s);
  }
  .pic {
    position: relative;
    width: var(--s);
    height: var(--s);
    background: var(--bg-3);
    box-shadow:
      inset 0 0 0 1px var(--line),
      inset 0 -2px 0 var(--c);
    overflow: hidden;
    transition: box-shadow var(--t-fast);
  }
  .av {
    width: 100%;
    height: 100%;
    object-fit: cover;
    transition:
      filter var(--t-mid),
      opacity var(--t-mid);
  }
  .tile:hover .pic {
    box-shadow:
      inset 0 0 0 1px var(--fg-3),
      inset 0 -2px 0 var(--c);
  }
  .dim .av {
    filter: grayscale(0.6) brightness(0.7);
  }
  .owned .pic {
    box-shadow:
      inset 0 0 0 1.5px var(--mint),
      0 0 8px rgb(34 227 178 / 0.3);
  }
  .check {
    position: absolute;
    right: 0;
    top: 0;
    width: 18px;
    height: 18px;
    display: flex;
    justify-content: flex-end;
    padding: 1px;
    color: var(--mint-ink);
    background: var(--mint);
    clip-path: polygon(0 0, 100% 0, 100% 100%);
  }
  .banned .av {
    filter: grayscale(1) brightness(0.45);
  }
  .banned .pic::after {
    content: '';
    position: absolute;
    inset: 0;
    background: repeating-linear-gradient(-45deg, rgb(229 72 77 / 0.35) 0 2px, transparent 2px 6px);
  }
  .ban {
    position: absolute;
    right: 0;
    top: 0;
    z-index: 1;
    width: 50%;
    height: 50%;
    max-width: 26px;
    max-height: 26px;
  }
  .tf {
    position: absolute;
    left: 1px;
    bottom: 3px;
    z-index: 1;
    width: 15px;
    height: 15px;
    display: grid;
    place-items: center;
    border-radius: 50%;
    color: var(--gold-ink);
    background: var(--gold);
    box-shadow: 0 0 0 1px rgb(0 0 0 / 0.6);
  }
  .tf .glyph {
    width: 11px;
    height: 11px;
  }
  .nm {
    font-size: 11px;
    color: var(--fg-2);
    max-width: calc(var(--s) + 8px);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .banned .nm {
    color: var(--fg-4);
    text-decoration: line-through;
  }
</style>
