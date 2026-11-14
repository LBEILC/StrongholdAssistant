<script lang="ts" module>
  // Remix Icon（Apache-2.0），按需引入单个 SVG，和助手界面同一套
  import heart from 'remixicon/icons/Health & Medical/heart-3-line.svg?raw'
  import heartFill from 'remixicon/icons/Health & Medical/heart-3-fill.svg?raw'
  import download from 'remixicon/icons/System/download-2-line.svg?raw'
  import copy from 'remixicon/icons/Document/file-copy-line.svg?raw'
  import check from 'remixicon/icons/System/check-line.svg?raw'
  import close from 'remixicon/icons/System/close-line.svg?raw'
  import user from 'remixicon/icons/User & Faces/user-3-line.svg?raw'
  import star from 'remixicon/icons/System/star-fill.svg?raw'
  import starLine from 'remixicon/icons/System/star-line.svg?raw'
  import arrowLeft from 'remixicon/icons/Arrows/arrow-left-s-line.svg?raw'
  import mail from 'remixicon/icons/Business/mail-line.svg?raw'
  import shield from 'remixicon/icons/System/shield-check-line.svg?raw'
  import trash from 'remixicon/icons/System/delete-bin-line.svg?raw'
  import pencil from 'remixicon/icons/Design/pencil-line.svg?raw'
  import eyeOff from 'remixicon/icons/System/eye-off-line.svg?raw'
  import eye from 'remixicon/icons/System/eye-line.svg?raw'
  import forbid from 'remixicon/icons/System/forbid-line.svg?raw'
  import github from 'remixicon/icons/Logos/github-line.svg?raw'
  import external from 'remixicon/icons/System/external-link-line.svg?raw'
  import chat from 'remixicon/icons/Communication/chat-3-line.svg?raw'
  import flag from 'remixicon/icons/Business/flag-2-line.svg?raw'
  import reply from 'remixicon/icons/Business/reply-line.svg?raw'
  import bell from 'remixicon/icons/Media/notification-3-line.svg?raw'
  import award from 'remixicon/icons/Business/award-line.svg?raw'
  import search from 'remixicon/icons/System/search-line.svg?raw'

  const icons = { heart, heartFill, download, copy, check, close, user, star, starLine, arrowLeft, mail, shield, trash, pencil, eyeOff, eye, forbid, github, external, chat, flag, reply, bell, award, search }
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
