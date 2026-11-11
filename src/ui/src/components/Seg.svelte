<script lang="ts" generics="T extends string">
  // 二级分段切换（阵容的目标 / 我的阵容，图鉴的干员 / 装备 / 策略）：底部亮条滑到选中项，和顶部标签同一种动效
  interface Props {
    items: { id: T; label: string; count?: number }[]
    value: T
    onchange: (v: T) => void
    height?: number
  }
  let { items, value, onchange, height = 28 }: Props = $props()
  const idx = $derived(items.findIndex((i) => i.id === value))
</script>

<div class="seg" style:--n={items.length} style:--h="{height}px">
  <span class="pill" style:transform="translateX({idx * 100}%)"></span>
  {#each items as it (it.id)}
    <button class:on={it.id === value} onclick={() => onchange(it.id)}>
      {it.label}
      {#if it.count !== undefined}<em>{it.count}</em>{/if}
    </button>
  {/each}
</div>

<style>
  .seg {
    position: relative;
    display: grid;
    grid-template-columns: repeat(var(--n), 1fr);
    background: var(--bg-2);
    padding: 2px;
  }
  /* 选中底块 + 底部亮条，一起滑动 */
  .pill {
    position: absolute;
    left: 2px;
    top: 2px;
    bottom: 2px;
    width: calc((100% - 4px) / var(--n));
    background: var(--bg-4);
    box-shadow: inset 0 -2px 0 var(--mint);
    transition: transform var(--t-mid) var(--ease-out);
    pointer-events: none;
  }
  button {
    position: relative;
    height: var(--h);
    font-size: 13px;
    font-weight: 600;
    color: var(--fg-3);
    transition: color var(--t-mid);
  }
  button:hover {
    color: var(--fg-2);
  }
  button.on {
    color: var(--fg-1);
  }
  em {
    font-style: normal;
    font-family: var(--font-term);
    font-size: 11px;
    margin-left: 3px;
    color: var(--fg-4);
    transition: color var(--t-mid);
  }
  button.on em {
    color: var(--mint);
  }
</style>
