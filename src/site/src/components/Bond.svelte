<script lang="ts">
  // 盟约图标：终端皮肤的圆盘位图 + 盟约剪影染色，做法同助手的 BondIcon（素材见 ASSETS.md）
  let { id, size = 40, on = false, title }: { id: string; size?: number; on?: boolean; title?: string } = $props()
</script>

<span class="bond" class:on style:--s="{size}px" {title}>
  <span class="glyph" style:--src="url(/assets/game/bond/{id}.png)"></span>
</span>

<style>
  .bond {
    position: relative;
    display: inline-grid;
    place-items: center;
    width: var(--s);
    height: var(--s);
    flex: none;
    isolation: isolate;
    color: var(--bond-off);
    transition: color 500ms var(--ease-out);
  }
  .bond::before,
  .bond::after {
    content: '';
    position: absolute;
    z-index: -1;
    transition: opacity 500ms var(--ease-out);
  }
  /* 素材带一圈发光：未激活 65px 图里本体直径 31，激活 75px 图里直径 39 */
  .bond::before {
    inset: calc(var(--s) * -17 / 31);
    background: url(/assets/game/skin/terminal/bond_off.png) center / 100% 100% no-repeat;
  }
  .bond::after {
    inset: calc(var(--s) * -18 / 39);
    background: url(/assets/game/skin/terminal/bond_on.png) center / 100% 100% no-repeat;
    opacity: 0;
  }
  .on {
    color: var(--mint-ink);
  }
  .on::before {
    opacity: 0;
  }
  .on::after {
    opacity: 1;
  }
  .glyph {
    width: 64%;
    height: 64%;
  }
</style>
