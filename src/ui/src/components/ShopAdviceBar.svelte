<script lang="ts">
  import { app } from '../lib/state.svelte'
  import Icon from './Icon.svelte'

  // 商店建议（409，单人规程，设置「商店建议」打开才有）：模型来自 garrison-rl（作者 CarlosDJY）。
  // 一行：「建议 买第 2 格 摆渡人 · 把握 62%」；给不出时说一句为什么、怎么办。
  const a = $derived(app.settings.shopAdvice ? app.shopAdvice : null)
  const name = (charId: string | null) => (charId ? app.catalog.operators.find((o) => o.charId === charId)?.name : undefined)

  // 宿主给的暂停原因（英文代号）按先后挑第一个能说清的；其余是「用了默认值」，不打扰玩家
  const REASONS: [string, string][] = [
    ['mode', '只在单人规程里给建议'],
    ['targetCovenant', '先在「阵容」页追踪一套阵容，用它的核心盟约当本局目标'],
    ['unitIdentity', '场上或备战区有人没认出来，先点「场上」的「识别」'],
    ['units', '场上或备战区有人没认出来，先点「场上」的「识别」'],
    ['shopCard', '商店的卡还没认全'],
    ['shopSlots', '商店的卡还没认全'],
    ['shopLayout', '商店的格子位置还没对上'],
    ['economy', '回合、资金或商店等级没读到'],
    ['hp', '血量没读到'],
    ['refreshCost', '刷新费没读到'],
    ['upgradeCost', '升级费没读到'],
    ['gameDataVersion', '游戏版本和模型对不上'],
    ['onlyEndRound', '这一轮没什么可买的了'],
    ['unsupportedTopAction', '这一轮模型更想卖人或直接结束，先不给建议'],
  ]
  const reason = $derived(a?.status === 'unavailable' ? (REASONS.find(([k]) => a.missingInputs.includes(k))?.[1] ?? null) : null)
  const act = $derived(a?.status === 'ready' ? a.action : null)
  const label = $derived.by(() => {
    if (!act) return ''
    if (act.kind === 'refresh') return '刷新商店'
    if (act.kind === 'upgrade') return '升级调度中心'
    const n = name(act.targetId)
    return `买第 ${(act.shopSlot ?? 0) + 1} 格${n ? ' ' + n : ''}`
  })
  const share = $derived(a?.preference ? Math.round(a.preference.topShare * 100) : null)
</script>

{#if a && (act || reason)}
  <div class="advice" class:off={!act} title="商店建议来自 garrison-rl（作者 CarlosDJY）的模型，只按单人规程训练过，仅供参考">
    <Icon name={act ? 'magic' : 'warn'} size={13} />
    {#if act}
      <span class="k">建议</span>
      <b>{label}</b>
      {#if share !== null}<span class="p">把握 {share}%</span>{/if}
    {:else}
      <span class="r">商店建议：{reason}</span>
    {/if}
  </div>
{/if}

<style>
  .advice {
    display: flex;
    align-items: center;
    gap: 6px;
    margin: 4px 12px 0;
    padding: 6px 10px;
    border: 1px solid var(--mint);
    border-radius: 4px;
    color: var(--fg-1);
    font-size: 12px;
    line-height: 1.4;
  }
  .advice :global(svg) {
    flex: none;
    color: var(--mint);
  }
  .advice.off {
    border-color: var(--line);
    color: var(--fg-3);
  }
  .advice.off :global(svg) {
    color: var(--fg-3);
  }
  .k {
    color: var(--fg-3);
  }
  b {
    color: var(--mint);
    font-weight: 600;
  }
  .p {
    margin-left: auto;
    color: var(--fg-3);
    font-variant-numeric: tabular-nums;
  }
  .r {
    min-width: 0;
  }
</style>
