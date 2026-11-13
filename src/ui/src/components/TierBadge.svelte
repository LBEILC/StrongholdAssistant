<script lang="ts">
  import { asset, ROMAN } from '../lib/assets'
  import { skin } from '../lib/skin.svelte'

  // 罗马数字等阶角标，用皮肤位图（每套皮肤样子不同）。size 是终端方角标的边长，其他皮肤按 --tier-h 换算高度、宽度随图。
  // 可露希尔的高阶角标是拼的（游戏里也是）：5、6 阶右上角和左下角各一颗星，按描边染色（5 阶金、6 阶青）；
  // 6 阶底板不带字，粉青渐变透过「VI」字形显示，跟牌子一起逆时针歪 12°（照 5 阶的「V」量的，见 docs/design.md）
  let { tier, size = 22 }: { tier: number; size?: number } = $props()
  const fx = $derived(skin.id === 'closure' && tier >= 5)
</script>

<span class="tb" style:--s="{size}px" title="{ROMAN[tier]} 阶">
  <span class="plate">
    <img src={asset.skin(`tier_${tier}`)} alt={ROMAN[tier]} />
    {#if fx && tier === 6}
      <span class="vi" style:--mask="url({asset.skin('tier6_mask')})" style:--grad="url({asset.skin('tier6_grad')})"></span>
    {/if}
    {#if fx}
      <span class="star tr" class:six={tier === 6} style:--src="url({asset.skin('tier_star')})"></span>
      <span class="star bl" class:six={tier === 6} style:--src="url({asset.skin('tier_star')})"></span>
    {/if}
  </span>
</span>

<style>
  .tb {
    display: inline-grid;
    place-items: center;
    min-width: var(--s);
    height: var(--s);
    flex: none;
  }
  .plate {
    position: relative;
    display: block;
  }
  img {
    height: calc(var(--s) * var(--tier-h));
    width: auto;
    filter: drop-shadow(0 1px 1px rgb(var(--shade-rgb) / 0.6));
  }
  /* 「VI」：遮罩图 28×20，字形在其中 (2,1)～(26,18)；中心对齐 5 阶「V」的 (46%, 39%) */
  .vi {
    position: absolute;
    left: 22.1%;
    top: 19%;
    width: 47.8%;
    height: 40%;
    background: var(--grad) 0 0 / 100% 200% repeat-y;
    mask: var(--mask) center / 100% 100% no-repeat;
    transform: rotate(-12deg);
    animation: vi-flow 3s linear infinite;
  }
  @keyframes vi-flow {
    to {
      background-position: 0 -200%;
    }
  }
  .star {
    position: absolute;
    width: 42%;
    aspect-ratio: 1;
    background: var(--gold);
    mask: var(--src) center / contain no-repeat;
    animation: star-twinkle 2.4s ease-in-out infinite;
  }
  /* 6 阶牌子是青色描边，星星也是青色（可露希尔的强调色） */
  .star.six {
    background: var(--mint);
  }
  .star.tr {
    right: -14%;
    top: -18%;
  }
  .star.bl {
    left: -12%;
    bottom: -14%;
    animation-delay: -1.2s;
  }
  @keyframes star-twinkle {
    50% {
      opacity: 0.35;
      transform: scale(0.7);
    }
  }
</style>
