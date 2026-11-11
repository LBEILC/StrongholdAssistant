<script lang="ts">
  import { fly, fade } from 'svelte/transition'
  import { cubicOut } from 'svelte/easing'
  import { app } from '../lib/state.svelte'
  import { asset } from '../lib/assets'
  import OpTile from './OpTile.svelte'
  import Icon from './Icon.svelte'

  // 添加禁用干员：识别漏掉的手动补上。近全高面板，和阵容编辑器同一套样子。
  // 从某个盟约组的「添加」点开时只列这个盟约的干员（可以在上面换盟约）；不限盟约点开时，
  // 游戏里被禁的都属于阵容不完整的盟约，所以默认只列这些盟约的干员。点头像禁用，再点解除
  let q = $state('')
  let bond = $state<string | null>(null)
  const incomplete = $derived(app.match.covenants.filter((c) => c.incomplete).map((c) => c.id))
  let onlyIncomplete = $state(true)
  const useIncomplete = $derived(onlyIncomplete && incomplete.length > 0)
  const inAny = (o: { core: string[]; extra: string[] }, ids: string[]) => ids.some((id) => o.core.includes(id) || o.extra.includes(id))
  const pool = $derived(
    app.catalog.operators
      .filter((o) => !o.obtain && (!useIncomplete || inAny(o, incomplete)) && (!q || o.name.includes(q)) && (!bond || inAny(o, [bond])))
      .sort((a, b) => b.tier - a.tier),
  )
  const close = () => (app.banPicker = false)
  // 每次打开按入口重设筛选
  $effect(() => {
    const from = app.banPicker
    q = ''
    bond = typeof from === 'string' ? from : null
    onlyIncomplete = true
  })
</script>

<svelte:window onkeydown={(e) => app.banPicker && e.key === 'Escape' && close()} />

{#if app.banPicker}
  <div class="scrim" transition:fade={{ duration: 160 }} onclick={close} role="presentation"></div>
  <aside class="ed" transition:fly={{ y: 400, duration: 260, easing: cubicOut }}>
    <header>
      <img src={asset.stage('img_chess_banned')} alt="" class="hi" />
      <h3>添加禁用干员{#if bond}<em>· {app.cov(bond).name}</em>{/if}</h3>
      <span class="cnt">本局已禁 <b class="term">{app.match.bannedOperators.length}</b></span>
      <button class="x" onclick={close} aria-label="关闭"><Icon name="close" size={16} /></button>
    </header>
    <p class="hint">识别漏掉的在这里补上：点头像标记禁用，再点一次解除。</p>

    <div class="pick">
      <div class="search">
        <Icon name="search" size={14} />
        <input bind:value={q} placeholder="搜索干员" />
        {#if incomplete.length}
          <button class="tog" class:on={onlyIncomplete} onclick={() => (onlyIncomplete = !onlyIncomplete)}>只看不完整盟约</button>
        {/if}
      </div>
      <div class="bondpick">
        {#each app.catalog.covenants as c (c.id)}
          {@const inc = incomplete.includes(c.id)}
          <button class="bp" class:on={bond === c.id} class:inc class:off={useIncomplete && !inc} title={c.name} onclick={() => (bond = bond === c.id ? null : c.id)}>
            <span class="glyph" style:--src="url({asset.bond(c.id)})"></span>
          </button>
        {/each}
      </div>
      <div class="pool">
        {#each pool as o (o.name)}
          <OpTile name={o.name} size={44} menu={false} onclick={() => app.toggleBanned(o.name)} />
        {:else}
          <p class="none">没有符合条件的干员。</p>
        {/each}
      </div>
    </div>

    <footer>
      <button class="primary" onclick={close}>完成</button>
    </footer>
  </aside>
{/if}

<style>
  .scrim {
    position: absolute;
    inset: 0;
    background: rgb(0 0 0 / 0.6);
    z-index: 20;
  }
  .ed {
    position: absolute;
    left: 0;
    right: 0;
    bottom: 0;
    top: 24px;
    z-index: 21;
    display: flex;
    flex-direction: column;
    background:
      var(--noise),
      linear-gradient(180deg, #202326, #141618 160px);
    box-shadow:
      0 -1px 0 var(--line),
      0 -12px 30px rgb(0 0 0 / 0.6);
  }
  .ed::before {
    content: '';
    position: absolute;
    top: 0;
    left: 12px;
    width: 64px;
    height: 2px;
    background: var(--red);
    box-shadow: 0 0 8px rgb(229 72 77 / 0.6);
  }
  header {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 12px 8px 4px 12px;
  }
  .hi {
    width: 20px;
    height: 20px;
  }
  h3 {
    margin: 0;
    font-size: 15px;
  }
  h3 em {
    font-style: normal;
    font-weight: 400;
    margin-left: 4px;
    color: var(--fg-3);
  }
  .cnt {
    font-size: 11px;
    color: var(--fg-3);
  }
  .cnt b {
    font-size: 13px;
    color: var(--red);
  }
  .x {
    margin-left: auto;
    width: 30px;
    height: 30px;
    display: grid;
    place-items: center;
    color: var(--fg-3);
  }
  .x:hover {
    color: var(--fg-1);
    background: var(--bg-4);
  }
  .hint {
    margin: 0 12px 4px;
    font-size: 11px;
    color: var(--fg-4);
  }
  .pick {
    flex: 1;
    min-height: 0;
    display: flex;
    flex-direction: column;
    gap: 6px;
    padding: 6px 12px 0;
  }
  .search {
    display: flex;
    align-items: center;
    gap: 6px;
    height: 28px;
    padding: 0 8px;
    color: var(--fg-3);
    background: var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .search:focus-within {
    box-shadow: inset 0 0 0 1px var(--mint-lo);
  }
  .search input {
    flex: 1;
    min-width: 0;
    background: none;
    border: 0;
    outline: 0;
    font-size: 12px;
    color: var(--fg-1);
  }
  .tog {
    flex: none;
    font-size: 11px;
    padding: 2px 6px;
    color: var(--fg-3);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .tog.on {
    color: var(--mint-ink);
    background: var(--mint);
    box-shadow: none;
  }
  .bondpick {
    display: grid;
    grid-template-columns: repeat(13, 1fr);
    gap: 3px;
  }
  .bp {
    position: relative;
    aspect-ratio: 1;
    display: grid;
    place-items: center;
    border-radius: 50%;
    color: var(--fg-3);
    background: var(--bg-2);
  }
  .bp .glyph {
    width: 62%;
    height: 62%;
  }
  /* 阵容不完整的盟约：红色细圈，和游戏里灰掉的盟约对应 */
  .bp.inc {
    color: var(--fg-2);
    box-shadow: inset 0 0 0 1px rgb(229 72 77 / 0.6);
  }
  .bp.off {
    opacity: 0.3;
  }
  .bp.on {
    color: var(--mint-ink);
    background: var(--mint);
    box-shadow: none;
    opacity: 1;
  }
  .pool {
    flex: 1;
    overflow-y: auto;
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(52px, 1fr));
    gap: 6px 2px;
    justify-items: center;
    align-content: start;
    padding: 4px 0 10px;
  }
  .none {
    grid-column: 1 / -1;
    font-size: 12px;
    color: var(--fg-4);
  }
  footer {
    display: flex;
    justify-content: flex-end;
    padding: 10px 12px;
    border-top: 1px solid var(--line);
    background: var(--bg-1);
  }
  .primary {
    height: 30px;
    padding: 0 18px;
    font-size: 13px;
    font-weight: 700;
    color: var(--mint-ink);
    background: var(--mint);
  }
</style>
