<script lang="ts">
  import { fly, fade } from 'svelte/transition'
  import { cubicOut } from 'svelte/easing'
  import { app } from '../lib/state.svelte'
  import { asset, ROMAN, fmtHp } from '../lib/assets'
  import BondIcon from './BondIcon.svelte'
  import TierBadge from './TierBadge.svelte'
  import OpTile from './OpTile.svelte'
  import GameText from './GameText.svelte'
  import Icon from './Icon.svelte'

  // 底部详情面板：点击打开，固定不随鼠标消失；Esc 或点遮罩关闭
  const d = $derived(app.detail)
  const close = () => (app.detail = null)
  const showEq = (name: string) => (app.detail = { kind: 'equipment', name })
  const showOp = (name: string) => (app.detail = { kind: 'operator', name })
</script>

<svelte:window onkeydown={(e) => e.key === 'Escape' && close()} />

{#if d}
  <div class="scrim" transition:fade={{ duration: 160 }} onclick={close} role="presentation"></div>
  <aside class="sheet" transition:fly={{ y: 320, duration: 240, easing: cubicOut }}>
    <span class="grip"></span>
    <button class="x" onclick={close} aria-label="关闭">
      <Icon name="close" size={16} />
    </button>

    {#if d.kind === 'operator'}
      {@const o = app.op(d.name)}
      {@const banned = app.banned.has(o.name)}
      {@const inTarget = app.target?.members.some((m) => m.name === o.name)}
      <div class="hero">
        <span class="pic" style:--c="var(--tier-{o.tier})"><img src={asset.operator(o.name)} alt="" /></span>
        <div class="meta">
          <div class="nm"><TierBadge tier={o.tier} size={20} /><h3>{o.name}</h3></div>
          <div class="bonds">
            {#each o.core as c (c)}<span class="bchip core"><BondIcon id={c} size={18} on />{app.cov(c).name}</span>{/each}
            {#each o.extra as c (c)}<span class="bchip"><BondIcon id={c} size={18} />{app.cov(c).name}</span>{/each}
          </div>
          {#if o.obtain}<span class="obtain">不可招募 · {o.obtain}</span>{/if}
          {#if banned}<span class="warn">本局已被禁用</span>{/if}
        </div>
      </div>
      <!-- 普通和精锐并排一屏看，精锐里变化的数字高亮 -->
      <div class="body">
        {#each o.normal as t, i (i)}
          {@const e = o.elite[i]}
          <div class="trait col">
            <span class="tt">{t.type}</span>
            {#if e.text === t.text}
              <div class="lv"><span class="lt">普通 / 精锐</span><GameText text={t.text} /></div>
            {:else}
              <div class="lv"><span class="lt">普通</span><GameText text={t.text} /></div>
              <div class="lv"><span class="lt el">精锐</span><GameText text={e.text} compare={t.text} /></div>
            {/if}
          </div>
        {/each}
        {#if o.givesEquipment?.length}
          <div class="rel">
            <span class="tt">提供装备</span>
            <span class="rr">{#each o.givesEquipment as n (n)}<button class="eqc" onclick={() => showEq(n)}><img src={asset.equipment(n)} alt="" />{n}</button>{/each}</span>
          </div>
        {/if}
      </div>
      <div class="actions">
        <button class="btn" class:on={app.tracked.has(o.name)} onclick={() => app.toggleTracked(o.name)}>
          {#if app.tracked.has(o.name)}<Icon name="starFill" size={15} />追踪中{:else}<Icon name="star" size={15} />追踪{/if}
        </button>
        {#if app.target}
          <button class="btn" class:on={inTarget} onclick={() => app.toggleMember(o.name)}>
            {#if inTarget}<Icon name="check" size={15} />在目标里{:else}<Icon name="add" size={15} />加入目标{/if}
          </button>
        {/if}
      </div>
      <p class="note">追踪的干员在商店里标记，顶栏点一下就能取消；加入目标会同时追踪</p>
      <div class="minor">
        <button class:fav={app.focusOps.has(o.name)} onclick={() => app.toggleFocusOp(o.name)}>
          <Icon name={app.focusOps.has(o.name) ? 'heartFill' : 'heart'} size={13} />{app.focusOps.has(o.name) ? '已特别关注' : '特别关注'}
        </button>
        <button class:bad={banned} onclick={() => app.toggleBanned(o.name)}>
          <Icon name="forbid" size={13} />{banned ? '解除本局禁用' : '标记本局禁用'}
        </button>
      </div>
    {:else if d.kind === 'equipment'}
      {@const e = app.eq(d.name)}
      {@const inTarget = !!app.target?.equipment.includes(e.name)}
      {@const trk = app.trackedEq.has(e.name)}
      <div class="hero">
        <span class="pic eqp" style:--c="var(--tier-{e.tier})"><img src={asset.equipment(e.name)} alt="" /></span>
        <div class="meta">
          <div class="nm"><TierBadge tier={e.tier} size={20} /><h3>{e.name}</h3></div>
          <div class="sub">{e.categories.join(' · ')}</div>
        </div>
      </div>
      <div class="body">
        <div class="trait"><span class="tt">普通</span><GameText text={e.normal} /></div>
        {#if e.elite}
          <div class="trait"><span class="tt">精锐</span><GameText text={e.elite} compare={e.normal} /></div>
        {:else}
          <div class="trait"><span class="tt">精锐</span><span class="none">没有进阶效果</span></div>
        {/if}
        {#if e.fuse}
          <div class="rel">
            <span class="tt">合成</span>
            <span class="rr">
              <i>+</i><button class="eqc" onclick={() => showEq(e.fuse!.with)}><img src={asset.equipment(e.fuse.with)} alt="" />{e.fuse.with}</button>
              <i>→</i><button class="eqc out" onclick={() => showEq(e.fuse!.result)}><img src={asset.equipment(e.fuse.result)} alt="" />{e.fuse.result}</button>
            </span>
          </div>
        {/if}
        {#if e.transformBond}
          <div class="rel">
            <span class="tt">变形</span>
            <span class="rr"><i>+</i><span class="tag">变形同构体</span><i>→</i><button class="eqc" onclick={() => (app.detail = { kind: 'covenant', id: e.transformBond! })}><BondIcon id={e.transformBond} size={16} on />{app.cov(e.transformBond).name}</button></span>
          </div>
        {/if}
        {#if e.sources?.length}
          <div class="rel">
            <span class="tt">来源</span>
            <span class="rr">{#each e.sources as n (n)}<OpTile name={n} size={32} onclick={() => showOp(n)} />{/each}</span>
          </div>
        {/if}
      </div>
      <div class="actions">
        <button class="btn" class:on={trk} onclick={() => app.toggleTrackedEq(e.name)}>
          {#if trk}<Icon name="starFill" size={15} />追踪中{:else}<Icon name="star" size={15} />追踪{/if}
        </button>
        {#if app.target}
          <button class="btn" class:on={inTarget} onclick={() => app.toggleTargetEq(e.name)}>
            {#if inTarget}<Icon name="check" size={15} />在目标里{:else}<Icon name="add" size={15} />加入目标{/if}
          </button>
        {/if}
      </div>
      <p class="note">追踪的装备在商店里标记，顶栏点一下就能取消；加入目标会同时追踪</p>
    {:else if d.kind === 'covenant'}
      {@const c = app.cov(d.id)}
      {@const mc = app.matchCovenant(c.id)}
      {@const members = app.catalog.operators.filter((o) => o.core.includes(c.id) || o.extra.includes(c.id)).sort((a, b) => a.tier - b.tier)}
      <div class="hero">
        <BondIcon id={c.id} size={56} on={!mc?.incomplete} incomplete={mc?.incomplete} />
        <div class="meta">
          <div class="nm"><h3>{c.name}</h3><span class="kind">{c.kind === 'core' ? '核心盟约' : '附加盟约'}</span></div>
          <div class="sub">激活：{c.thresholds.map((t) => `${t} 人`).join(' / ')}</div>
          {#if mc?.incomplete}<span class="warn">本局有所含干员被禁用</span>{/if}
          <button class="favbtn" class:fav={app.focusCovs.has(c.id)} onclick={() => app.toggleFocusCov(c.id)}>
            <Icon name={app.focusCovs.has(c.id) ? 'heartFill' : 'heart'} size={13} />{app.focusCovs.has(c.id) ? '已特别关注' : '特别关注'}
          </button>
        </div>
      </div>
      <div class="body">
        {#each c.effects as e, i (i)}
          <div class="trait"><span class="tt">{e.condition ? '条件' : '基础'}</span><span>{#if e.condition}<b class="cond">&lt;{e.condition}&gt;</b> {/if}<GameText text={e.text} /></span></div>
        {/each}
        <div class="members">
          {#each members as o (o.name)}
            <OpTile name={o.name} size={40} onclick={() => (app.detail = { kind: 'operator', name: o.name })} />
          {/each}
        </div>
      </div>
    {:else if d.kind === 'boss'}
      {@const b = app.catalog.bosses.find((x) => x.id === d.id)!}
      <div class="hero">
        <span class="pic" style:--c="var(--gold)"><img src={asset.boss(b.id)} alt="" /></span>
        <div class="meta">
          <span class="leader">敌方领袖</span>
          <h3>{b.name}</h3>
          {#if b.hidingCore}<span class="obtain">有隐秘核心</span>{/if}
        </div>
      </div>
      <div class="body">
        {#each b.abilities as a, i (i)}
          <div class="trait"><span class="tt term">{String(i + 1).padStart(2, '0')}</span><GameText text={a} /></div>
        {/each}
      </div>
      <!-- 血量：单人按规程等级排成网格，联机四档一行；本局模式高亮 -->
      {#each [['single', '单人'], ['multi', '联机']] as [g, label] (g)}
        {@const rows = b.hp.filter((h) => h.group === g)}
        {#if rows.length}
          <div class="hph"><b>血量 · {label}</b>{#if b.hidingCore}<em>下行为隐秘核心</em>{/if}</div>
          <div class="hp" class:multi={g === 'multi'}>
            {#each rows as h (h.mode)}
              <div class="hc" class:cur={app.match.mode?.id === h.mode}>
                <span class="hm">{h.name.replace('规程等级', 'Lv.').replace('模拟', '')}</span>
                <b>{fmtHp(h.hp)}</b>
                {#if h.coreHp}<small>{fmtHp(h.coreHp)}</small>{/if}
              </div>
            {/each}
          </div>
        {/if}
      {/each}
    {/if}
  </aside>
{/if}

<style>
  .scrim {
    position: absolute;
    inset: 0;
    background: rgb(0 0 0 / 0.55);
    z-index: 10;
  }
  .sheet {
    position: absolute;
    left: 0;
    right: 0;
    bottom: 0;
    max-height: 72%;
    overflow-y: auto;
    z-index: 11;
    padding: 14px 12px 16px;
    background:
      var(--noise),
      linear-gradient(180deg, #202326, #16181a 120px);
    box-shadow:
      0 -1px 0 var(--line),
      0 -12px 30px rgb(0 0 0 / 0.6);
  }
  .sheet::before {
    content: '';
    position: absolute;
    top: 0;
    left: 12px;
    width: 64px;
    height: 2px;
    background: var(--mint);
    box-shadow: var(--mint-glow);
  }
  .grip {
    display: block;
    width: 32px;
    height: 3px;
    margin: -6px auto 8px;
    background: var(--bg-4);
  }
  .x {
    position: absolute;
    top: 8px;
    right: 8px;
    width: 26px;
    height: 26px;
    display: grid;
    place-items: center;
    color: var(--fg-3);
  }
  .x:hover {
    color: var(--fg-1);
    background: var(--bg-4);
  }
  .hero {
    display: flex;
    gap: 12px;
    align-items: center;
    padding-right: 28px;
  }
  .pic {
    width: 76px;
    height: 76px;
    flex: none;
    background: var(--bg-3);
    box-shadow:
      inset 0 0 0 1px var(--line),
      inset 0 -3px 0 var(--c);
    overflow: hidden;
  }
  .pic img {
    width: 100%;
    height: 100%;
    object-fit: cover;
  }
  .pic.eqp img {
    object-fit: contain;
    padding: 10px;
  }
  .meta {
    display: flex;
    flex-direction: column;
    gap: 6px;
    min-width: 0;
    align-items: flex-start;
  }
  .nm {
    display: flex;
    align-items: center;
    gap: 6px;
  }
  h3 {
    margin: 0;
    font-size: 18px;
  }
  .kind,
  .sub {
    font-size: 11px;
    color: var(--fg-3);
  }
  .bonds {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
  }
  .bchip {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    font-size: 12px;
    padding: 1px 7px 1px 2px;
    background: var(--bg-3);
    color: var(--fg-2);
    border-radius: 12px;
  }
  .bchip.core {
    color: var(--fg-1);
  }
  .warn {
    font-size: 11px;
    color: #fff;
    background: var(--red);
    padding: 1px 6px;
  }
  .obtain {
    font-size: 11px;
    color: var(--gold);
    padding: 1px 6px;
    box-shadow: inset 0 0 0 1px rgb(243 179 34 / 0.45);
  }
  .leader {
    font-size: 11px;
    font-weight: 700;
    padding: 1px 8px;
    color: var(--gold-ink);
    background: var(--gold);
  }
  .body {
    display: flex;
    flex-direction: column;
    gap: 6px;
    margin-top: 12px;
  }
  .trait.col {
    flex-direction: column;
    gap: 4px;
  }
  .lv {
    display: flex;
    gap: 8px;
  }
  .lt {
    flex: none;
    align-self: flex-start;
    margin-top: 2px;
    font-size: 10px;
    line-height: 1;
    padding: 3px 5px;
    color: var(--fg-3);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .lt.el {
    color: var(--tier-6);
    box-shadow: inset 0 0 0 1px rgb(242 122 18 / 0.5);
  }
  .trait {
    display: flex;
    gap: 8px;
    font-size: 12.5px;
    line-height: 1.55;
    color: var(--fg-2);
    padding: 6px 8px;
    background: rgb(0 0 0 / 0.25);
  }
  .tt {
    flex: none;
    min-width: 4.2em;
    font-size: 11px;
    font-weight: 700;
    color: var(--fg-1);
    padding-top: 1px;
  }
  .cond {
    color: var(--gold);
    font-weight: 600;
  }
  .none {
    color: var(--fg-4);
  }
  .rel {
    display: flex;
    align-items: center;
    gap: 8px;
    font-size: 12px;
    padding: 6px 8px;
    border: 1px dashed var(--line);
  }
  .rr {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 6px;
  }
  .rr i {
    font-style: normal;
    color: var(--fg-4);
  }
  .eqc {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    height: 26px;
    padding: 0 8px 0 3px;
    color: var(--fg-1);
    background: var(--bg-2);
    box-shadow: inset 0 -2px 0 var(--fg-4);
  }
  .eqc:hover {
    background: var(--bg-3);
  }
  .eqc.out {
    box-shadow: inset 0 -2px 0 var(--gold);
  }
  .eqc img {
    width: 20px;
    height: 20px;
    object-fit: contain;
  }
  .tag {
    font-size: 11px;
    padding: 2px 6px;
    color: var(--mint);
    box-shadow: inset 0 0 0 1px var(--mint);
  }
  .hph {
    display: flex;
    align-items: baseline;
    gap: 8px;
    margin: 14px 0 6px;
    font-size: 12px;
  }
  .hph em {
    font-style: normal;
    font-size: 10.5px;
    color: var(--fg-4);
  }
  .hp {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(62px, 1fr));
    gap: 2px;
  }
  .hp.multi {
    grid-template-columns: repeat(4, 1fr);
  }
  .hc {
    display: flex;
    flex-direction: column;
    align-items: center;
    padding: 4px 2px;
    background: rgb(0 0 0 / 0.25);
    font-variant-numeric: tabular-nums;
  }
  .hc.cur {
    background: rgb(243 179 34 / 0.14);
    box-shadow: inset 0 0 0 1px var(--gold);
  }
  .hm {
    font-size: 10px;
    color: var(--fg-4);
  }
  .hc b {
    font-size: 12.5px;
    color: var(--fg-1);
  }
  .hc small {
    font-size: 10px;
    color: var(--gold);
  }
  .members {
    display: flex;
    flex-wrap: wrap;
    gap: 8px 6px;
    margin-top: 6px;
  }
  .actions {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 6px;
    margin-top: 12px;
  }
  .btn {
    display: flex;
    align-items: center;
    gap: 4px;
    font-size: 13px;
    font-weight: 700;
    padding: 6px 11px;
    color: var(--fg-1);
    background: var(--bg-4);
  }
  .btn:hover {
    background: #3a3e42;
  }
  .btn.on {
    color: var(--mint-ink);
    background: var(--mint);
  }
  .minor {
    display: flex;
    gap: 12px;
    margin-top: 10px;
    padding-top: 10px;
    border-top: 1px solid var(--line-soft);
  }
  .minor button {
    display: flex;
    align-items: center;
    gap: 4px;
    font-size: 12px;
    color: var(--fg-3);
  }
  .minor button:hover {
    color: var(--fg-1);
  }
  .minor button.fav,
  .favbtn.fav {
    color: var(--gold);
  }
  .favbtn {
    display: flex;
    align-items: center;
    gap: 4px;
    font-size: 12px;
    color: var(--fg-3);
  }
  .favbtn:hover {
    color: var(--fg-1);
  }
  .minor button.bad {
    color: var(--red);
  }
  .note {
    margin: 6px 0 0;
    font-size: 11px;
    color: var(--fg-4);
  }
</style>
