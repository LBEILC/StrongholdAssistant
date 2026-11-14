<script lang="ts" generics="T extends string">
  // 二级分段切换（阵容的目标 / 我的阵容，图鉴的干员 / 装备 / 策略）：底部亮条滑到选中项，和顶部标签同一种动效
  interface Props {
    items: { id: T; label: string; count?: number; dot?: boolean }[]
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
      {#if it.dot}<i class="dot"></i>{/if}
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
    font-size: var(--fs-md);
    font-weight: var(--fw-semi);
    color: var(--fg-3);
    transition: color var(--t-mid);
  }
  button:hover {
    color: var(--fg-2);
  }
  button.on {
    color: var(--fg-1);
  }
  /* 小红点：有待处理的事（如有新版本） */
  .dot {
    position: absolute;
    top: 4px;
    width: 6px;
    height: 6px;
    margin-left: 2px;
    border-radius: 50%;
    background: var(--red);
  }
  em {
    font-style: normal;
    font-family: var(--font-term);
    font-size: var(--fs-sm);
    margin-left: 3px;
    color: var(--fg-4);
    transition: color var(--t-mid);
  }
  button.on em {
    color: var(--mint);
  }

  /* 可露希尔：胶囊轨道，选中项是带光泽的亮青胶囊（和主按钮同一种） */
  :global([data-skin='closure']) .seg {
    padding: 3px;
    border-radius: var(--r-pill);
    background: var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line-strong);
  }
  :global([data-skin='closure']) .pill {
    left: 3px;
    top: 3px;
    bottom: 3px;
    width: calc((100% - 6px) / var(--n));
    border-radius: var(--r-pill);
    background:
      linear-gradient(180deg, rgb(var(--hl-rgb) / 0.55), rgb(var(--hl-rgb) / 0.05) 48%, transparent 52%),
      linear-gradient(180deg, var(--mint-hi), var(--mint) 55%, var(--mint-lo));
    box-shadow: 0 0 8px rgb(var(--mint-rgb) / 0.4);
  }
  :global([data-skin='closure']) button.on {
    color: var(--mint-ink);
  }
  :global([data-skin='closure']) button.on em {
    color: rgb(var(--bg-0-rgb) / 0.6);
  }

  /* 乌萨斯：选中项是滑动的铁锈红平行四边形，字米白斜体 */
  :global([data-skin='ursus']) .seg {
    background: var(--bg-1);
    box-shadow: inset 0 -2px 0 rgb(var(--rust-rgb) / 0.4);
  }
  :global([data-skin='ursus']) .pill {
    background: var(--noise), linear-gradient(180deg, var(--rust-hi), var(--rust));
    box-shadow: none;
    clip-path: polygon(var(--slant) 0, 100% 0, calc(100% - var(--slant)) 100%, 0 100%);
  }
  :global([data-skin='ursus']) button.on {
    color: var(--cream);
    font-style: italic;
  }
  :global([data-skin='ursus']) button.on em {
    color: rgb(240 228 200 / 0.7);
  }
</style>
