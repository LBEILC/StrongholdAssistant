<script lang="ts">
  import { api } from '../api'
  import { session, toast } from '../session.svelte'

  // 必须有密码（2026-11-14 用户定）：已经登录、还没设密码的账号先设密码，关不掉；不想设就退出登录。
  // 以后用密码登录，验证码邮件只在注册和忘记密码时发
  let pw1 = $state('')
  let pw2 = $state('')
  let busy = $state(false)
  let err = $state('')
  const ok = $derived([...pw1].length >= 8 && [...pw1].length <= 64 && pw1 === pw2)

  async function save() {
    busy = true
    err = ''
    try {
      session.me = await api.setPassword(pw1)
      toast.show('密码设好了，以后用邮箱和密码登录。')
    } catch (e) {
      err = (e as Error).message
    } finally {
      busy = false
    }
  }
</script>

<div class="mask">
  <div class="dlg" role="dialog" aria-modal="true" aria-label="设置登录密码">
    <div class="hd"><b>设置登录密码</b><span class="term">Password</span></div>
    <p class="lead">阵容广场改成用密码登录了：设一个密码，以后用邮箱 <b>{session.me?.emailMasked}</b> 和密码登录，不用再等验证码邮件。8～64 位，不能和邮箱一样。</p>
    <label class="f"><span>密码</span><input type="password" bind:value={pw1} autocomplete="new-password" maxlength="64" /></label>
    <label class="f">
      <span>再输一次</span>
      <input type="password" bind:value={pw2} autocomplete="new-password" maxlength="64" onkeydown={(e) => e.key === 'Enter' && ok && !busy && save()} />
    </label>
    {#if pw2 && pw1 !== pw2}<p class="err">两次输入的不一样。</p>{/if}
    {#if err}<p class="err">{err}</p>{/if}
    <div class="ft">
      <button class="btn ghost sm" disabled={busy} onclick={() => session.logout()}>先退出登录</button>
      <button class="btn primary sm" disabled={!ok || busy} onclick={save}>{busy ? '保存中…' : '保存'}</button>
    </div>
  </div>
</div>

<style>
  .mask {
    position: fixed;
    inset: 0;
    z-index: 95;
    display: grid;
    place-items: center;
    padding: 16px;
    background: rgb(0 0 0 / 0.65);
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
    display: flex;
    flex-direction: column;
    gap: 12px;
    padding: 18px 18px 16px;
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
    align-items: baseline;
    gap: 10px;
  }
  .hd b {
    font-size: 16px;
  }
  .hd .term {
    font-size: 11px;
    color: var(--mint);
  }
  .lead {
    margin: 0;
    font-size: 13px;
    line-height: 1.7;
    color: var(--fg-2);
  }
  .f {
    display: flex;
    flex-direction: column;
    gap: 6px;
  }
  .f span {
    font-size: 12px;
    color: var(--fg-3);
  }
  .err {
    margin: 0;
  }
  .ft {
    display: flex;
    justify-content: flex-end;
    gap: 8px;
    margin-top: 4px;
  }
</style>
