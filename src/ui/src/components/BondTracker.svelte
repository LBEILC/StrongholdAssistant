<script lang="ts">
  import { app } from '../lib/state.svelte'
  import { asset } from '../lib/assets'
  import BondIcon from './BondIcon.svelte'

  // 常驻顶栏：目标阵容的盟约进度 + 还没买到的干员。任何页面都可见，取代参考项目的「追踪」标签。
  // 商店阶段最常用：买到谁就点谁的头像标记已持有。
  const t = $derived(app.target)
  const ownedCount = $derived(t ? t.members.length - app.missing.length : 0)
  const goTarget = () => ((app.tab = 'lineup'), (app.lineupView = 'target'))
</script>

<section class="tracker">
  {#if t}
    <div class="head">
      <span class="lbl term">Target</span>
      <button class="name" onclick={goTarget}>{t.name}</button>
      <span class="spacer"></span>
      <span class="count"><b>{ownedCount}</b>/{t.members.length} 持有</span>
      <span class="count trk">追踪 <b>{app.tracked.size}</b></span>
    </div>
    <div class="row">
      {#each app.targetBonds as b (b.covenant.id)}
        {@const next = b.covenant.thresholds.find((x) => x > b.owned) ?? b.covenant.thresholds.at(-1)!}
        <div class="cell" class:active={b.active}>
          <BondIcon
            id={b.covenant.id}
            size={34}
            on={b.active}
            incomplete={app.matchCovenant(b.covenant.id)?.incomplete}
            progress={b.owned / next}
            onclick={() => (app.detail = { kind: 'covenant', id: b.covenant.id })}
          />
          <span class="n"><b>{b.owned}</b>/{next}</span>
        </div>
      {/each}
    </div>
    <div class="buy">
      <span class="bl">待购</span>
      {#each app.missing as m (m.name)}
        <button
          class="av"
          class:banned={app.banned.has(m.name)}
          title="{m.name}：点一下标记已持有"
          onclick={() => app.toggleOwned(m.name)}
        >
          <img src={asset.operator(m.name)} alt={m.name} />
        </button>
      {:else}
        <span class="done">阵容已齐</span>
      {/each}
    </div>
  {:else}
    <div class="empty">
      还没有目标阵容。去
      <button onclick={goTarget}>阵容</button>
      规划或选一个，这里会显示盟约进度和待购干员。
    </div>
  {/if}
</section>

<style>
  .tracker {
    flex: none;
    position: relative;
    padding: 8px 10px 8px;
    background:
      var(--noise),
      linear-gradient(180deg, #16181a, #111214);
    border-bottom: 1px solid var(--line);
  }
  .tracker::before {
    /* 左侧薄荷绿竖条 */
    content: '';
    position: absolute;
    left: 0;
    top: 8px;
    bottom: 8px;
    width: 2px;
    background: var(--mint);
    box-shadow: var(--mint-glow);
  }
  .head {
    display: flex;
    align-items: baseline;
    gap: 6px;
    margin-bottom: 8px;
  }
  .lbl {
    font-size: 9px;
    color: var(--mint);
  }
  .name {
    font-size: 13px;
    font-weight: 700;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    min-width: 0;
  }
  .name:hover {
    color: var(--mint-hi);
  }
  .spacer {
    flex: 1;
  }
  .count {
    font-size: 11px;
    color: var(--fg-3);
    white-space: nowrap;
  }
  .count b {
    color: var(--fg-1);
    font-family: var(--font-term);
    font-size: 13px;
  }
  .count.trk b {
    color: var(--mint);
  }
  .row {
    display: flex;
    gap: 6px;
    overflow-x: auto;
    padding: 5px 2px 2px 4px;
    scrollbar-width: none;
  }
  .cell {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 6px;
    min-width: 44px;
  }
  .n {
    font-family: var(--font-term);
    font-size: 11px;
    color: var(--fg-3);
    line-height: 1;
  }
  .n b {
    font-size: 13px;
    color: var(--fg-2);
  }
  .active .n b {
    color: var(--mint);
  }
  .buy {
    display: flex;
    align-items: center;
    gap: 4px;
    margin-top: 8px;
    min-height: 28px;
    overflow-x: auto;
    scrollbar-width: none;
  }
  .bl {
    flex: none;
    font-size: 11px;
    color: var(--fg-3);
    margin-right: 2px;
  }
  .av {
    flex: none;
    width: 28px;
    height: 28px;
    background: var(--bg-3);
    box-shadow: inset 0 0 0 1px var(--line);
    overflow: hidden;
    transition:
      box-shadow var(--t-fast),
      transform var(--t-fast) var(--ease-out);
  }
  .av img {
    width: 100%;
    height: 100%;
    object-fit: cover;
  }
  .av:hover {
    transform: translateY(-1px);
    box-shadow:
      inset 0 0 0 1.5px var(--mint),
      0 0 6px rgb(34 227 178 / 0.35);
  }
  .av.banned img {
    filter: grayscale(1) brightness(0.45);
  }
  .done {
    font-size: 11px;
    color: var(--mint);
  }
  .empty {
    font-size: 12px;
    color: var(--fg-3);
    padding: 6px 0 4px;
  }
  .empty button {
    color: var(--mint);
    text-decoration: underline;
    text-underline-offset: 2px;
  }
</style>
