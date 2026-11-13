<script lang="ts">
  import type { ReleaseNotes } from '../lib/api'
  import { KIND_LABEL } from '../lib/changelog'

  // 一个版本的更新条目：左边「新增 / 改进 / 修复」小标，右边标题加一两句说明
  let { items }: { items: ReleaseNotes['items'] } = $props()
</script>

<ul class="notes">
  {#each items as it, i (i)}
    <li class:hl={it.highlight}>
      <span class="k {it.kind}">{KIND_LABEL[it.kind]}</span>
      <span class="tx"><b>{it.title}</b><span>{it.detail}</span></span>
    </li>
  {/each}
</ul>

<style>
  .notes {
    list-style: none;
    margin: 0;
    padding: 0;
    display: flex;
    flex-direction: column;
    gap: 2px;
  }
  li {
    display: flex;
    gap: 8px;
    align-items: flex-start;
    padding: 6px 8px;
    background: rgb(var(--shade-rgb) / 0.22);
  }
  li.hl {
    background: linear-gradient(90deg, var(--mint-tint), transparent 70%), rgb(var(--shade-rgb) / 0.22);
  }
  .k {
    flex: none;
    margin-top: 1px;
    font-size: var(--fs-xs);
    font-weight: 700;
    line-height: 1;
    padding: 3px 5px;
  }
  .k.new {
    color: var(--mint-ink);
    background: var(--mint);
  }
  .k.improved {
    color: var(--gold-ink);
    background: var(--gold);
  }
  .k.fixed {
    color: var(--fg-2);
    box-shadow: inset 0 0 0 1px var(--fg-4);
  }
  .tx {
    display: flex;
    flex-direction: column;
    gap: 2px;
    min-width: 0;
  }
  .tx b {
    font-size: var(--fs-md);
    color: var(--fg-1);
  }
  .tx span {
    font-size: var(--fs-base);
    line-height: 1.5;
    color: var(--fg-3);
  }
</style>
