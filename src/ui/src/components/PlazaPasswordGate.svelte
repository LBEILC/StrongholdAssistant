<script lang="ts">
  import { fly, fade } from 'svelte/transition'
  import { cubicOut } from 'svelte/easing'
  import { app } from '../lib/state.svelte'
  import { plaza } from '../lib/plaza.svelte'

  // 阵容广场必须有密码（v0.9.2，2026-11-14 用户定）：广场账号还没设密码时先设，关不掉；不想设就退出广场登录。
  // 以后用密码登录，验证码邮件只在注册和忘记密码时发（邮件每天能发的数量有限）
  let pw1 = $state('')
  let pw2 = $state('')
  let busy = $state(false)
  let err = $state('')
  const ok = $derived([...pw1].length >= 8 && [...pw1].length <= 64 && pw1 === pw2)
  const show = $derived(!!plaza.me && !plaza.me.hasPassword)

  async function save() {
    busy = true
    err = ''
    try {
      await plaza.setPassword(pw1)
      pw1 = pw2 = ''
      app.showToast('广场密码设好了，以后用邮箱和密码登录')
    } catch (e) {
      err = (e as Error).message
    } finally {
      busy = false
    }
  }
</script>

{#if show}
  <div class="scrim" transition:fade={{ duration: 200 }}></div>
  <div class="wrap">
    <div class="card" role="dialog" aria-modal="true" aria-label="设置阵容广场密码" in:fly={{ y: 24, duration: 280, easing: cubicOut }} out:fade={{ duration: 150 }}>
      <header>
        <span class="term tag">Plaza</span>
        <h3>给阵容广场账号设个密码</h3>
        <p>阵容广场改成用密码登录了。设好以后，用邮箱 <b>{plaza.me?.emailMasked}</b> 和这个密码登录，不用再等验证码邮件。</p>
      </header>
      <div class="body">
        <label><span>密码</span><input type="password" bind:value={pw1} autocomplete="new-password" maxlength="64" placeholder="8～64 位，不能和邮箱一样" /></label>
        <label>
          <span>再输一次</span>
          <input type="password" bind:value={pw2} autocomplete="new-password" maxlength="64" onkeydown={(e) => e.key === 'Enter' && ok && !busy && save()} />
        </label>
        {#if pw2 && pw1 !== pw2}<p class="err">两次输入的不一样。</p>{/if}
        {#if err}<p class="err">{err}</p>{/if}
      </div>
      <footer>
        <button class="ghost" disabled={busy} onclick={() => plaza.logout()}>先退出广场登录</button>
        <button class="primary" disabled={!ok || busy} onclick={save}>{busy ? '保存中…' : '保存'}</button>
      </footer>
    </div>
  </div>
{/if}

<style>
  .scrim {
    position: fixed;
    inset: 0;
    z-index: 72;
    background: var(--scrim-strong);
  }
  .wrap {
    position: fixed;
    inset: 0;
    z-index: 73;
    display: grid;
    place-items: center;
    grid-template-rows: minmax(0, 1fr);
    padding: 16px 12px;
  }
  .card {
    position: relative;
    width: 100%;
    max-width: 420px;
    max-height: 100%;
    display: flex;
    flex-direction: column;
    background:
      var(--noise),
      linear-gradient(180deg, var(--sheet-top), var(--sheet-bottom) 160px);
    box-shadow:
      inset 0 0 0 1px var(--line-strong),
      0 16px 40px rgb(var(--shade-rgb) / 0.6);
  }
  .card::before {
    content: '';
    position: absolute;
    top: 0;
    left: 12px;
    width: 64px;
    height: 2px;
    background: var(--mint);
    box-shadow: var(--mint-glow);
  }
  header {
    padding: 14px 14px 8px;
  }
  .tag {
    font-size: var(--fs-2xs);
    padding: 2px 5px 1px;
    color: var(--mint-ink);
    background: var(--mint);
  }
  h3 {
    margin: 8px 0 4px;
    font-size: var(--fs-xl);
  }
  header p {
    margin: 0;
    font-size: var(--fs-base);
    line-height: 1.6;
    color: var(--fg-3);
  }
  header b {
    color: var(--fg-1);
  }
  .body {
    display: flex;
    flex-direction: column;
    gap: 8px;
    padding: 6px 14px 12px;
  }
  label {
    display: flex;
    flex-direction: column;
    gap: 4px;
  }
  label span {
    font-size: var(--fs-sm);
    color: var(--fg-3);
  }
  input {
    height: 32px;
    padding: 0 8px;
    font-size: var(--fs-base);
    color: var(--fg-1);
    background: var(--bg-0);
    border: 0;
    outline: none;
    box-shadow: inset 0 0 0 1px var(--line);
  }
  input:focus {
    box-shadow: inset 0 0 0 1px var(--mint-lo);
  }
  input::placeholder {
    color: var(--fg-4);
  }
  .err {
    margin: 0;
    font-size: var(--fs-sm);
    color: var(--red-fg-hi);
  }
  footer {
    display: flex;
    justify-content: flex-end;
    gap: 6px;
    padding: 10px 14px;
    border-top: 1px solid var(--line);
    background: var(--bg-1);
  }
  .ghost {
    font-size: var(--fs-base);
    color: var(--fg-3);
    padding: 5px 8px;
  }
  .ghost:hover {
    color: var(--fg-1);
  }
  .primary {
    height: 30px;
    padding: 0 18px;
    font-size: var(--fs-md);
    font-weight: 700;
    color: var(--mint-ink);
    background: var(--mint);
  }
  .primary:hover:not(:disabled) {
    background: var(--mint-hi);
  }
  .primary:disabled {
    opacity: 0.45;
  }
</style>
