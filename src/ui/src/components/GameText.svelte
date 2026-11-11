<script lang="ts">
  // 游戏描述文字：【盟约】薄荷绿，<触发条件> 金色
  // 给了 compare 时，和 compare 同位置数字不同的数字高亮（精锐对照普通）
  let { text, compare }: { text: string; compare?: string } = $props()
  const TAG = /(【[^】]+】|<[^>]+>)/
  const NUM = /(\d+(?:\.\d+)?%?)/
  const nums = (s: string) => s.replace(/【[^】]+】|<[^>]+>/g, '').match(/\d+(?:\.\d+)?%?/g) ?? []
  const parts = $derived.by(() => {
    const ref = compare === undefined ? null : nums(compare)
    let n = 0
    return text
      .split(TAG)
      .filter(Boolean)
      .flatMap((p) => {
        if (p.startsWith('【')) return [{ t: p, k: 'k' }]
        if (p.startsWith('<')) return [{ t: p, k: 'c' }]
        return p
          .split(NUM)
          .filter(Boolean)
          .map((s) => {
            if (!/^\d/.test(s)) return { t: s, k: '' }
            const changed = ref !== null && ref[n] !== s
            n++
            return { t: s, k: changed ? 'up' : '' }
          })
      })
  })
</script>

<span class="gt">
  {#each parts as p, i (i)}
    {#if p.k}<b class={p.k}>{p.t}</b>{:else}{p.t}{/if}
  {/each}
</span>

<style>
  .gt {
    white-space: pre-line;
  }
  .k {
    font-weight: 600;
    color: var(--mint);
  }
  .c {
    font-weight: 600;
    color: var(--gold);
  }
  .up {
    font-weight: 700;
    color: var(--tier-6);
  }
</style>
