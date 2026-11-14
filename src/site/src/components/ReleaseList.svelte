<script lang="ts">
  import { KIND, LATEST, dot, type Release } from '../data'
  import { reveal } from '../lib'

  // 更新日志的「账本」排法：左边版本号和日期，右边逐条改动；compact 只放标题
  let { releases, compact = false, kind = null }: { releases: Release[]; compact?: boolean; kind?: keyof typeof KIND | null } = $props()
</script>

<ol class="ledger" class:compact>
  {#each releases as r (r.version)}
    {@const items = kind ? r.items.filter((i) => i.kind === kind) : r.items}
    {#if items.length}
      <li class="row" id={'v' + r.version} use:reveal>
        <div class="ver">
          <span class="v num">{r.version}</span>
          <span class="lbl">{dot(r.date)}</span>
          {#if r.version === LATEST.version}<span class="latest lbl">Latest</span>{/if}
        </div>
        <ul class="items">
          {#each items as it, i (i)}
            <li class:hl={it.highlight}>
              <span class="k k-{it.kind}">{KIND[it.kind]}</span>
              <div class="tx">
                <b>{it.title}</b>
                {#if !compact}<p>{it.detail}</p>{/if}
              </div>
            </li>
          {/each}
        </ul>
      </li>
    {/if}
  {/each}
</ol>

<style>
  .ledger {
    list-style: none;
    margin: 0;
    padding: 0;
    border-top: 2px solid var(--line-strong);
  }
  .row {
    display: grid;
    grid-template-columns: 200px 1fr;
    gap: 32px;
    padding: 28px 0;
    border-bottom: 1px solid var(--line);
  }
  .compact .row {
    grid-template-columns: 110px 1fr;
    gap: 20px;
    padding: 20px 0;
  }
  .ver {
    display: flex;
    flex-direction: column;
    align-items: flex-start;
    gap: 4px;
  }
  .v {
    font-size: 30px;
    font-weight: 600;
    line-height: 1.1;
    color: var(--fg-0);
  }
  .compact .v {
    font-size: 24px;
  }
  .latest {
    margin-top: 6px;
    padding: 2px 8px;
    font-size: 10px;
    color: var(--mint-ink);
    background: var(--mint);
    letter-spacing: 0.18em;
  }
  .items {
    list-style: none;
    margin: 0;
    padding: 0;
    display: flex;
    flex-direction: column;
    gap: 16px;
  }
  .compact .items {
    gap: 10px;
  }
  .items li {
    display: flex;
    gap: 14px;
    align-items: baseline;
  }
  .k {
    flex: none;
    padding: 1px 7px;
    font-size: 12px;
    font-weight: 600;
    line-height: 1.6;
    border-radius: var(--r-1);
    border: 1px solid currentColor;
  }
  .k-new {
    color: var(--mint);
  }
  .k-improved {
    color: var(--tier-4);
  }
  .k-fixed {
    color: var(--gold);
  }
  .tx b {
    font-size: 15px;
    font-weight: 600;
  }
  .hl .tx b {
    color: var(--fg-0);
  }
  .tx p {
    margin-top: 4px;
    font-size: 14px;
    color: var(--fg-2);
  }
  @media (max-width: 720px) {
    .row,
    .compact .row {
      grid-template-columns: 1fr;
      gap: 14px;
    }
    .ver {
      flex-direction: row;
      align-items: baseline;
      gap: 12px;
    }
    .latest {
      margin-top: 0;
    }
  }
</style>
