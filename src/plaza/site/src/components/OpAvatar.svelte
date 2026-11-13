<script lang="ts">
  import { asset, op, ROMAN } from '../data'

  // 干员头像：底边等阶色，关键干员左上角金角，优先装备挂在右下角
  interface Props {
    name: string
    size?: number
    isKey?: boolean
    /** 目标精锐：左下角游戏里的精锐标签（v0.7.0） */
    elite?: boolean
    priority?: string[]
    showName?: boolean
  }
  let { name, size = 40, isKey = false, elite = false, priority = [], showName = false }: Props = $props()
  const o = $derived(op(name))
</script>

<span class="oa" style:--s="{size}px" style:--c="var(--tier-{o.tier})" title="{name}（{ROMAN[o.tier]} 阶）{isKey ? ' · 关键' : ''}{elite ? ' · 精锐' : ''}{priority.length ? ` · 优先装备：${priority.join('、')}` : ''}">
  <span class="pic">
    <img src={asset.operator(name)} alt={name} loading="lazy" />
    {#if isKey}<i class="key"></i>{/if}
    {#if elite}<img class="el" src={asset.card('icon_golden')} alt="精锐" />{/if}
    {#if priority.length}
      <span class="pe">{#each priority.slice(0, 2) as e (e)}<img src={asset.equipment(e)} alt="" />{/each}</span>
    {/if}
  </span>
  {#if showName}<span class="nm">{name}</span>{/if}
</span>

<style>
  .oa {
    display: inline-flex;
    flex-direction: column;
    align-items: center;
    gap: 4px;
    width: var(--s);
  }
  .pic {
    position: relative;
    width: var(--s);
    height: var(--s);
    background: var(--bg-3);
    box-shadow: inset 0 -3px 0 var(--c);
  }
  .pic > img {
    width: 100%;
    height: 100%;
    object-fit: cover;
  }
  .pic::after {
    content: '';
    position: absolute;
    inset: auto 0 0;
    height: 3px;
    background: var(--c);
  }
  .key {
    position: absolute;
    left: 0;
    top: 0;
    width: 0;
    height: 0;
    border-style: solid;
    border-width: calc(var(--s) * 0.32) calc(var(--s) * 0.32) 0 0;
    border-color: var(--gold) transparent transparent transparent;
  }
  .pic > img.el {
    width: auto;
    position: absolute;
    left: 0;
    bottom: 3px;
    z-index: 2;
    height: max(11px, calc(var(--s) * 0.26));
    aspect-ratio: 40 / 27;
    object-view-box: inset(0px 16px 0px 4px);
    box-shadow: 0 0 0 1px rgb(0 0 0 / 0.6);
  }
  /* 角标伸出头像外面，要压在右边那张头像的精锐标签（z-index 2）和等阶色条上面 */
  .pe {
    position: absolute;
    z-index: 3;
    right: -4px;
    bottom: -4px;
    display: flex;
    gap: 1px;
    padding: 1px;
    background: var(--bg-0);
    box-shadow: inset 0 0 0 1px var(--gold);
  }
  .pe img {
    width: calc(var(--s) * 0.34);
    height: calc(var(--s) * 0.34);
    object-fit: contain;
  }
  .nm {
    max-width: calc(var(--s) + 16px);
    font-size: 12px;
    color: var(--fg-2);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
</style>
