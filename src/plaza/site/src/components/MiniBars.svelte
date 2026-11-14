<script lang="ts">
  // 后台看板的小柱状图：一项每天的数，单一系列，顶部虚线标最大刻度，鼠标移到哪天就显示那天的数。
  // count 为 null 的日子是「还没开始记录」，画成灰底，不当成 0
  let {
    data,
    label,
    summary = '',
    note = '',
    fmt = (n: number) => n.toLocaleString('zh-CN'),
  }: {
    data: { date: string; count: number | null }[]
    label: string
    summary?: string
    note?: string
    fmt?: (n: number) => string
  } = $props()

  // viewBox 宽 300、高 96，底部留 16 给日期
  const W = 300, H = 96, PAD_B = 16, PAD_T = 12
  const max = $derived.by(() => {
    const m = Math.max(...data.map((x) => x.count ?? 0))
    if (m <= 0) return 1
    const step = 10 ** Math.floor(Math.log10(m))
    return Math.ceil(m / step) * step
  })
  const bw = $derived(W / Math.max(1, data.length))
  const firstRec = $derived(data.findIndex((x) => x.count !== null))
  let hover = $state<number | null>(null)
  const md = (date: string) => date.slice(5).replace('-', '/')
  const r = (h: number) => Math.min(2, h)
</script>

<figure class="chart">
  <figcaption>
    <span>{label}</span>
    {#if summary}<em>{summary}</em>{/if}
  </figcaption>
  <div class="plot" role="img" aria-label="{label}每天的数">
    <svg viewBox="0 0 {W} {H}" preserveAspectRatio="none" onmouseleave={() => (hover = null)}>
      <line class="grid" x1="0" x2={W} y1={PAD_T} y2={PAD_T} />
      <line class="base" x1="0" x2={W} y1={H - PAD_B} y2={H - PAD_B} />
      {#if firstRec !== 0}<rect class="none" x="0" y={PAD_T} width={(firstRec < 0 ? data.length : firstRec) * bw} height={H - PAD_B - PAD_T} />{/if}
      {#each data as x, i (x.date)}
        {@const h = x.count ? ((H - PAD_B - PAD_T) * x.count) / max : 0}
        {#if h > 0}
          <path class="bar" class:on={hover === i} d="M{i * bw + 1},{H - PAD_B} v{-(h - r(h))} q0,-{r(h)} {r(h)},-{r(h)} h{bw - 2 - 2 * r(h)} q{r(h)},0 {r(h)},{r(h)} v{h - r(h)} z" />
        {/if}
        <!-- 命中区比柱子大：整列都能悬停 -->
        <rect class="hit" x={i * bw} y="0" width={bw} height={H - PAD_B} onmouseenter={() => (hover = i)} role="presentation" />
      {/each}
    </svg>
    <span class="ymax term">{fmt(max)}</span>
    {#if firstRec > 0}<span class="since" style:width="{(firstRec / data.length) * 100}%">{md(data[firstRec].date)} 起才开始记录</span>{/if}
    {#if firstRec < 0}<span class="since" style:width="100%">还没开始记录</span>{/if}
    {#if data.length}<div class="xl"><span>{md(data[0].date)}</span><span>{md(data[data.length - 1].date)}</span></div>{/if}
    {#if hover !== null}
      {@const x = data[hover]}
      <div class="tip" style:left="{((hover + 0.5) / data.length) * 100}%" class:right={hover > data.length * 0.7} class:left={hover < data.length * 0.3}>
        <span>{x.date}</span><b>{x.count === null ? '还没开始记录' : fmt(x.count)}</b>
      </div>
    {/if}
  </div>
  {#if note}<p class="note">{note}</p>{/if}
</figure>

<style>
  .chart {
    margin: 0;
    padding: 12px 14px 10px;
    background: var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line-soft, var(--line));
  }
  figcaption {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    gap: 8px;
    margin-bottom: 8px;
    font-size: 13px;
    color: var(--fg-2);
  }
  figcaption em {
    font-style: normal;
    font-size: 12px;
    color: var(--fg-4);
  }
  .plot {
    position: relative;
  }
  svg {
    display: block;
    width: 100%;
    height: 110px;
  }
  .grid {
    stroke: var(--line);
    stroke-width: 1;
    stroke-dasharray: 2 3;
    vector-effect: non-scaling-stroke;
  }
  .base {
    stroke: var(--line);
    stroke-width: 1;
    vector-effect: non-scaling-stroke;
  }
  .none {
    fill: var(--bg-2);
    opacity: 0.6;
  }
  .bar {
    fill: var(--mint-lo);
  }
  .bar.on {
    fill: var(--mint);
  }
  .hit {
    fill: transparent;
  }
  .ymax {
    position: absolute;
    top: 0;
    left: 0;
    font-size: 11px;
    color: var(--fg-4);
    transform: translateY(-2px);
  }
  .since {
    position: absolute;
    left: 0;
    top: 12px;
    height: 76px;
    display: grid;
    place-items: center;
    font-size: 12px;
    color: var(--fg-4);
    pointer-events: none;
  }
  .xl {
    display: flex;
    justify-content: space-between;
    margin-top: -14px;
    font-size: 11px;
    color: var(--fg-4);
    pointer-events: none;
  }
  .tip {
    position: absolute;
    top: -6px;
    transform: translate(-50%, -100%);
    display: flex;
    flex-direction: column;
    align-items: center;
    padding: 4px 8px;
    font-size: 12px;
    white-space: nowrap;
    color: var(--fg-2);
    background: var(--bg-3, var(--bg-2));
    box-shadow: 0 0 0 1px var(--line), 0 4px 12px rgb(0 0 0 / 0.4);
    pointer-events: none;
    z-index: 2;
  }
  .tip.right {
    transform: translate(-100%, -100%);
  }
  .tip.left {
    transform: translate(0, -100%);
  }
  .tip b {
    font-size: 14px;
    color: var(--fg-1);
  }
  .note {
    margin: 6px 0 0;
    font-size: 11px;
    color: var(--fg-4);
  }
</style>
