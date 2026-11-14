<script lang="ts">
  import { app } from '../lib/state.svelte'
  import { asset } from '../lib/assets'
  import Icon from './Icon.svelte'
  import { openMenu, operatorMenu, equipmentMenu } from '../lib/menu.svelte'

  // 覆盖模式下收起的竖条：侧栏盖在游戏画面上时，买东西、布阵前收成一条，只留追踪列表
  const total = $derived(app.trackedOps.length + app.session.trackedEquipment.length)
  const expand = () => app.setCollapsed(false)
  // 竖条贴着游戏窗口那条边：贴左侧、盖右侧时竖条在右边缘，展开方向朝左
  const atRight = $derived(app.connection.side === (app.connection.overlay ? 'right' : 'left'))
</script>

<div class="strip">
  <span class="grip" aria-hidden="true"><i></i></span>
  <button class="open" title="展开侧栏" aria-label="展开侧栏" onclick={expand}><Icon name={atRight ? 'contractLeft' : 'expandRight'} size={18} /></button>
  {#if app.continuous}
    <button class="stopscan" title="正在识别对局详情，点这里停止" aria-label="停止识别" onclick={() => app.toggleContinuous()}>
      <span class="live-dot"></span>
      <Icon name="stop" size={14} />
    </button>
  {/if}
  <!-- 追踪列表竖着排，点头像取消追踪，和顶栏一样 -->
  <span class="cnt" title="追踪 {total} 项"><b>{total}</b><em>追踪</em></span>
  <div class="list">
    {#each app.trackedOps as n (n)}
      <button class="av" class:banned={app.banned.has(n)} title="{n}：点一下取消追踪，右键看更多" onclick={() => app.toggleTracked(n)} oncontextmenu={(e) => openMenu(e, operatorMenu(n))}>
        <img src={asset.operator(n)} alt={n} />
          {#if app.banned.has(n)}<img class="ban" src={asset.stage('img_chess_banned')} alt="禁用" />{/if}
        <span class="x"><Icon name="close" size={13} /></span>
      </button>
    {/each}
    {#each app.session.trackedEquipment as n (n)}
      <button class="av eq" title="{n}：点一下取消追踪，右键看更多" onclick={() => app.toggleTrackedEq(n)} oncontextmenu={(e) => openMenu(e, equipmentMenu(n))}>
        <img src={asset.equipment(n)} alt={n} />
        <span class="x"><Icon name="close" size={13} /></span>
      </button>
    {/each}
  </div>
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
      linear-gradient(180deg, var(--bg-1a), var(--bg-0));
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
  .stopscan {
    flex: none;
    width: 34px;
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 4px;
    padding: 6px 0;
    color: var(--mint);
    box-shadow: inset 0 0 0 1px var(--mint);
  }
  .stopscan:hover {
    background: var(--mint-tint);
  }
  .cnt {
    flex: none;
    display: flex;
    flex-direction: column;
    align-items: center;
    line-height: 1.1;
    padding-top: 2px;
  }
  .cnt b {
    font-family: var(--font-term);
    font-size: var(--fs-xl);
    color: var(--mint);
  }
  .cnt em {
    font-style: normal;
    font-size: var(--fs-xs);
    color: var(--fg-3);
  }
  .list {
    flex: 1;
    min-height: 0;
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 4px;
    overflow-y: auto;
    scrollbar-width: none;
    padding-bottom: 6px;
  }
  .av {
    position: relative;
    flex: none;
    width: 34px;
    height: 34px;
    background: var(--bg-3);
    box-shadow: inset 0 0 0 1px var(--mint-lo);
    overflow: hidden;
  }
  .av img {
    width: 100%;
    height: 100%;
    object-fit: cover;
  }
  .av.eq img {
    object-fit: contain;
    padding: 3px;
  }
  /* 本局禁用：头像压暗变灰、描红边 */
  .av.banned {
    box-shadow: inset 0 0 0 1.5px var(--red);
  }
  .av.banned img:not(.ban) {
    filter: grayscale(1) brightness(0.42);
  }
  /* 禁用角标：素材四周有留白，放大并往右上移，让红三角卡在角上 */
  .av .ban {
    position: absolute;
    right: -5px;
    top: -5px;
    z-index: 1;
    width: 20px;
    height: 20px;
  }
  .x {
    position: absolute;
    inset: 0;
    display: grid;
    place-items: center;
    color: var(--fg-0);
    background: rgb(var(--bg-0-rgb) / 0.62);
    box-shadow: inset 0 0 0 1.5px var(--red);
    opacity: 0;
    transition: opacity var(--t-fast);
  }
  .av:hover .x {
    opacity: 1;
  }
</style>
