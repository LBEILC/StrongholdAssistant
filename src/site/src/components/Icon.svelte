<script lang="ts" module>
  // Remix Icon（Apache-2.0），按需引入单个 SVG，和助手界面、广场同一套
  import download from 'remixicon/icons/System/download-2-line.svg?raw'
  import external from 'remixicon/icons/System/external-link-line.svg?raw'
  import github from 'remixicon/icons/Logos/github-fill.svg?raw'
  import qq from 'remixicon/icons/Logos/qq-fill.svg?raw'
  import arrow from 'remixicon/icons/Arrows/arrow-right-line.svg?raw'
  import arrowUp from 'remixicon/icons/Arrows/arrow-right-up-line.svg?raw'
  import menu from 'remixicon/icons/System/menu-line.svg?raw'
  import close from 'remixicon/icons/System/close-line.svg?raw'
  import play from 'remixicon/icons/Media/play-circle-line.svg?raw'
  import windows from 'remixicon/icons/Logos/windows-fill.svg?raw'
  import flash from 'remixicon/icons/Weather/flashlight-line.svg?raw'
  import group from 'remixicon/icons/User & Faces/group-line.svg?raw'
  import shield from 'remixicon/icons/System/shield-check-line.svg?raw'
  import history from 'remixicon/icons/System/history-line.svg?raw'
  import masonry from 'remixicon/icons/Design/layout-masonry-line.svg?raw'
  import share from 'remixicon/icons/System/share-line.svg?raw'
  import route from 'remixicon/icons/Map/route-line.svg?raw'
  import keyboard from 'remixicon/icons/Device/keyboard-line.svg?raw'
  import cloud from 'remixicon/icons/Business/cloud-line.svg?raw'
  import book from 'remixicon/icons/Document/book-open-line.svg?raw'
  import question from 'remixicon/icons/System/question-line.svg?raw'
  import plus from 'remixicon/icons/System/add-line.svg?raw'
  import minus from 'remixicon/icons/System/subtract-line.svg?raw'
  import check from 'remixicon/icons/System/check-line.svg?raw'
  import copy from 'remixicon/icons/Document/file-copy-line.svg?raw'
  import chat from 'remixicon/icons/Communication/chat-3-line.svg?raw'
  import scales from 'remixicon/icons/Others/scales-3-line.svg?raw'
  import image from 'remixicon/icons/Media/image-line.svg?raw'
  import chevron from 'remixicon/icons/Arrows/arrow-down-s-line.svg?raw'
  import community from 'remixicon/icons/Buildings/community-line.svg?raw'
  import chart from 'remixicon/icons/Business/bar-chart-box-line.svg?raw'
  import alert from 'remixicon/icons/System/alert-line.svg?raw'
  import discuss from 'remixicon/icons/Communication/discuss-line.svg?raw'
  import compass from 'remixicon/icons/Map/compass-3-line.svg?raw'
  import lock from 'remixicon/icons/System/lock-line.svg?raw'

  const icons = { download, external, github, qq, arrow, arrowUp, menu, close, play, windows, flash, group, shield, history, masonry, share, route, keyboard, cloud, book, question, plus, minus, check, copy, chat, scales, image, chevron, community, chart, alert, discuss, compass, lock }
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
