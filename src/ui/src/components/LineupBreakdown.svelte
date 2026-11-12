<script lang="ts">
  import type { Lineup } from '../lib/api'
  import { app } from '../lib/state.svelte'
  import { asset } from '../lib/assets'
  import { allStages, stageLabel } from '../lib/stages'
  import { effective } from '../lib/opconfig'
  import OpTile from './OpTile.svelte'
  import BondIcon from './BondIcon.svelte'

  // 广场详情里的阵容明细（v0.7.0）：有回合阶段时分标签（默认成型阵容）；每名干员列出精锐、技能、模组。
  // 别人的阵容没写技能模组时按数据默认显示（不用我的默认：看的是作者的配置）
  let { lineup }: { lineup: Lineup } = $props()
  const stages = $derived(allStages(lineup))
  let ix = $state(-1)
  const sel = $derived(ix < 0 || ix >= stages.length ? stages.length - 1 : ix)
  const cur = $derived(stages[sel])
</script>

<div class="bd">
  {#if stages.length > 1}
    <div class="tabs" role="tablist">
      {#each stages as s, i (i)}
        <button role="tab" aria-selected={sel === i} class:on={sel === i} onclick={() => (ix = i)}>{s.final ? '成型' : stageLabel(s)}</button>
      {/each}
    </div>
    {#if !cur.final && cur.goals.length}
      <div class="goals">
        {#each cur.goals as g (g.id)}<span class="g"><BondIcon id={g.id} size={18} on />{app.cov(g.id).name}{#if g.level > 1}<em>{g.level} 档</em>{/if}</span>{/each}
      </div>
    {/if}
  {/if}
  <div class="ms">
    {#each cur.members as m (m.name)}
      {@const o = app.op(m.name)}
      {@const ef = o.skills?.length ? effective(m, o, {}) : null}
      {@const sk = ef && o.skills.find((k) => k.id === ef.skill)}
      {@const md = ef && o.modules?.find((k) => k.id === ef.module)}
      <div class="m">
        <OpTile name={m.name} size={44} isKey={m.key} elite={m.elite} showName={false} onclick={() => (app.detail = { kind: 'operator', name: m.name })} />
        {#if ef}
          <span class="cf">
            {#if sk}<img src={asset.skill(sk.id)} alt={sk.name} title="技能：{sk.name}" />{/if}
            {#if md}<img class="mo" src={asset.module(md.id)} alt={md.name} title="模组：{md.name}" />{:else if o.modules?.length}<i title="不带模组">—</i>{/if}
          </span>
        {/if}
        <span class="nm">{m.name}</span>
      </div>
    {/each}
  </div>
  {#if cur.equipment.length}
    <div class="eqs">
      {#each cur.equipment as n (n)}<img src={asset.equipment(n)} alt={n} title={n} />{/each}
    </div>
  {/if}
</div>

<style>
  .bd {
    margin-top: 8px;
  }
  .tabs {
    display: flex;
    gap: 2px;
    overflow-x: auto;
    scrollbar-width: none;
    margin-bottom: 6px;
  }
  .tabs button {
    flex: none;
    height: 24px;
    padding: 0 9px;
    font-size: 11.5px;
    color: var(--fg-3);
    background: var(--bg-2);
  }
  .tabs button.on {
    color: var(--mint-ink);
    background: var(--mint);
    font-weight: 700;
  }
  .goals {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
    margin-bottom: 6px;
  }
  .g {
    display: inline-flex;
    align-items: center;
    gap: 3px;
    padding: 1px 7px 1px 2px;
    font-size: 11.5px;
    color: var(--fg-2);
    background: var(--bg-3);
    border-radius: 11px;
  }
  .g em {
    font-style: normal;
    font-size: 10px;
    color: var(--gold);
  }
  .ms {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(56px, 1fr));
    gap: 6px 4px;
  }
  .m {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 2px;
    min-width: 0;
  }
  .cf {
    display: flex;
    gap: 2px;
    height: 18px;
    align-items: center;
  }
  .cf img {
    width: 18px;
    height: 18px;
    background: rgb(0 0 0 / 0.4);
  }
  .cf .mo {
    object-fit: contain;
    padding: 1px;
  }
  .cf i {
    width: 18px;
    font-style: normal;
    font-size: 10px;
    text-align: center;
    color: var(--fg-4);
  }
  .nm {
    max-width: 100%;
    font-size: 10.5px;
    color: var(--fg-3);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .eqs {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
    margin-top: 8px;
  }
  .eqs img {
    width: 28px;
    height: 28px;
    object-fit: contain;
    background: var(--bg-2);
  }
</style>
