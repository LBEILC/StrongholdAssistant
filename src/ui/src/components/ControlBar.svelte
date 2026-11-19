<script lang="ts">
  import { fly, fade } from 'svelte/transition'
  import { cubicOut } from 'svelte/easing'
  import { api, type ControlStep } from '../lib/api'
  import { app } from '../lib/state.svelte'
  import { control, MODES } from '../lib/control.svelte'
  import Icon from './Icon.svelte'

  // 自动操作的执行浮条（docs/自动操作.md「执行器」）：一批操作开始后常驻在侧栏底部，
  // 显示进度、当前这一步和标注图、停止；逐步确认时在这里点「执行」「跳过」；结束后留着结果，点关闭才收
  const job = $derived(control.job)
  const show = $derived(!!job && control.dismissed !== job.id)
  const running = $derived(control.running)
  const done = $derived(job ? job.steps.filter((s) => s.state === 'done').length : 0)
  // 当前要看的那一步：正在做 / 等确认的那步；结束了就是出问题的那步或最后做完的一步
  const current = $derived.by((): ControlStep | null => {
    if (!job) return null
    const s = job.steps
    return (
      s.find((x) => x.state === 'running') ??
      (job.state === 'waitingConfirm' ? s.find((x) => x.state === 'pending') : undefined) ??
      s.find((x) => x.state === 'failed') ??
      [...s].reverse().find((x) => x.state === 'done') ??
      s[0] ??
      null
    )
  })
  const index = $derived(job && current ? job.steps.indexOf(current) + 1 : 0)
  const pc = $derived((app.connection.source ?? 'pc') === 'pc')
  const stopKey = $derived(control.settings.stopHotkey)
  let list = $state(false)

  const MARK: Record<ControlStep['state'], string> = {
    pending: '', running: '…', done: '✓', failed: '✕', skipped: '跳过', cancelled: '取消',
  }
  const summary = $derived.by(() => {
    if (!job) return ''
    if (job.state === 'done') {
      if (job.mode === 'dryRun') return `试运行做完：${job.steps.length} 步都只标在截图上，没有碰游戏。点图看每一步会点哪。`
      const unchecked = job.steps.filter((s) => s.state === 'done' && s.verified === null).length
      return `做完了 ${done} 步${unchecked ? `，其中 ${unchecked} 步读不出结果、没核对（如朝向）` : '，都核对过'}。`
    }
    return job.message ?? (job.state === 'stopped' ? '已停止' : '出错停下了')
  })
</script>

{#if show && job}
  <div class="bar" class:bad={job.state === 'failed'} class:wait={job.state === 'waitingConfirm'} in:fly={{ y: 16, duration: 220, easing: cubicOut }} out:fade={{ duration: 150 }}>
    <div class="top">
      <Icon name="robot" size={15} />
      <b>{job.source === 'api' ? '外部程序在操作' : '自动操作'}</b>
      <span class="mode m-{job.mode}">{MODES.find((m) => m.id === job.mode)?.label}</span>
      <button class="prog term" onclick={() => (list = !list)} title="看全部步骤">{index}/{job.steps.length}<Icon name={list ? 'arrowUp' : 'arrowDown'} size={12} /></button>
      <span class="sp"></span>
      {#if running}
        <button class="stop" onclick={() => api.stopControl()}><Icon name="stop" size={12} />停止</button>
      {:else}
        <button class="x" onclick={() => (control.dismissed = job.id)} aria-label="关闭"><Icon name="close" size={14} /></button>
      {/if}
    </div>

    {#if current}
      <div class="now">
        {#if current.preview}
          <button class="thumb" onclick={() => (control.zoom = current.preview)} title="看大图"><img src={current.preview} alt="第 {index} 步会点哪里" /></button>
        {/if}
        <div class="txt">
          <span class="lbl">{job.state === 'waitingConfirm' ? '下一步' : running ? '正在做' : `第 ${index} 步`}</span>
          <p>{current.text}</p>
          {#if current.problem}<p class="problem">{current.problem}</p>{/if}
        </div>
      </div>
    {/if}

    {#if job.state === 'waitingConfirm'}
      <div class="ask">
        <button class="run" onclick={() => api.confirmStep(job.id, 'run')}><Icon name="play" size={12} />执行这一步</button>
        <button class="skip" onclick={() => api.confirmStep(job.id, 'skip')}><Icon name="skip" size={13} />跳过</button>
      </div>
    {/if}

    {#if running}
      <p class="hint">
        {#if pc && job.mode !== 'dryRun' && job.state === 'running'}<em>正在用你的鼠标操作游戏，别动鼠标和键盘，一动就停。</em>{/if}
        {#if stopKey}按 <kbd>{stopKey}</kbd> 随时停止。{/if}
      </p>
    {:else}
      <p class="result">{summary}</p>
    {/if}

    {#if list}
      <ol class="steps">
        {#each job.steps as s, i (i)}
          <li class="s-{s.state}" class:cur={s === current}>
            <i>{i + 1}</i>
            <button class="st" disabled={!s.preview} onclick={() => s.preview && (control.zoom = s.preview)}>{s.text}</button>
            <em>{MARK[s.state]}{s.state === 'done' && s.verified === null && job.mode !== 'dryRun' ? ' 未核对' : ''}</em>
          </li>
        {/each}
      </ol>
    {/if}
  </div>
{/if}

{#if control.zoom}
  <div class="zoom" transition:fade={{ duration: 150 }} onclick={() => (control.zoom = null)} role="presentation">
    <img src={control.zoom} alt="标注图" />
    <span>点任意处关闭</span>
  </div>
{/if}

<style>
  .bar {
    position: absolute;
    left: 8px;
    right: 8px;
    bottom: 8px;
    z-index: 40;
    display: flex;
    flex-direction: column;
    gap: 7px;
    padding: 9px 10px 10px;
    background:
      var(--noise),
      linear-gradient(180deg, var(--pop-top), var(--pop-bottom));
    box-shadow:
      inset 0 0 0 1px var(--line-strong),
      inset 0 2px 0 var(--mint),
      0 10px 28px rgb(var(--shade-rgb) / 0.55);
  }
  .bar.wait {
    box-shadow:
      inset 0 0 0 1px var(--line-strong),
      inset 0 2px 0 var(--gold),
      0 10px 28px rgb(var(--shade-rgb) / 0.55);
  }
  .bar.bad {
    box-shadow:
      inset 0 0 0 1px var(--line-strong),
      inset 0 2px 0 var(--red),
      0 10px 28px rgb(var(--shade-rgb) / 0.55);
  }
  .top {
    display: flex;
    align-items: center;
    gap: 6px;
    color: var(--mint);
  }
  .top b {
    font-size: var(--fs-md);
    color: var(--fg-1);
  }
  .mode {
    font-size: var(--fs-2xs);
    padding: 1px 5px;
    color: var(--fg-2);
    background: var(--bg-3);
  }
  .mode.m-auto {
    color: var(--mint-ink);
    background: var(--mint);
  }
  .mode.m-confirm {
    color: var(--gold-ink);
    background: var(--gold);
  }
  .prog {
    display: inline-flex;
    align-items: center;
    gap: 2px;
    font-size: var(--fs-sm);
    color: var(--fg-3);
  }
  .prog:hover {
    color: var(--fg-1);
  }
  .sp {
    flex: 1;
  }
  .stop {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    height: 24px;
    padding: 0 10px;
    font-size: var(--fs-sm);
    font-weight: var(--fw-bold);
    color: #fff;
    background: var(--red);
  }
  .x {
    color: var(--fg-3);
    padding: 2px;
  }
  .x:hover {
    color: var(--fg-1);
  }
  .now {
    display: flex;
    gap: 8px;
    align-items: flex-start;
  }
  .thumb {
    flex: none;
    width: 112px;
    aspect-ratio: 16 / 9;
    padding: 0;
    overflow: hidden;
    background: var(--bg-0);
    box-shadow: inset 0 0 0 1px var(--line);
  }
  .thumb img {
    width: 100%;
    height: 100%;
    object-fit: cover;
    display: block;
  }
  .txt {
    min-width: 0;
  }
  .lbl {
    font-size: var(--fs-2xs);
    color: var(--fg-4);
  }
  .txt p {
    margin: 1px 0 0;
    font-size: var(--fs-base);
    line-height: 1.5;
    color: var(--fg-1);
  }
  .txt .problem {
    font-size: var(--fs-sm);
    color: var(--red-fg-hi);
  }
  .ask {
    display: flex;
    gap: 6px;
  }
  .run {
    flex: 1;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: 4px;
    height: 28px;
    font-size: var(--fs-md);
    font-weight: var(--fw-bold);
    color: var(--mint-ink);
    background: var(--mint);
  }
  .run:hover {
    background: var(--mint-hi);
  }
  .skip {
    display: inline-flex;
    align-items: center;
    gap: 3px;
    height: 28px;
    padding: 0 12px;
    font-size: var(--fs-base);
    color: var(--fg-2);
    background: var(--bg-3);
  }
  .hint,
  .result {
    margin: 0;
    font-size: var(--fs-sm);
    line-height: 1.55;
    color: var(--fg-3);
  }
  .hint em {
    display: block;
    font-style: normal;
    color: var(--gold);
  }
  .bad .result {
    color: var(--red-fg-hi);
  }
  kbd {
    font-family: var(--font-term);
    font-size: var(--fs-xs);
    padding: 0 4px;
    color: var(--fg-1);
    background: var(--bg-3);
    box-shadow: inset 0 -1px 0 var(--line);
  }
  .steps {
    margin: 0;
    padding: 6px 0 0;
    list-style: none;
    max-height: 180px;
    overflow-y: auto;
    border-top: 1px solid var(--line-soft);
  }
  .steps li {
    display: flex;
    align-items: baseline;
    gap: 6px;
    padding: 2px 0;
    font-size: var(--fs-sm);
    color: var(--fg-2);
  }
  .steps li i {
    font-style: normal;
    width: 16px;
    flex: none;
    text-align: right;
    color: var(--fg-4);
  }
  .steps .st {
    flex: 1;
    min-width: 0;
    padding: 0;
    text-align: left;
    color: inherit;
  }
  .steps .st:not(:disabled):hover {
    color: var(--fg-1);
    text-decoration: underline;
  }
  .steps em {
    font-style: normal;
    font-size: var(--fs-xs);
    color: var(--fg-4);
  }
  .steps .s-done em {
    color: var(--mint);
  }
  .steps .s-failed {
    color: var(--red-fg-hi);
  }
  .steps .s-failed em {
    color: var(--red-fg-hi);
  }
  .steps .s-cancelled,
  .steps .s-skipped {
    color: var(--fg-4);
  }
  .steps li.cur {
    color: var(--fg-1);
  }
  .zoom {
    position: fixed;
    inset: 0;
    z-index: 90;
    display: grid;
    place-items: center;
    gap: 8px;
    align-content: center;
    padding: 12px;
    background: rgb(var(--shade-rgb) / 0.86);
    cursor: zoom-out;
  }
  .zoom img {
    max-width: 100%;
    max-height: calc(100vh - 60px);
    box-shadow: 0 0 0 1px var(--line-strong);
  }
  .zoom span {
    font-size: var(--fs-sm);
    color: var(--fg-3);
  }
</style>
