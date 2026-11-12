<script lang="ts">
  import { app } from '../lib/state.svelte'
  import { asset, ROMAN } from '../lib/assets'
  import Icon from './Icon.svelte'
  import { openMenu, operatorMenu } from '../lib/menu.svelte'

  // 小头像格：阵容、禁用列表里用。点一下切换「已持有」或看详情，由调用方决定
  interface Props {
    name: string
    size?: number
    owned?: boolean
    dim?: boolean
    /** 转职盟约：左下角小徽记 */
    transform?: string
    showName?: boolean
    /** 阵容里的关键干员：左上角金色三角 */
    isKey?: boolean
    /** 目标精锐：左下角金色 V 标（v0.7.0） */
    elite?: boolean
    /** 技能或模组跟默认不一样时，右下角显示这个技能的图标（v0.7.0） */
    skill?: string | null
    /** 右键弹干员菜单；编辑器、挑选面板里关掉 */
    menu?: boolean
    onclick?: (e: MouseEvent) => void
  }
  let { name, size = 48, owned = false, dim = false, transform, showName = true, isKey = false, elite = false, skill = null, menu = true, onclick }: Props = $props()
  const op = $derived(app.op(name))
  const banned = $derived(app.banned.has(name))
  const focus = $derived(app.focusOps.has(name))
</script>

<button class="tile" class:owned class:dim class:banned class:focus style:--s="{size}px" style:--c="var(--tier-{op.tier})" {onclick} oncontextmenu={menu ? (e) => openMenu(e, operatorMenu(name)) : undefined} title="{name} · {ROMAN[op.tier]} 阶">
  <span class="pic">
    <img class="av" src={asset.operator(name)} alt="" loading="lazy" />
    {#if owned}<span class="check"><Icon name="check" size={11} /></span>{/if}
    {#if banned}<img class="ban" src={asset.stage('img_chess_banned')} alt="禁用" />{/if}
    {#if isKey}<span class="key" title="关键干员"></span>{/if}
    {#if focus}<span class="fav" title="特别关注"><Icon name="heartFill" size={9} /></span>{/if}
    <!-- 精锐：游戏图鉴里精锐状态的标签，贴在头像左下角 -->
    {#if elite}<img class="el" src={asset.card('icon_golden')} alt="精锐" title="目标精锐" />{/if}
    {#if skill}<img class="sk" src={asset.skill(skill)} alt="" title="技能：{op.skills?.find((x) => x.id === skill)?.name ?? ''}" />{/if}
    {#if transform}<span class="tf" title="转职：{app.cov(transform).name}"><span class="glyph" style:--src="url({asset.bond(transform)})"></span></span>{/if}
  </span>
  {#if showName}<span class="nm">{name}</span>{/if}
</button>

<style>
  .tile {
    display: inline-flex;
    flex-direction: column;
    align-items: center;
    gap: 3px;
    width: var(--s);
  }
  .pic {
    position: relative;
    width: var(--s);
    height: var(--s);
    background: var(--bg-3);
    box-shadow:
      inset 0 0 0 1px var(--line),
      inset 0 -2px 0 var(--c);
    overflow: hidden;
    transition: box-shadow var(--t-fast);
  }
  .av {
    width: 100%;
    height: 100%;
    object-fit: cover;
    transition:
      filter var(--t-mid),
      opacity var(--t-mid);
  }
  .tile:hover .pic {
    box-shadow:
      inset 0 0 0 1px var(--fg-3),
      inset 0 -2px 0 var(--c);
  }
  .dim .av {
    filter: grayscale(0.6) brightness(0.7);
  }
  .owned .pic {
    box-shadow:
      inset 0 0 0 1.5px var(--mint),
      0 0 8px rgb(34 227 178 / 0.3);
  }
  .check {
    position: absolute;
    right: 0;
    top: 0;
    width: 18px;
    height: 18px;
    display: flex;
    justify-content: flex-end;
    padding: 1px;
    color: var(--mint-ink);
    background: var(--mint);
    clip-path: polygon(0 0, 100% 0, 100% 100%);
  }
  /* 禁用：和游戏禁用列表一样，头像照常，右上角贴红色三角角标。
     素材 73×73 里三角只占中间 37×37，放大到头像宽 0.6 倍、往右上移 0.14 倍，三角直角正好卡在角上 */
  .ban {
    position: absolute;
    right: calc(var(--s) * -0.14);
    top: calc(var(--s) * -0.14);
    z-index: 1;
    width: calc(var(--s) * 0.6);
    height: calc(var(--s) * 0.6);
  }
  /* 特别关注：金色描边 + 右下角爱心 */
  .focus .pic {
    outline: 1.5px solid var(--gold);
    outline-offset: 1px;
  }
  .fav {
    position: absolute;
    right: 1px;
    bottom: 3px;
    z-index: 2;
    width: 13px;
    height: 13px;
    display: grid;
    place-items: center;
    border-radius: 50%;
    color: var(--gold-ink);
    background: var(--gold);
    box-shadow: 0 0 0 1px rgb(0 0 0 / 0.6);
  }
  .key {
    position: absolute;
    left: 0;
    top: 0;
    z-index: 2;
    width: 11px;
    height: 11px;
    background: var(--gold);
    clip-path: polygon(0 0, 100% 0, 0 100%);
  }
  /* 素材是图鉴的标签底板（60×27），双箭头在横向第 10～37 像素，右边留给游戏写字。
     只框出第 4～43 像素（图案居中，左右各留 6 像素） */
  .el {
    position: absolute;
    left: 0;
    bottom: 2px;
    z-index: 2;
    height: max(11px, calc(var(--s) * 0.26));
    aspect-ratio: 40 / 27;
    object-view-box: inset(0px 16px 0px 4px);
    box-shadow: 0 0 0 1px rgb(0 0 0 / 0.6);
  }
  .sk {
    position: absolute;
    right: 1px;
    bottom: 3px;
    z-index: 2;
    width: 14px;
    height: 14px;
    box-shadow: 0 0 0 1px rgb(0 0 0 / 0.7);
    background: var(--bg-0);
  }
  .tf {
    position: absolute;
    left: 1px;
    bottom: 3px;
    z-index: 1;
    width: 15px;
    height: 15px;
    display: grid;
    place-items: center;
    border-radius: 50%;
    color: var(--gold-ink);
    background: var(--gold);
    box-shadow: 0 0 0 1px rgb(0 0 0 / 0.6);
  }
  .tf .glyph {
    width: 11px;
    height: 11px;
  }
  .nm {
    font-size: 11px;
    color: var(--fg-2);
    max-width: calc(var(--s) + 8px);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
</style>
