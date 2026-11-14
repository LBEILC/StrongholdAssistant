<script lang="ts">
  import type { Snippet } from 'svelte'
  import { reveal } from '../lib'

  // 区块标题：编号 + 中文大标题 + 英文小字，背后一行描边的英文大字（同 README 功能图的做法）
  let { no, title, en, small = false, children }: { no: string; title: string; en: string; small?: boolean; children?: Snippet } = $props()
</script>

<header class="sh" use:reveal>
  {#if !small}<span class="ghost" aria-hidden="true">{en}</span>{/if}
  <span class="lbl"><b class="mint">// {no}</b>&nbsp;&nbsp;{en}</span>
  <h2>{title}</h2>
  <div class="bars" aria-hidden="true"><i></i><i></i><i></i></div>
  {#if children}<p class="lead">{@render children()}</p>{/if}
</header>

<style>
  .sh {
    position: relative;
    margin-bottom: 56px;
  }
  .ghost {
    position: absolute;
    right: 0;
    top: -0.32em;
    font-family: var(--font-term);
    font-size: clamp(64px, 11vw, 150px);
    font-weight: 700;
    line-height: 1;
    letter-spacing: 0.04em;
    text-transform: uppercase;
    white-space: nowrap;
    color: transparent;
    -webkit-text-stroke: 1px rgb(var(--hl-rgb) / 0.07);
    pointer-events: none;
    user-select: none;
  }
  .lbl b {
    font-weight: 600;
  }
  h2 {
    position: relative;
    margin: 10px 0 18px;
    font-size: var(--d-1);
    font-weight: 700;
    line-height: 1.2;
    letter-spacing: 0.02em;
  }
  .bars i {
    width: 72px;
  }
  .bars i:nth-child(2) {
    width: 24px;
  }
  .bars i:nth-child(3) {
    width: 10px;
  }
  .lead {
    position: relative;
    max-width: 640px;
    margin-top: 22px;
    font-size: 16px;
    color: var(--fg-2);
  }
  @media (max-width: 720px) {
    .sh {
      margin-bottom: 36px;
    }
    .ghost {
      display: none;
    }
    .lead {
      font-size: 15px;
    }
  }
</style>
