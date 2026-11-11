<script lang="ts">
  // 游戏描述文字：【盟约】薄荷绿，<触发条件> 金色
  let { text }: { text: string } = $props()
  const parts = $derived(text.split(/(【[^】]+】|<[^>]+>)/).filter(Boolean))
</script>

<span class="gt">
  {#each parts as p, i (i)}
    {#if p.startsWith('【')}<b class="k">{p}</b>{:else if p.startsWith('<')}<b class="c">{p}</b>{:else}{p}{/if}
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
</style>
