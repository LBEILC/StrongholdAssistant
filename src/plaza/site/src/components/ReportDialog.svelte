<script lang="ts">
  import { api, REPORT_REASONS, type ReportReason, type ReportTarget } from '../api'
  import { router } from '../router.svelte'
  import { session, toast } from '../session.svelte'
  import Icon from './Icon.svelte'

  // 举报弹窗：理由单选 + 选填说明。没登录先去登录，登录完回到这页
  let { target, what, onclose }: { target: ReportTarget; what: string; onclose: () => void } = $props()

  const reasons = $derived(
    (Object.keys(REPORT_REASONS) as ReportReason[]).filter((r) => r !== 'broken' || target.kind === 'post'),
  )
  let reason = $state<ReportReason | null>(null)
  let detail = $state('')
  let busy = $state(false)
  let err = $state('')

  $effect(() => {
    if (session.ready && !session.me) router.go(`/login?next=${encodeURIComponent(location.pathname)}`)
  })

  async function send() {
    busy = true
    err = ''
    try {
      await api.report({ target, reason: reason!, ...(detail.trim() && { detail: detail.trim() }) })
      toast.show('已收到举报，管理员会尽快处理。')
      onclose()
    } catch (e) {
      err = (e as Error).message
    } finally {
      busy = false
    }
  }
</script>

<svelte:window onkeydown={(e) => e.key === 'Escape' && onclose()} />

<!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
<div class="mask" onclick={(e) => e.target === e.currentTarget && onclose()}>
  <div class="dlg" role="dialog" aria-modal="true" aria-label="举报">
    <div class="hd">
      <b><Icon name="flag" size={16} />举报{target.kind === 'post' ? '阵容' : target.kind === 'rating' ? '短评' : '评论'}</b>
      <button class="x" onclick={onclose} title="关闭"><Icon name="close" size={16} /></button>
    </div>
    <p class="what">{what}</p>
    <div class="rs" role="radiogroup" aria-label="举报理由">
      {#each reasons as r (r)}
        <button class="r" class:on={reason === r} role="radio" aria-checked={reason === r} onclick={() => (reason = r)}>
          <i class="dot"></i>{REPORT_REASONS[r]}
        </button>
      {/each}
    </div>
    <textarea bind:value={detail} maxlength="200" placeholder="补充说明（选填）"></textarea>
    {#if err}<p class="err">{err}</p>{/if}
    <div class="ft">
      <span class="muted">举报不会公开，只有管理员能看到。</span>
      <button class="btn ghost sm" onclick={onclose}>取消</button>
      <button class="btn primary sm" disabled={!reason || busy} onclick={send}>{busy ? '提交中…' : '提交'}</button>
    </div>
  </div>
</div>

<style>
  .mask {
    position: fixed;
    inset: 0;
    z-index: 90;
    display: grid;
    place-items: center;
    padding: 16px;
    background: rgb(0 0 0 / 0.55);
    animation: fade 120ms ease-out;
  }
  @keyframes fade {
    from {
      opacity: 0;
    }
  }
  .dlg {
    position: relative;
    width: min(420px, 100%);
    padding: 16px 18px 14px;
    background:
      var(--noise),
      linear-gradient(180deg, #23262a, #191b1e);
    box-shadow:
      inset 0 0 0 1px #3a3e43,
      0 12px 32px rgb(0 0 0 / 0.6);
  }
  .dlg::before {
    content: '';
    position: absolute;
    top: 0;
    left: 12px;
    width: 48px;
    height: 2px;
    background: var(--mint);
    box-shadow: var(--mint-glow);
  }
  .hd {
    display: flex;
    align-items: center;
    justify-content: space-between;
  }
  .hd b {
    display: flex;
    align-items: center;
    gap: 6px;
    font-size: 16px;
  }
  .x {
    display: grid;
    place-items: center;
    width: 28px;
    height: 28px;
    color: var(--fg-3);
  }
  .x:hover {
    color: var(--fg-1);
  }
  .what {
    margin: 6px 0 12px;
    font-size: 13px;
    color: var(--fg-3);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .rs {
    display: grid;
    gap: 2px;
    margin-bottom: 10px;
  }
  .r {
    display: flex;
    align-items: center;
    gap: 10px;
    height: 36px;
    padding: 0 10px;
    text-align: left;
    color: var(--fg-2);
    background: var(--bg-2);
  }
  .r:hover {
    color: var(--fg-1);
    background: var(--bg-3);
  }
  .r.on {
    color: var(--fg-1);
    background: var(--bg-3);
    box-shadow: inset 2px 0 0 var(--mint);
  }
  .dot {
    width: 12px;
    height: 12px;
    border-radius: 50%;
    box-shadow: inset 0 0 0 1px var(--fg-4);
  }
  .on .dot {
    background: var(--mint);
    box-shadow:
      inset 0 0 0 3px #1d2023,
      0 0 0 1px var(--mint);
  }
  textarea {
    width: 100%;
    resize: none;
    field-sizing: content;
    min-height: 64px;
    max-height: 160px;
    line-height: 1.6;
  }
  .err {
    margin: 8px 0 0;
  }
  .ft {
    display: flex;
    align-items: center;
    gap: 8px;
    margin-top: 12px;
  }
  .ft .muted {
    flex: 1;
    font-size: 12px;
  }
</style>
