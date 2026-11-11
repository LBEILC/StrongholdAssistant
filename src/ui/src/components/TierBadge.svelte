<script lang="ts">
  import { asset, ROMAN } from '../lib/assets'

  // 方框罗马数字等阶。用游戏位图；素材撤掉时退回 CSS 方框 + 文字
  let { tier, size = 22 }: { tier: number; size?: number } = $props()
</script>

<span class="tb" style:--s="{size}px" style:--c="var(--tier-{tier})" title="{ROMAN[tier]} 阶">
  <img src={asset.tier(tier)} alt={ROMAN[tier]} />
</span>

<style>
  .tb {
    display: inline-grid;
    place-items: center;
    width: var(--s);
    height: var(--s);
    flex: none;
  }
  img {
    width: 100%;
    height: 100%;
    filter: drop-shadow(0 1px 1px rgb(0 0 0 / 0.6));
  }
</style>
