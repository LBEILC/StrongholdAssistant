<script lang="ts">
  import Icon from './Icon.svelte'
  import type { Qa } from '../faq'

  // 手风琴：用 <details>，不靠脚本也能展开；地址带 #问题 id 时自动展开那一条
  let { items }: { items: Qa[] } = $props()
  const hash = typeof location === 'undefined' ? '' : location.hash.slice(1)
</script>

<div class="faq">
  {#each items as it (it.id)}
    <details id={it.id} open={hash === it.id}>
      <summary>
        <span class="q">{it.q}</span>
        <span class="tg"><Icon name="plus" size={18} /></span>
      </summary>
      <div class="a prose">{@html it.a}</div>
    </details>
  {/each}
</div>

<style>
  .faq {
    border-top: 1px solid var(--line);
  }
  details {
    border-bottom: 1px solid var(--line);
    transition: background var(--t-fast);
  }
  details[open] {
    background: linear-gradient(90deg, rgb(var(--mint-rgb) / 0.05), transparent 60%);
  }
  summary {
    display: flex;
    align-items: center;
    gap: 16px;
    padding: 20px 4px 20px 16px;
    cursor: pointer;
    list-style: none;
    position: relative;
  }
  summary::-webkit-details-marker {
    display: none;
  }
  summary::before {
    content: '';
    position: absolute;
    left: 0;
    top: 22px;
    bottom: 22px;
    width: 2px;
    background: var(--fg-4);
    transition: background var(--t-fast);
  }
  details[open] summary::before,
  summary:hover::before {
    background: var(--mint);
    box-shadow: var(--mint-glow);
  }
  .q {
    flex: 1;
    font-size: 16px;
    font-weight: 600;
  }
  .tg {
    display: grid;
    place-items: center;
    width: 30px;
    height: 30px;
    color: var(--fg-3);
    border: 1px solid var(--line);
    border-radius: var(--r-1);
    transition:
      transform var(--t-mid) var(--ease-out),
      color var(--t-fast),
      border-color var(--t-fast);
  }
  summary:hover .tg {
    color: var(--mint);
    border-color: var(--mint-lo);
  }
  details[open] .tg {
    transform: rotate(45deg);
    color: var(--mint);
  }
  .a {
    padding: 0 56px 24px 16px;
    color: var(--fg-2);
    animation: open var(--t-mid) var(--ease-out);
  }
  .a :global(b) {
    color: var(--fg-1);
  }
  @keyframes open {
    from {
      opacity: 0;
      transform: translateY(-4px);
    }
  }
  @media (max-width: 720px) {
    .a {
      padding-right: 8px;
    }
    .q {
      font-size: 15px;
    }
  }
</style>
