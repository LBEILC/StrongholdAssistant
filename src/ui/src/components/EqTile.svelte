<script lang="ts">
  import { app } from '../lib/state.svelte'
  import { asset, ROMAN } from '../lib/assets'
  import { openMenu, equipmentMenu } from '../lib/menu.svelte'
  import Icon from './Icon.svelte'

  // 装备小格：和干员小格同尺寸，图标留边居中；右上角星标 = 在商店里标记（追踪或在目标里）
  interface Props {
    name: string
    size?: number
    showName?: boolean
    /** 编辑器里表示已选 */
    picked?: boolean
    /** 变暗（目标页里没在追踪的） */
    dim?: boolean
    /** 右键弹装备菜单；编辑器里关掉 */
    menu?: boolean
    onclick?: () => void
  }
  let { name, size = 48, showName = true, picked = false, dim = false, menu = true, onclick }: Props = $props()
  const e = $derived(app.eq(name))
  const tracked = $derived(app.trackedEq.has(name))
</script>

<button
  class="tile"
  class:picked
  class:dim
  style:--s="{size}px"
  style:--c="var(--tier-{e.tier})"
  {onclick}
  oncontextmenu={menu ? (ev) => openMenu(ev, equipmentMenu(name)) : undefined}
  title="{name} · {ROMAN[e.tier]} 阶装备"
>
  <span class="pic">
    <img src={asset.equipment(name)} alt="" loading="lazy" />
    {#if picked}<span class="check"><Icon name="check" size={11} /></span>{:else if tracked && menu}<span class="trk"><Icon name="starFill" size={9} /></span>{/if}
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
    display: grid;
    place-items: center;
    background: radial-gradient(circle at 50% 45%, #2c3034, var(--bg-2) 70%);
    box-shadow:
      inset 0 0 0 1px var(--line),
      inset 0 -2px 0 var(--c);
    transition: box-shadow var(--t-fast);
  }
  .pic img {
    width: 78%;
    height: 78%;
    object-fit: contain;
  }
  .dim img {
    filter: grayscale(0.7) brightness(0.6);
  }
  .tile:hover .pic {
    box-shadow:
      inset 0 0 0 1px var(--fg-3),
      inset 0 -2px 0 var(--c);
  }
  .picked .pic {
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
  .trk {
    position: absolute;
    right: 1px;
    top: 1px;
    width: 13px;
    height: 13px;
    display: grid;
    place-items: center;
    color: var(--mint-ink);
    background: var(--mint);
  }
  .nm {
    font-size: 11px;
    color: var(--fg-2);
    max-width: calc(var(--s) + 8px);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
</style>
