<script lang="ts">
  import { app } from '../lib/state.svelte'
  import { allStages, stageLabel } from '../lib/stages'
  import Tabs from './Tabs.svelte'
  import BondTracker from './BondTracker.svelte'
  import UpdateBar from './UpdateBar.svelte'
  import BondIcon from './BondIcon.svelte'

  // 工作台左边的常驻栏：导航、目标阵容（当前阶段、目标盟约进度）、追踪列表、更新提示。
  // 侧栏里横在顶上的追踪条和更新条都收到这里，内容区从标题栏下面直接开始
  const t = $derived(app.target)
  const stageName = $derived.by(() => {
    if (!t?.stages?.length) return ''
    const s = allStages(t)[app.stageIx]
    return s.final ? '成型阵容' : stageLabel(s)
  })
  const goTarget = () => (app.setTab('lineup'), (app.lineupView = 'target'))
</script>

<aside class="rail">
  <Tabs vertical />

  {#if t}
    <section class="tgt">
      <span class="lbl term">Target</span>
      <button class="name" onclick={goTarget} title="目标阵容：{t.name}">{t.name}</button>
      {#if stageName}<span class="stg">{stageName}{app.session.stage == null ? ' · 自动' : ''}</span>{/if}
      {#if app.goalProgress.length}
        <ul class="goals">
          {#each app.goalProgress as g (g.covenant.id)}
            <li>
              <button class:active={g.active} onclick={() => (app.detail = { kind: 'covenant', id: g.covenant.id })}>
                <BondIcon id={g.covenant.id} size={20} on={g.active} incomplete={app.matchCovenant(g.covenant.id)?.incomplete} />
                <span class="gn">{g.covenant.name}</span>
                <span class="gc"><em>{g.have}</em>/{g.need}</span>
              </button>
            </li>
          {/each}
        </ul>
      {/if}
    </section>
  {/if}

  <div class="trk">
    <BondTracker rail />
  </div>

  <UpdateBar rail />
</aside>

<style>
  .rail {
    flex: none;
    width: var(--rail-w);
    min-height: 0;
    display: flex;
    flex-direction: column;
    background: var(--bg-1);
    border-right: 1px solid var(--line);
  }
  .rail > :global(.tabs) {
    border-right: none;
  }
  .tgt {
    display: flex;
    flex-direction: column;
    gap: 2px;
    padding: 10px 12px 8px;
    border-top: 1px solid var(--line);
  }
  .lbl {
    font-size: var(--fs-2xs);
    color: var(--mint);
  }
  .name {
    font-size: var(--fs-lg);
    font-weight: var(--fw-bold);
    text-align: left;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .name:hover {
    color: var(--mint-hi);
  }
  .stg {
    font-size: var(--fs-sm);
    color: var(--mint);
  }
  .goals {
    display: flex;
    flex-direction: column;
    gap: 2px;
    margin: 6px 0 0;
    padding: 0;
    list-style: none;
  }
  .goals button {
    width: 100%;
    display: flex;
    align-items: center;
    gap: 7px;
    padding: 2px 4px 2px 2px;
    font-size: var(--fs-base);
    color: var(--fg-3);
  }
  .goals button:hover {
    background: var(--bg-3);
  }
  .goals button.active {
    color: var(--fg-1);
  }
  .gn {
    flex: 1;
    min-width: 0;
    text-align: left;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .gc {
    font-family: var(--font-term);
    font-size: var(--fs-sm);
    color: var(--fg-4);
  }
  .gc em {
    font-style: normal;
    font-size: var(--fs-md);
    color: var(--fg-1);
  }
  .active .gc em {
    color: var(--mint);
  }
  /* 追踪列表占满剩下的高度，多了在栏里滚 */
  .trk {
    flex: 1;
    min-height: 0;
    display: flex;
    flex-direction: column;
    border-top: 1px solid var(--line);
  }
</style>
