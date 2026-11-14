<script lang="ts">
  // 五颗星：显示平均分（可以有半颗，按比例填色），或者传 onpick 当打分用（悬停预览）
  let { value = 0, size = 14, onpick }: { value?: number; size?: number; onpick?: (n: 1 | 2 | 3 | 4 | 5) => void } = $props()
  let hover = $state(0)
  const shown = $derived(hover || value)
  const fill = (i: number) => Math.max(0, Math.min(1, shown - i))
  const labels = ['很弱', '偏弱', '一般', '好用', '超强']
</script>

{#if onpick}
  <span class="stars pick" style:--s="{size}px" role="radiogroup" aria-label="打分" onmouseleave={() => (hover = 0)}>
    {#each [1, 2, 3, 4, 5] as n (n)}
      <button
        type="button"
        role="radio"
        aria-checked={value === n}
        aria-label="{n} 星 · {labels[n - 1]}"
        title="{n} 星 · {labels[n - 1]}"
        onmouseenter={() => (hover = n)}
        onclick={() => onpick(n as 1 | 2 | 3 | 4 | 5)}
      ><i style:--f={fill(n - 1)}></i></button>
    {/each}
    <span class="hint">{shown ? labels[Math.ceil(shown) - 1] : '点星星打分'}</span>
  </span>
{:else}
  <span class="stars" style:--s="{size}px" aria-label="{value.toFixed(1)} 星">
    {#each [0, 1, 2, 3, 4] as i (i)}<i style:--f={fill(i)}></i>{/each}
  </span>
{/if}

<style>
  .stars {
    display: inline-flex;
    align-items: center;
    gap: calc(var(--s) * 0.12);
    line-height: 0;
  }
  /* 一颗星 = 灰底星 + 按 --f 比例裁出来的金色星 */
  i {
    position: relative;
    display: block;
    width: var(--s);
    height: var(--s);
    background: var(--bg-3, #2a2d30);
    clip-path: polygon(50% 0, 61.8% 35.4%, 100% 38.2%, 69.1% 61.8%, 80.9% 100%, 50% 76.4%, 19.1% 100%, 30.9% 61.8%, 0 38.2%, 38.2% 35.4%);
  }
  i::after {
    content: '';
    position: absolute;
    inset: 0;
    width: calc(var(--f) * 100%);
    background: var(--gold);
  }
  .pick button {
    padding: 2px;
    background: none;
    cursor: pointer;
  }
  .pick button:hover i {
    transform: scale(1.12);
  }
  .pick i {
    transition: transform 120ms;
  }
  .hint {
    margin-left: 8px;
    font-size: 13px;
    line-height: 1;
    color: var(--fg-3);
  }
</style>
