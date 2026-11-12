<script lang="ts">
  import { tick } from 'svelte'
  // 全局提示框：替换浏览器原生 title 提示。鼠标移到带 title 的元素上时，把 title 挪到 data-tip（原生提示就不会出来），
  // 停 400ms 后在元素上方弹出（上方放不下就放下方，左右不出窗口）；离开、按下、滚动、右键都收起。
  // 组件照常写 title 就行，不用改；Svelte 之后再把 title 写回去也没关系，下次悬停会再挪一次
  let tip = $state<{ text: string; x: number; y: number; below: boolean } | null>(null)
  let box: HTMLElement | undefined = $state()
  let target: HTMLElement | null = null
  let timer = 0
  const GAP = 6

  function over(e: PointerEvent) {
    const el = (e.target as HTMLElement).closest<HTMLElement>('[title], [data-tip]')
    if (el === target) return
    hide()
    if (!el) return
    if (el.hasAttribute('title')) {
      el.dataset.tip = el.getAttribute('title')!
      el.removeAttribute('title')
    }
    if (!el.dataset.tip) return
    target = el
    timer = window.setTimeout(async () => {
      const r = el.getBoundingClientRect()
      tip = { text: el.dataset.tip!, x: r.left, y: r.top, below: false }
      // 先画出来量大小（同一帧里就改位置，看不到跳动），再决定放上方还是下方、左右收进窗口
      await tick()
      if (!box || !tip || target !== el) return
      const b = box.getBoundingClientRect()
      const below = r.top - b.height - GAP < 4
      tip = {
        ...tip,
        below,
        y: below ? r.bottom + GAP : r.top - b.height - GAP,
        x: Math.max(4, Math.min(r.left + r.width / 2 - b.width / 2, innerWidth - b.width - 4)),
      }
    }, 400)
  }
  function hide() {
    clearTimeout(timer)
    target = null
    tip = null
  }
</script>

<svelte:window onpointerover={over} onpointerout={(e) => !e.relatedTarget && hide()} onpointerdown={hide} onwheel={hide} oncontextmenu={hide} onblur={hide} onkeydown={hide} />

{#if tip}
  <div class="tip" class:below={tip.below} bind:this={box} style:left="{tip.x}px" style:top="{tip.y}px" role="tooltip">{tip.text}</div>
{/if}

<style>
  .tip {
    position: fixed;
    z-index: 90;
    max-width: 240px;
    padding: 5px 9px 6px;
    font-size: 12px;
    line-height: 1.5;
    color: var(--fg-1);
    white-space: pre-line;
    pointer-events: none;
    background:
      var(--noise),
      linear-gradient(180deg, #25282c, #1c1e21);
    box-shadow:
      inset 0 0 0 1px #3a3e43,
      inset 2px 0 0 var(--mint),
      0 6px 18px rgb(0 0 0 / 0.55);
    animation: in 140ms var(--ease-out);
  }
  @keyframes in {
    from {
      opacity: 0;
      transform: translateY(3px);
    }
  }
  .tip.below {
    animation-name: in-below;
  }
  @keyframes in-below {
    from {
      opacity: 0;
      transform: translateY(-3px);
    }
  }
</style>
