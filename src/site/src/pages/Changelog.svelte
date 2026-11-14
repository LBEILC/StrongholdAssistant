<script lang="ts">
  import PageHead from '../components/PageHead.svelte'
  import ReleaseList from '../components/ReleaseList.svelte'
  import { FIRST, KIND, RELEASES, dot } from '../data'

  // 筛选：全部 / 新增 / 改进 / 修复
  let kind = $state<keyof typeof KIND | null>(null)
  const count = (k: keyof typeof KIND | null) => RELEASES.reduce((n, r) => n + r.items.filter((i) => !k || i.kind === k).length, 0)
  const FILTERS: [keyof typeof KIND | null, string][] = [[null, '全部'], ['new', KIND.new], ['improved', KIND.improved], ['fixed', KIND.fixed]]
</script>

<PageHead en="Changelog" title="更新日志">
  自 {dot(FIRST.date)} 第一版起，共 {RELEASES.length} 个版本、{count(null)} 项改动。软件里的「设置 → 更新日志」是同一份。
</PageHead>

<section class="wrap body">
  <div class="filters" role="tablist" aria-label="按类型筛选">
    {#each FILTERS as [k, label] (label)}
      <button role="tab" aria-selected={kind === k} class:on={kind === k} class="k-{k ?? 'all'}" onclick={() => (kind = k)}>
        {label}<span class="num">{count(k)}</span>
      </button>
    {/each}
  </div>
  <ReleaseList releases={RELEASES} {kind} />
</section>

<style>
  .body {
    padding-top: 48px;
    padding-bottom: 40px;
  }
  .filters {
    position: sticky;
    top: var(--head-h);
    z-index: 5;
    display: flex;
    flex-wrap: wrap;
    gap: 6px;
    padding: 14px 0;
    margin-bottom: 8px;
    background: rgb(var(--bg-0-rgb) / 0.9);
    backdrop-filter: blur(10px);
  }
  .filters button {
    display: inline-flex;
    align-items: center;
    gap: 8px;
    height: 34px;
    padding: 0 14px;
    font-size: 14px;
    color: var(--fg-2);
    background: var(--bg-2);
    border: 1px solid var(--line);
    border-radius: var(--r-1);
  }
  .filters button:hover {
    color: var(--fg-0);
    border-color: var(--line-hi);
  }
  .filters .num {
    font-size: 12px;
    color: var(--fg-4);
  }
  .filters .on {
    color: var(--mint-ink);
    background: var(--mint);
    border-color: var(--mint);
    font-weight: 600;
  }
  .filters .on .num {
    color: var(--mint-ink);
    opacity: 0.7;
  }
</style>
