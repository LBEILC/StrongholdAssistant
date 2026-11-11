<script lang="ts">
  import { app } from '../lib/state.svelte'
  import { asset } from '../lib/assets'
  import TierBadge from './TierBadge.svelte'

  // 图鉴里的干员卡，仿商店卡：头像、左上等阶、底部盟约 + 名字
  let { name, selected = false, onclick }: { name: string; selected?: boolean; onclick: () => void } = $props()
  const op = $derived(app.op(name))
  const banned = $derived(app.banned.has(name))
  const tracked = $derived(app.tracked.has(name))
  const owned = $derived(app.owned.has(name))
</script>

<button class="card" class:banned class:tracked class:selected style:--c="var(--tier-{op.tier})" style:--bg="url({asset.card('bg_common')})" {onclick}>
  <span class="pic">
    <img src={asset.operator(name)} alt="" loading="lazy" />
  </span>
  <span class="tier"><TierBadge tier={op.tier} size={17} /></span>
  {#if tracked}<span class="trk" title={owned ? '已持有' : '追踪中'}>{owned ? '持有' : '追踪'}</span>{/if}
  {#if banned}<span class="ban term">BANNED</span>{/if}
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
  .card {
    position: relative;
    display: flex;
    flex-direction: column;
    text-align: left;
    background: var(--bg) center / cover, var(--bg-2);
    box-shadow:
      inset 0 0 0 1px #2f3337,
      inset 0 2px 0 var(--c);
    overflow: hidden;
    transition:
      box-shadow var(--t-fast),
      transform var(--t-fast) var(--ease-out);
  }
  .card:hover {
    box-shadow:
      inset 0 0 0 1px #5a6066,
      inset 0 2px 0 var(--c);
  }
  .card.selected {
    box-shadow:
      inset 0 0 0 1.5px var(--fg-1),
      inset 0 2px 0 var(--c);
  }
  .card.tracked {
    box-shadow:
      inset 0 0 0 1.5px var(--mint),
      inset 0 2px 0 var(--c),
      0 0 10px rgb(34 227 178 / 0.25);
  }
  .pic {
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
    font-size: 10px;
    font-weight: 700;
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
    color: var(--fg-3);
  }
  .b.core {
    color: var(--fg-1);
  }
  .nm {
    font-size: 12px;
    font-weight: 600;
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
    font-size: 10px;
    color: #fff;
    background: rgb(229 72 77 / 0.85);
    padding: 2px 0 1px;
    letter-spacing: 0.3em;
  }
</style>
