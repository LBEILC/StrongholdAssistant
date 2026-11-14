<script lang="ts">
  import { app } from '../lib/state.svelte'
  import { asset } from '../lib/assets'
  import TierBadge from './TierBadge.svelte'
  import Icon from './Icon.svelte'
  import { openMenu, operatorMenu } from '../lib/menu.svelte'

  // 图鉴里的干员卡，仿商店卡：头像、左上等阶、底部盟约 + 名字
  let { name, selected = false, onclick }: { name: string; selected?: boolean; onclick: () => void } = $props()
  const op = $derived(app.op(name))
  const banned = $derived(app.banned.has(name))
  const tracked = $derived(app.tracked.has(name))
  const focus = $derived(app.focusOps.has(name))
</script>

<button
  class="card"
  class:banned
  class:tracked
  class:selected
  style:--c="var(--tier-{op.tier})"
  style:--bg="url({asset.card('bg_common')})"
  style:--frame="url({asset.skin(op.tier >= 5 ? `frame_${op.tier}` : 'frame')})"
  {onclick} oncontextmenu={(e) => openMenu(e, operatorMenu(name))}>
  <span class="pic">
    <img src={asset.operator(name)} alt="" loading="lazy" />
  </span>
  <span class="tier"><TierBadge tier={op.tier} size={17} /></span>
  {#if tracked}<span class="trk" title="追踪中：商店里会标记">追踪</span>{/if}
  {#if banned}<span class="ban term">BANNED</span>{/if}
  {#if focus}<span class="fav" title="特别关注"><Icon name="heartFill" size={10} /></span>{/if}
  <span class="foot">
    <span class="bonds">
      {#each [...op.core, ...op.extra] as c (c)}
        <span class="glyph b" class:core={app.cov(c).kind === 'core'} style:--src="url({asset.bond(c)})" title={app.cov(c).name}></span>
      {/each}
    </span>
    <span class="nm">{name}</span>
  </span>
</button>

<style>
  .fav {
    position: absolute;
    left: 4px;
    top: 24px;
    width: 16px;
    height: 16px;
    display: grid;
    place-items: center;
    border-radius: 50%;
    color: var(--gold-ink);
    background: var(--gold);
    box-shadow: 0 0 0 1px rgb(var(--shade-rgb) / 0.6);
  }
  .card {
    position: relative;
    display: flex;
    flex-direction: column;
    text-align: left;
    /* 卡框用皮肤位图（1～4 阶一种、5 阶、6 阶），垫在头像下面；和游戏卡一样宽高比 0.81，直接铺满 */
    background:
      var(--frame) 0 0 / 100% 100% no-repeat,
      var(--bg) center / cover,
      var(--bg-2);
    outline: 1.5px solid transparent;
    outline-offset: -1.5px;
    overflow: hidden;
    transition:
      outline-color var(--t-fast),
      box-shadow var(--t-fast),
      filter var(--t-fast);
  }
  .card:hover {
    filter: brightness(1.12);
  }
  .card.selected {
    outline-color: var(--fg-1);
  }
  .card.tracked {
    outline-color: var(--mint);
    box-shadow: 0 0 10px rgb(var(--mint-rgb) / 0.25);
  }
  /* 经典（助手原来的样子）：不用卡框位图，细边框加顶上一道等阶色 */
  :global([data-skin='classic']) .card {
    background: var(--bg) center / cover, var(--bg-2);
    outline: none;
    box-shadow:
      inset 0 0 0 1px var(--line),
      inset 0 2px 0 var(--c);
  }
  :global([data-skin='classic']) .card:hover {
    filter: none;
    box-shadow:
      inset 0 0 0 1px var(--line-hi),
      inset 0 2px 0 var(--c);
  }
  :global([data-skin='classic']) .card.selected {
    box-shadow:
      inset 0 0 0 1.5px var(--fg-1),
      inset 0 2px 0 var(--c);
  }
  :global([data-skin='classic']) .card.tracked {
    box-shadow:
      inset 0 0 0 1.5px var(--mint),
      inset 0 2px 0 var(--c),
      0 0 10px rgb(var(--mint-rgb) / 0.25);
  }
  :global([data-skin='classic']) .pic {
    margin: 0;
  }
  .pic {
    margin: 2px 2px 0;
    aspect-ratio: 1;
    overflow: hidden;
    -webkit-mask: linear-gradient(180deg, #000 70%, transparent);
    mask: linear-gradient(180deg, #000 70%, transparent);
  }
  .pic img {
    width: 100%;
    height: 100%;
    object-fit: cover;
  }
  .tier {
    position: absolute;
    left: 2px;
    top: 4px;
  }
  .trk {
    position: absolute;
    right: 0;
    top: 2px;
    font-size: var(--fs-xs);
    font-weight: var(--fw-bold);
    line-height: 1;
    padding: 3px 5px;
    color: var(--mint-ink);
    background: var(--mint);
  }
  .foot {
    margin-top: -16px;
    position: relative;
    padding: 0 5px 5px;
    display: flex;
    flex-direction: column;
    gap: 1px;
  }
  .bonds {
    display: flex;
    gap: 3px;
    height: 13px;
  }
  .b {
    width: 13px;
    height: 13px;
    color: var(--card-bond-fg);
  }
  .b.core {
    color: var(--card-bond-core-fg);
  }
  /* 名字条随皮肤：终端直接压在卡框的青绿条上，乌萨斯是深青灰斜条，可露希尔是白条 */
  .nm {
    margin: 0 -5px;
    padding: 1px 5px;
    font-size: var(--fs-base);
    font-weight: var(--fw-semi);
    color: var(--card-name-fg);
    background: var(--card-name-bg);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .banned .pic img {
    filter: grayscale(1) brightness(0.4);
  }
  .banned .nm {
    color: var(--fg-3);
  }
  .ban {
    position: absolute;
    left: 0;
    right: 0;
    top: 38%;
    text-align: center;
    font-size: var(--fs-xs);
    color: var(--fg-0);
    background: rgb(var(--red-rgb) / 0.85);
    padding: 2px 0 1px;
    letter-spacing: 0.3em;
  }

  /* 可露希尔：游戏商店卡右下角都有一颗四角星；追踪中的卡换成粉色并闪烁 */
  :global([data-skin='closure']) .card::after {
    content: '';
    position: absolute;
    right: -2px;
    bottom: -2px;
    width: 22px;
    height: 22px;
    background: var(--win-cyan);
    mask: var(--skin-sparkle) center / contain no-repeat;
    pointer-events: none;
  }
  :global([data-skin='closure']) .card.tracked::after {
    background: var(--win-pink);
    animation: card-twinkle 2.4s ease-in-out infinite;
  }
  @keyframes card-twinkle {
    50% {
      transform: scale(0.7) rotate(15deg);
      opacity: 0.6;
    }
  }
</style>
