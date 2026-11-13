<script lang="ts">
  // 盟约选择按钮组：编辑器的目标盟约和筛选、禁用挑选面板、图鉴筛选共用。
  // 按钮固定 --bond-pick 大小，放不下就换行，侧栏和工作台里一样大。
  import { app } from '../lib/state.svelte'
  import { asset } from '../lib/assets'

  type PickState = {
    on?: boolean
    badge?: string // 角标，如档位 I / II
    inc?: boolean // 本局阵容不完整：红色细圈
    dim?: boolean // 不在可选范围：变淡
    disabled?: boolean
  }
  type Props = {
    state: (id: string) => PickState
    onpick: (id: string) => void
  }
  let { state, onpick }: Props = $props()
</script>

<div class="bondpick">
  {#each app.catalog.covenants as c (c.id)}
    {@const s = state(c.id)}
    <button class="bp" class:on={s.on} class:core={c.kind === 'core'} class:inc={s.inc} class:dim={s.dim} disabled={s.disabled} title={c.name} onclick={() => onpick(c.id)}>
      <span class="glyph" style:--src="url({asset.bond(c.id)})"></span>
      {#if s.badge}<span class="lv term">{s.badge}</span>{/if}
    </button>
  {/each}
</div>

<style>
  .bondpick {
    display: grid;
    grid-template-columns: repeat(auto-fill, var(--bond-pick));
    justify-content: space-between;
    gap: 4px;
  }
  .bp {
    position: relative;
    width: var(--bond-pick);
    height: var(--bond-pick);
    display: grid;
    place-items: center;
    border-radius: 50%;
    color: var(--fg-3);
    background: var(--bg-2);
  }
  /* 核心盟约：细圈 + 亮一级，和附加盟约分开 */
  .bp.core {
    color: var(--fg-2);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .bp:hover:not(:disabled) {
    color: var(--fg-1);
  }
  .bp:focus-visible {
    outline-offset: 2px;
  }
  .bp .glyph {
    width: 62%;
    height: 62%;
  }
  .bp.inc {
    color: var(--fg-2);
    box-shadow: inset 0 0 0 1px rgb(var(--red-rgb) / 0.6);
  }
  .bp.dim {
    opacity: 0.3;
  }
  .bp:disabled {
    cursor: default;
  }
  .bp.on {
    color: var(--mint-ink);
    background: var(--mint);
    box-shadow: var(--mint-glow);
    opacity: 1;
  }
  .lv {
    position: absolute;
    right: -3px;
    bottom: -3px;
    font-size: var(--fs-3xs);
    line-height: 1;
    padding: 2px 3px 1px;
    color: var(--gold-ink);
    background: var(--gold);
  }
</style>
