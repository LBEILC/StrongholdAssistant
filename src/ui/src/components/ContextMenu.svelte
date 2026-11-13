<script lang="ts">
  import { menu, textField, textMenu, type MenuItem } from '../lib/menu.svelte'
  import Icon from './Icon.svelte'

  // 右键菜单：仿游戏终端面板，顶部一条薄荷绿短线；在鼠标处弹出，贴边时往回收。
  // 开关项点了就关；Esc、点别处、滚动、窗口失焦都关
  const m = $derived(menu.open)
  let box: HTMLElement | undefined = $state()
  let pos = $state({ x: 0, y: 0 })
  let armed = $state<number | null>(null)

  $effect(() => {
    if (!m || !box) return
    armed = null
    const pad = 4
    const { width, height } = box.getBoundingClientRect()
    pos = {
      x: Math.max(pad, Math.min(m.x, innerWidth - width - pad)),
      y: Math.max(pad, m.y + height > innerHeight - pad ? m.y - height : m.y),
    }
  })

  const close = () => (menu.open = null)
  function pick(it: MenuItem, i: number) {
    if (it.confirm && armed !== i) {
      armed = i
      return
    }
    close()
    it.run()
  }
</script>

<svelte:window
  onkeydown={(e) => m && e.key === 'Escape' && (e.stopPropagation(), close())}
  onblur={() => {
    // 游戏在前台时右键侧栏，窗口这时才被激活，焦点来回切一下会触发失焦；刚弹出的菜单不因此关掉
    if (performance.now() - menu.at > 600) close()
  }}
  onresize={close}
  onpointerdown={(e) => m && !box?.contains(e.target as Node) && close()}
  onwheel={close}
  oncontextmenu={(e) => {
    // 有菜单的元素自己处理（openMenu 会拦下事件）；输入框弹自己的编辑菜单，其余地方什么都不弹
    const field = textField(e.target)
    if (field) return textMenu(e, field)
    e.preventDefault()
    close()
  }}
/>

{#if m}
  <div class="menu" bind:this={box} style:left="{pos.x}px" style:top="{pos.y}px" role="menu">
    {#if m.title}
      <div class="hd">
        <b>{m.title}</b>
        {#if m.sub}<span>{m.sub}</span>{/if}
      </div>
    {/if}
    {#each m.items as it, i (i)}
      {#if it === 'sep'}
        <i class="sep"></i>
      {:else}
        <button class="it" class:danger={it.tone === 'danger'} class:on={it.on} class:armed={armed === i} role="menuitem" disabled={it.disabled} onclick={() => pick(it, i)}>
          {#if it.icon}<Icon name={it.icon} size={14} />{:else}<span class="ph"></span>{/if}
          <span class="lb">{armed === i ? it.confirm : it.label}</span>
          {#if it.on !== undefined}
            <span class="box">{#if it.on}<Icon name="check" size={11} />{/if}</span>
          {/if}
        </button>
      {/if}
    {/each}
  </div>
{/if}

<style>
  .menu {
    position: fixed;
    z-index: 100;
    min-width: 168px;
    max-width: 240px;
    padding: 4px 0;
    background:
      var(--noise),
      linear-gradient(180deg, var(--pop-top), var(--pop-bottom));
    box-shadow:
      inset 0 0 0 1px var(--line-strong),
      0 8px 24px rgb(var(--shade-rgb) / 0.6),
      0 0 0 1px rgb(var(--shade-rgb) / 0.5);
    transform-origin: top left;
    animation: pop var(--t-fast) var(--ease-out);
  }
  .menu::before {
    content: '';
    position: absolute;
    top: 0;
    left: 8px;
    width: 36px;
    height: 2px;
    background: var(--mint);
    box-shadow: var(--mint-glow);
  }
  @keyframes pop {
    from {
      opacity: 0;
      transform: scale(0.96) translateY(-2px);
    }
  }
  .hd {
    display: flex;
    align-items: baseline;
    gap: 6px;
    padding: 5px 10px 6px;
    margin-bottom: 3px;
    border-bottom: 1px solid var(--line-soft);
    white-space: nowrap;
  }
  .hd b {
    font-size: var(--fs-md);
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .hd span {
    font-size: var(--fs-sm);
    color: var(--fg-4);
  }
  .it {
    position: relative;
    width: 100%;
    height: 28px;
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 0 10px;
    font-size: var(--fs-md);
    text-align: left;
    color: var(--fg-2);
  }
  .it:disabled {
    color: var(--fg-4);
    pointer-events: none;
  }
  .it:hover {
    color: var(--fg-1);
    background: var(--bg-4);
  }
  .it:hover::before {
    content: '';
    position: absolute;
    left: 0;
    top: 5px;
    bottom: 5px;
    width: 2px;
    background: var(--mint);
  }
  .it.danger:hover::before {
    background: var(--red);
  }
  .it.on {
    color: var(--fg-1);
  }
  .it.danger.on,
  .it.armed {
    color: var(--red-fg);
  }
  .it.armed {
    background: var(--red-tint);
  }
  .ph {
    width: 14px;
  }
  .lb {
    flex: 1;
    white-space: nowrap;
  }
  .box {
    width: 14px;
    height: 14px;
    display: grid;
    place-items: center;
    box-shadow: inset 0 0 0 1px var(--fg-4);
  }
  .on .box {
    color: var(--mint-ink);
    background: var(--mint);
    box-shadow: none;
  }
  .danger.on .box {
    color: var(--fg-0);
    background: var(--red);
  }
  .sep {
    display: block;
    height: 1px;
    margin: 3px 8px;
    background: var(--line-soft);
  }
</style>
