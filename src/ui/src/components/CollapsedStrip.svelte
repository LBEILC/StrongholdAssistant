<script lang="ts">
  import { app } from '../lib/state.svelte'
  import BondIcon from './BondIcon.svelte'
  import Icon from './Icon.svelte'

  // 覆盖模式下收起的竖条：侧栏盖在游戏画面上时，买东西、布阵前收成一条，只留目标盟约进度和待购人数
  const t = $derived(app.target)
  const expand = () => app.setCollapsed(false)
</script>

<div class="strip">
  <span class="grip" aria-hidden="true"><i></i></span>
  <button class="open" title="展开侧栏" aria-label="展开侧栏" onclick={expand}><Icon name="expandRight" size={18} /></button>
  {#if t}
    <div class="bonds">
      {#each app.targetBonds as b (b.covenant.id)}
        {@const next = b.covenant.thresholds.find((x) => x > b.owned) ?? b.covenant.thresholds.at(-1)!}
        <button class="cell" class:active={b.active} title="{b.covenant.name} {b.owned}/{next}" onclick={expand}>
          <BondIcon id={b.covenant.id} size={30} on={b.active} progress={b.owned / next} />
          <span class="n"><b>{b.owned}</b>/{next}</span>
        </button>
      {/each}
    </div>
    <button class="buy" class:done={!app.missing.length} title="还有 {app.missing.length} 名待购" onclick={expand}>
      <span>待购</span>
      <b>{app.missing.length}</b>
    </button>
  {/if}
</div>

<style>
  .strip {
    height: 100%;
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 6px;
    background:
      var(--noise),
      linear-gradient(180deg, #16181b, var(--bg-0));
    box-shadow: inset -1px 0 0 var(--line-soft);
    overflow: hidden;
  }
  .grip {
    /* 拖动区：收起后也能挪位置 */
    -webkit-app-region: drag;
    flex: none;
    width: 100%;
    height: 22px;
    display: grid;
    place-items: center;
    background: var(--bg-0);
  }
  .grip i {
    width: 12px;
    height: 12px;
    background: var(--mint);
    clip-path: polygon(50% 0, 100% 50%, 50% 100%, 0 50%);
    box-shadow: var(--mint-glow);
  }
  .open {
    flex: none;
    width: 34px;
    height: 30px;
    display: grid;
    place-items: center;
    color: var(--mint-ink);
    background: var(--mint);
  }
  .open:hover {
    background: var(--mint-hi);
  }
  .bonds {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 6px;
    padding-top: 4px;
  }
  .cell {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 1px;
  }
  .n {
    font-size: 10px;
    color: var(--fg-3);
    font-variant-numeric: tabular-nums;
  }
  .n b {
    color: var(--fg-1);
  }
  .active .n b {
    color: var(--mint);
  }
  .buy {
    margin-top: 2px;
    width: 38px;
    display: flex;
    flex-direction: column;
    align-items: center;
    padding: 4px 0 5px;
    background: rgb(243 179 34 / 0.14);
    box-shadow: inset 0 0 0 1px rgb(243 179 34 / 0.45);
  }
  .buy span {
    font-size: 10px;
    color: var(--fg-3);
  }
  .buy b {
    font-size: 16px;
    line-height: 1.1;
    color: var(--gold);
  }
  .buy.done {
    background: var(--mint-tint);
    box-shadow: inset 0 0 0 1px rgb(34 227 178 / 0.4);
  }
  .buy.done b {
    color: var(--mint);
  }
</style>
