<script lang="ts">
  import { asset, cov } from '../data'

  // 目标盟约：图标 + 名字 + 档位；给了 have / need 时写人数，没凑够的变灰
  interface Props {
    id: string
    level?: number
    have?: number
    need?: number
  }
  let { id, level = 1, have, need }: Props = $props()
  const c = $derived(cov(id))
  const short = $derived(have !== undefined && need !== undefined && have < need)
</script>

<span class="bc" class:core={c.kind === 'core'} class:short>
  <span class="glyph" style:--src="url({asset.bond(id)})"></span>
  {c.name}{#if level === 2}<em>II</em>{/if}
  {#if have !== undefined && need !== undefined}<b>{have}/{need}</b>{/if}
</span>

<style>
  .bc {
    display: inline-flex;
    align-items: center;
    gap: 5px;
    height: 26px;
    padding: 0 9px 0 5px;
    font-size: 12.5px;
    color: var(--fg-1);
    background: var(--bg-2);
    box-shadow: inset 0 0 0 1px var(--line);
    border-radius: 13px;
  }
  .glyph {
    width: 18px;
    height: 18px;
    color: var(--mint);
  }
  .bc.core .glyph {
    color: var(--mint-hi);
  }
  em {
    font-style: normal;
    font-family: var(--font-term);
    font-size: 11px;
    color: var(--mint);
  }
  b {
    font-family: var(--font-term);
    font-weight: 500;
    color: var(--mint);
  }
  .short b {
    color: var(--gold);
  }
</style>
