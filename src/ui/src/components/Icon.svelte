<script lang="ts" module>
  // 游戏素材里没有的通用图标用 Remix Icon（Apache-2.0），按需引入单个 SVG
  import settings from 'remixicon/icons/System/settings-3-line.svg?raw'
  import subtract from 'remixicon/icons/System/subtract-line.svg?raw'
  import close from 'remixicon/icons/System/close-line.svg?raw'
  import refresh from 'remixicon/icons/System/refresh-line.svg?raw'
  import search from 'remixicon/icons/System/search-line.svg?raw'
  import grid from 'remixicon/icons/Design/layout-grid-line.svg?raw'
  import focus from 'remixicon/icons/Design/focus-3-line.svg?raw'
  import add from 'remixicon/icons/System/add-line.svg?raw'
  import star from 'remixicon/icons/System/star-line.svg?raw'
  import starFill from 'remixicon/icons/System/star-fill.svg?raw'
  import check from 'remixicon/icons/System/check-line.svg?raw'
  import arrowRight from 'remixicon/icons/Arrows/arrow-right-s-line.svg?raw'
  import arrowDown from 'remixicon/icons/Arrows/arrow-down-s-line.svg?raw'
  import contractLeft from 'remixicon/icons/Arrows/contract-left-line.svg?raw'
  import expandRight from 'remixicon/icons/Arrows/expand-right-line.svg?raw'
  import pencil from 'remixicon/icons/Design/pencil-line.svg?raw'
  import trash from 'remixicon/icons/System/delete-bin-line.svg?raw'
  import lock from 'remixicon/icons/System/lock-line.svg?raw'
  import lockFill from 'remixicon/icons/System/lock-fill.svg?raw'
  import forbid from 'remixicon/icons/System/forbid-line.svg?raw'
  import magic from 'remixicon/icons/Design/magic-line.svg?raw'
  import scan from 'remixicon/icons/Device/scan-line.svg?raw'
  import stop from 'remixicon/icons/Media/stop-fill.svg?raw'
  import heart from 'remixicon/icons/Health & Medical/heart-3-line.svg?raw'
  import heartFill from 'remixicon/icons/Health & Medical/heart-3-fill.svg?raw'

  const icons = { settings, subtract, close, refresh, search, grid, focus, add, star, starFill, check, arrowRight, arrowDown, contractLeft, expandRight, pencil, trash, lock, lockFill, forbid, magic, scan, stop, heart, heartFill }
  export type IconName = keyof typeof icons
</script>

<script lang="ts">
  let { name, size = 16 }: { name: IconName; size?: number } = $props()
</script>

<span class="ri" style:--s="{size}px" aria-hidden="true">{@html icons[name]}</span>

<style>
  .ri {
    display: inline-grid;
    place-items: center;
    width: var(--s);
    height: var(--s);
    flex: none;
  }
  .ri :global(svg) {
    width: 100%;
    height: 100%;
    fill: currentColor;
  }
</style>
