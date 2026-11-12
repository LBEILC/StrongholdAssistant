<script lang="ts">
  import { strategies, strategy, asset } from '../data'
  import Icon from './Icon.svelte'

  // 策略选择：按钮显示当前策略，点开是带搜索的列表（头像 + 名字 + 发起人）。Esc、点外面关闭；上下键移动、回车选中
  let { value = $bindable(null) }: { value: string | null } = $props()
  let open = $state(false)
  let q = $state('')
  let hi = $state(0)
  let root: HTMLDivElement
  let search = $state<HTMLInputElement>()
  let list = $state<HTMLDivElement>()
  // 下方放不下就往上弹；列表高度按可用空间收
  let up = $state(false)
  let room = $state(340)

  const cur = $derived(strategy(value))
  const items = $derived([null, ...strategies.filter((s) => !q || s.name.includes(q) || s.initiator.includes(q)).map((s) => s.id)])

  function toggle() {
    open = !open
    if (open) {
      const r = root.getBoundingClientRect()
      const below = innerHeight - r.bottom - 16
      const above = r.top - 72
      up = below < 300 && above > below
      room = Math.max(160, Math.min(340, (up ? above : below) - 56))
      q = ''
      hi = Math.max(0, items.indexOf(value))
      queueMicrotask(() => {
        search?.focus()
        list?.querySelector('.on')?.scrollIntoView({ block: 'nearest' })
      })
    }
  }
  function pick(id: string | null) {
    value = id
    open = false
  }
  function key(e: KeyboardEvent) {
    if (e.key === 'Escape') open = false
    else if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault()
      hi = (hi + (e.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length
      queueMicrotask(() => list?.querySelector('.hi')?.scrollIntoView({ block: 'nearest' }))
    } else if (e.key === 'Enter' && items[hi] !== undefined) pick(items[hi])
  }
  $effect(() => {
    if (!open) return
    const out = (e: PointerEvent) => !root.contains(e.target as Node) && (open = false)
    addEventListener('pointerdown', out)
    return () => removeEventListener('pointerdown', out)
  })
  $effect(() => {
    void q
    hi = 0
  })
</script>

<div class="sp" bind:this={root}>
  <div class="trigger" class:open>
    <button class="main" aria-haspopup="listbox" aria-expanded={open} onclick={toggle}>
      {#if cur}
        <img src={asset.strategy(cur.id)} alt="" />
        <span class="tn"><b>{cur.name}</b><em>{cur.initiator}</em></span>
      {:else}
        <span class="all">全部策略</span>
      {/if}
      <i class="chev"></i>
    </button>
    {#if cur}<button class="clr" aria-label="清除策略" title="清除策略" onclick={() => pick(null)}><Icon name="close" size={13} /></button>{/if}
  </div>

  {#if open}
    <div class="pop" class:up role="listbox" tabindex="-1" onkeydown={key}>
      <input bind:this={search} bind:value={q} placeholder="搜索策略或发起人" onkeydown={key} />
      <div class="list" bind:this={list} style:max-height="{room}px">
        {#each items as id, i (id ?? '')}
          {@const s = strategy(id)}
          <button class="opt" class:on={id === value} class:hi={i === hi} role="option" aria-selected={id === value} onpointerenter={() => (hi = i)} onclick={() => pick(id)}>
            {#if s}
              <img src={asset.strategy(s.id)} alt="" loading="lazy" />
              <span class="tn"><b>{s.name}</b><em>{s.initiator}</em></span>
            {:else}
              <span class="allopt">全部策略</span>
            {/if}
            {#if id === value}<Icon name="check" size={15} />{/if}
          </button>
        {/each}
        {#if items.length === 1}<p class="none">没有找到「{q}」</p>{/if}
      </div>
    </div>
  {/if}
</div>

<style>
  .sp {
    position: relative;
  }
  .trigger {
    display: flex;
    align-items: center;
    padding-right: 6px;
    background: var(--bg-2);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .main {
    flex: 1;
    min-width: 0;
    display: flex;
    align-items: center;
    gap: 8px;
    min-height: 40px;
    padding: 4px 6px 4px 4px;
    text-align: left;
  }
  .trigger:hover,
  .trigger.open {
    box-shadow: inset 0 0 0 1px var(--mint-lo);
  }
  .trigger img {
    width: 32px;
    height: 32px;
    object-fit: cover;
  }
  .all {
    flex: 1;
    padding-left: 6px;
    color: var(--fg-2);
  }
  .tn {
    flex: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    line-height: 1.25;
  }
  .tn b {
    font-size: 13px;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .tn em {
    font-style: normal;
    font-size: 11px;
    color: var(--fg-3);
  }
  .clr {
    flex: none;
    display: grid;
    place-items: center;
    width: 22px;
    height: 22px;
    color: var(--fg-3);
    border-radius: 50%;
  }
  .clr:hover {
    color: var(--fg-1);
    background: var(--bg-4);
  }
  .chev {
    flex: none;
    width: 7px;
    height: 7px;
    margin: 0 2px 3px;
    border-right: 1.5px solid var(--fg-3);
    border-bottom: 1.5px solid var(--fg-3);
    transform: rotate(45deg);
    transition: transform var(--t-fast);
  }
  .chev {
    order: 9;
  }
  .open .chev {
    transform: translateY(3px) rotate(-135deg);
  }
  .pop {
    position: absolute;
    left: 0;
    right: 0;
    top: calc(100% + 4px);
    z-index: 20;
    display: flex;
    flex-direction: column;
    background: var(--bg-2);
    box-shadow:
      inset 0 0 0 1px var(--line),
      0 12px 32px rgb(0 0 0 / 0.6);
    animation: pop var(--t-fast) var(--ease-out);
  }
  .pop::before {
    content: '';
    position: absolute;
    left: 0;
    top: 0;
    width: 40px;
    height: 2px;
    background: var(--mint);
  }
  .pop.up {
    top: auto;
    bottom: calc(100% + 4px);
    flex-direction: column-reverse;
    animation-name: pop-up;
  }
  .pop.up::before {
    top: auto;
    bottom: 0;
  }
  @keyframes pop-up {
    from {
      opacity: 0;
      transform: translateY(4px);
    }
  }
  @keyframes pop {
    from {
      opacity: 0;
      transform: translateY(-4px);
    }
  }
  .pop input {
    margin: 8px;
    padding: 6px 10px;
    background: var(--bg-1);
  }
  .list {
    overflow-y: auto;
    padding: 0 4px 6px;
  }
  .opt {
    width: 100%;
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 4px 8px 4px 4px;
    text-align: left;
    color: var(--fg-2);
  }
  .opt img {
    width: 28px;
    height: 28px;
    object-fit: cover;
  }
  .opt.hi {
    color: var(--fg-1);
    background: var(--bg-3);
  }
  .opt.on {
    color: var(--mint);
  }
  .opt.on .tn em {
    color: var(--mint-lo);
  }
  .allopt {
    flex: 1;
    height: 28px;
    display: flex;
    align-items: center;
    padding-left: 6px;
  }
  .none {
    margin: 8px;
    font-size: 12px;
    color: var(--fg-4);
  }
</style>
