<script lang="ts">
  import type { Snippet } from 'svelte'

  // 子页面的页头：编号路径 + 大标题 + 说明，背后一行描边英文
  let { en, title, children }: { en: string; title: string; children?: Snippet } = $props()
</script>

<header class="ph">
  <div class="wrap">
    <span class="ghost" aria-hidden="true">{en}</span>
    <span class="lbl"><a href="/">Stronghold Assistant</a> <span class="mint">//</span> {en}</span>
    <h1>{title}</h1>
    <div class="bars" aria-hidden="true"><i></i><i></i><i></i></div>
    {#if children}<p class="lead">{@render children()}</p>{/if}
  </div>
</header>

<style>
  .ph {
    position: relative;
    padding: calc(var(--head-h) + 72px) 0 56px;
    border-bottom: 1px solid var(--line-soft);
    background:
      radial-gradient(800px 300px at 15% 0%, rgb(var(--mint-rgb) / 0.08), transparent 70%),
      var(--hatch);
    overflow: hidden;
  }
  .wrap {
    position: relative;
  }
  .ghost {
    position: absolute;
    right: var(--gutter);
    bottom: -0.3em;
    font-family: var(--font-term);
    font-size: clamp(72px, 13vw, 180px);
    font-weight: 700;
    line-height: 1;
    text-transform: uppercase;
    white-space: nowrap;
    color: transparent;
    -webkit-text-stroke: 1px rgb(var(--hl-rgb) / 0.07);
    pointer-events: none;
  }
  .lbl a:hover {
    color: var(--fg-1);
  }
  h1 {
    margin: 14px 0 20px;
    font-size: var(--d-1);
    font-weight: 700;
    line-height: 1.15;
    color: var(--fg-0);
  }
  .lead {
    max-width: 640px;
    margin-top: 22px;
    color: var(--fg-2);
    font-size: 16px;
  }
  @media (max-width: 720px) {
    .ph {
      padding: calc(var(--head-h) + 40px) 0 40px;
    }
  }
</style>
