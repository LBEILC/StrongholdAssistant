<script lang="ts">
  import shop from '../assets/shots/game-shop.webp'

  // 游戏商店实拍（终端主题，2000×1125）上画助手的商店标记。坐标是原图像素，按容器宽度（cqw）缩放；
  // 标记样式照 docs/readme/marker-mock.html。crop 只看画面右下的商店区域（原图 x 600–2000、y 600–1125）
  let { crop = false, play = true }: { crop?: boolean; play?: boolean } = $props()

  const W = 2000
  const H = 1125
  const C = { x: 600, y: 600, w: 1400, h: 525 }
  const marks = [
    { r: [1063, 849, 216, 264], kind: 'key', tag: '关键' },
    { r: [1291, 849, 216, 264], kind: 'track', tag: '追踪' },
    { r: [1519, 849, 216, 264], kind: 'sub', tag: '可抓' },
  ]
  const box = $derived(crop ? C : { x: 0, y: 0, w: W, h: H })
  const pct = (v: number, of: number) => `${(v / of) * 100}%`
</script>

<div class="stage" class:play style:aspect-ratio="{box.w} / {box.h}" style:--k={box.w / W}>
  <img
    src={shop}
    alt="游戏商店画面，三张卡被助手描框标记"
    loading="lazy"
    style:width={pct(W, box.w)}
    style:left={pct(-box.x, box.w)}
    style:top={pct(-box.y, box.h)}
  />
  {#each marks as m, i (i)}
    <div
      class="mk {m.kind}"
      style:left={pct(m.r[0] - box.x, box.w)}
      style:top={pct(m.r[1] - box.y, box.h)}
      style:width={pct(m.r[2], box.w)}
      style:height={pct(m.r[3], box.h)}
      style:--i={i}
    >
      <span class="frame"></span>
      <span class="br a"></span><span class="br b"></span>
      <span class="tag">{#if m.kind !== 'sub'}<svg viewBox="0 0 24 24"><path d="M12 18.26 4.95 22.21l1.57-7.93L.59 8.79l8.03-.95L12 .5l3.39 7.34 8.03.95-5.94 5.49 1.58 7.93z" /></svg>{/if}{m.tag}</span>
    </div>
  {/each}
</div>

<style>
  .stage {
    position: relative;
    width: 100%;
    overflow: hidden;
    container-type: inline-size;
    background: var(--bg-1);
    /* 1 原图像素 = 这么多 cqw */
    --px: calc(100cqw / 2000 / var(--k));
  }
  img {
    position: absolute;
    max-width: none;
    height: auto;
  }
  .mk {
    position: absolute;
    margin: calc(var(--px) * -3);
    box-sizing: content-box;
    padding: calc(var(--px) * 3);
    pointer-events: none;
    --c: var(--mint);
    --hi: var(--mint-hi);
    --ink: var(--mint-ink);
    --glow: rgb(var(--mint-rgb) / 0.5);
  }
  .mk.key {
    --c: var(--gold);
    --hi: #ffd36a;
    --ink: var(--gold-ink);
    --glow: rgb(var(--gold-rgb) / 0.5);
  }
  .frame {
    position: absolute;
    inset: 0;
    border: max(1.5px, calc(var(--px) * 4)) solid var(--c);
    box-shadow:
      0 0 0 max(1px, calc(var(--px) * 2)) rgb(0 0 0 / 0.75),
      inset 0 0 0 max(1px, calc(var(--px) * 3)) rgb(0 0 0 / 0.8),
      0 0 calc(var(--px) * 18) calc(var(--px) * 3) var(--glow);
  }
  .sub .frame {
    border-style: dashed;
    box-shadow: 0 0 0 max(1px, calc(var(--px) * 2)) rgb(0 0 0 / 0.6);
  }
  .br {
    position: absolute;
    width: calc(var(--px) * 28);
    height: calc(var(--px) * 28);
    border: 0 solid var(--hi);
    filter: drop-shadow(0 0 1px #000);
  }
  .sub .br {
    display: none;
  }
  .br.a {
    left: -1px;
    top: -1px;
    border-left-width: max(2px, calc(var(--px) * 7));
    border-top-width: max(2px, calc(var(--px) * 7));
  }
  .br.b {
    right: -1px;
    bottom: -1px;
    border-right-width: max(2px, calc(var(--px) * 7));
    border-bottom-width: max(2px, calc(var(--px) * 7));
  }
  .tag {
    position: absolute;
    left: -1px;
    bottom: calc(100% + var(--px) * 7);
    display: flex;
    align-items: center;
    gap: calc(var(--px) * 6);
    height: max(14px, calc(var(--px) * 32));
    padding: 0 max(4px, calc(var(--px) * 12)) 0 max(3px, calc(var(--px) * 9));
    font-size: max(9px, calc(var(--px) * 19));
    font-weight: 700;
    white-space: nowrap;
    color: var(--ink);
    background: var(--c);
    box-shadow:
      0 0 0 max(1px, calc(var(--px) * 2)) rgb(0 0 0 / 0.75),
      0 0 calc(var(--px) * 12) var(--glow);
  }
  .sub .tag {
    color: var(--mint);
    background: rgb(0 0 0 / 0.72);
    box-shadow: inset 0 0 0 1px rgb(var(--mint-rgb) / 0.6);
  }
  .tag svg {
    width: max(8px, calc(var(--px) * 18));
    height: max(8px, calc(var(--px) * 18));
    fill: currentColor;
  }

  /* 标记依次锁定：先收缩到位，再亮标签 */
  .play .mk {
    animation: lock 4.8s var(--ease-out) infinite;
    animation-delay: calc(var(--i) * 260ms + 400ms);
  }
  @keyframes lock {
    0% {
      opacity: 0;
      transform: scale(1.18);
    }
    8% {
      opacity: 1;
      transform: scale(1);
    }
    10% {
      opacity: 0.4;
    }
    12%,
    88% {
      opacity: 1;
      transform: scale(1);
    }
    96%,
    100% {
      opacity: 0;
      transform: scale(1);
    }
  }
  @media (prefers-reduced-motion: reduce) {
    .play .mk {
      animation: none;
    }
  }
</style>
