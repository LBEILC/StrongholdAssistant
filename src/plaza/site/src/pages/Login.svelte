<script lang="ts">
  import { api, type Me } from '../api'
  import { router, link } from '../router.svelte'
  import { session, toast } from '../session.svelte'
  import Turnstile from '../components/Turnstile.svelte'
  import Icon from '../components/Icon.svelte'

  // 邮箱验证码登录：填邮箱 → 过人机验证 → 发码 → 填码。新账号接着起昵称
  let { next }: { next: string } = $props()
  // 在助手的登录小窗口里打开时（?app=1），登录成功后把凭据交给助手，昵称在助手里起
  const webview = (window as unknown as { chrome?: { webview?: { postMessage: (m: unknown) => void } } }).chrome?.webview
  const inApp = new URLSearchParams(location.search).get('app') === '1' && !!webview
  let handedOff = $state(false)
  function done(r: { token: string; me: Me }) {
    session.me = r.me
    if (inApp) {
      webview!.postMessage({ type: 'plaza-login', token: r.token })
      handedOff = true
      return
    }
    if (r.me.nickname) {
      toast.show(`欢迎回来，${r.me.nickname}。`)
      router.go(target)
    } else step = 'nickname'
  }
  const target = $derived(next.startsWith('/') ? next : '/me')

  let step = $state<'email' | 'code' | 'nickname'>('email')
  let mode = $state<'code' | 'password'>('code')
  let password = $state('')
  let email = $state('')
  let code = $state('')
  let nickname = $state('')
  let token = $state<string | null>(null)
  let busy = $state(false)
  let error = $state('')
  let wait = $state(0)
  let ts = $state<ReturnType<typeof Turnstile>>()
  let timer = 0

  const emailOk = $derived(/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim()))

  function countdown(s: number) {
    wait = s
    clearInterval(timer)
    timer = window.setInterval(() => (wait = Math.max(0, wait - 1)) || clearInterval(timer), 1000)
  }
  $effect(() => () => clearInterval(timer))

  async function send() {
    busy = true
    error = ''
    try {
      const r = await api.sendCode(email.trim(), token!)
      step = 'code'
      code = ''
      countdown(r.retryAfter)
    } catch (e) {
      error = (e as Error).message
    } finally {
      busy = false
      ts?.reset()
    }
  }

  async function passwordLogin() {
    busy = true
    error = ''
    try {
      done(await api.passwordLogin(email.trim(), password, token!))
    } catch (e) {
      error = (e as Error).message
    } finally {
      busy = false
      ts?.reset()
    }
  }

  async function verify() {
    busy = true
    error = ''
    try {
      done(await api.verify(email.trim(), code.trim()))
    } catch (e) {
      error = (e as Error).message
    } finally {
      busy = false
    }
  }

  async function saveNickname() {
    busy = true
    error = ''
    try {
      session.me = await api.setNickname(nickname.trim())
      toast.show('昵称设好了。')
      router.go(target)
    } catch (e) {
      error = (e as Error).message
    } finally {
      busy = false
    }
  }
</script>

<div class="wrap">
  <div class="card">
    <div class="ch">
      <h1>{step === 'nickname' ? '起个昵称' : '登录阵容广场'}</h1>
      <span class="term">{step === 'nickname' ? 'Nickname' : 'Sign in'}</span>
    </div>

    {#if handedOff}
      <p class="lead ok">登录成功，回到卫戍协议助手就好，这个窗口会自动关闭。</p>
    {:else if step === 'email'}
      <div class="tabs">
        <button class:on={mode === 'code'} onclick={() => ((mode = 'code'), (error = ''))}>验证码登录</button>
        <button class:on={mode === 'password'} onclick={() => ((mode = 'password'), (error = ''))}>密码登录</button>
      </div>
      {#if mode === 'code'}
        <p class="lead">第一次登录会自动建账号，不用先注册。登录后才能点赞；发布阵容在卫戍协议助手里操作。</p>
      {:else}
        <p class="lead">登录后在「我的」里设过密码才能用。忘了密码就用验证码登录，再到「我的」里重设。</p>
      {/if}
      <label class="f">
        <span>邮箱</span>
        <input type="email" bind:value={email} placeholder="name@example.com" autocomplete={mode === 'password' ? 'username' : 'email'} onkeydown={(e) => e.key === 'Enter' && mode === 'code' && emailOk && token && !busy && send()} />
      </label>
      {#if mode === 'password'}
        <label class="f">
          <span>密码</span>
          <input type="password" bind:value={password} autocomplete="current-password" onkeydown={(e) => e.key === 'Enter' && emailOk && password && token && !busy && passwordLogin()} />
        </label>
      {/if}
      <Turnstile bind:this={ts} ontoken={(t) => (token = t)} />
      {#if mode === 'code'}
        <button class="btn primary full" disabled={!emailOk || !token || busy} onclick={send}><Icon name="mail" size={16} />{busy ? '发送中…' : '发送验证码'}</button>
      {:else}
        <button class="btn primary full" disabled={!emailOk || !password || !token || busy} onclick={passwordLogin}>{busy ? '登录中…' : '登录'}</button>
      {/if}
    {:else if step === 'code'}
      <p class="lead">验证码已发到 <b>{email}</b>，10 分钟内有效。没收到的话看看垃圾箱，QQ 邮箱、163 邮箱有时会把它当成广告。</p>
      <label class="f">
        <span>6 位验证码</span>
        <input class="codein" inputmode="numeric" maxlength="6" bind:value={code} autocomplete="one-time-code" onkeydown={(e) => e.key === 'Enter' && code.trim().length === 6 && !busy && verify()} />
      </label>
      <button class="btn primary full" disabled={code.trim().length !== 6 || busy} onclick={verify}>{busy ? '登录中…' : '登录'}</button>
      <div class="row">
        <button class="lnk" onclick={() => ((step = 'email'), (error = ''))}>换个邮箱</button>
        <span class="muted">{wait ? `${wait} 秒后可重新发送` : '没收到？回上一步重新发送'}</span>
      </div>
    {:else}
      <p class="lead">昵称会显示在你发布的阵容上，2～16 个字，全站不能重名，以后可以改。</p>
      <label class="f">
        <span>昵称</span>
        <input bind:value={nickname} maxlength="16" onkeydown={(e) => e.key === 'Enter' && [...nickname.trim()].length >= 2 && !busy && saveNickname()} />
      </label>
      <button class="btn primary full" disabled={[...nickname.trim()].length < 2 || busy} onclick={saveNickname}>{busy ? '保存中…' : '保存'}</button>
    {/if}

    {#if error}<p class="err">{error}</p>{/if}
    <p class="foot">登录即表示你同意按 <a href="/privacy" use:link>隐私说明</a> 处理你的邮箱。</p>
  </div>
</div>

<style>
  .wrap {
    display: flex;
    justify-content: center;
    padding: 56px 16px;
  }
  .card {
    width: min(420px, 100%);
    display: flex;
    flex-direction: column;
    gap: 14px;
    padding: 24px;
    background: linear-gradient(180deg, var(--bg-2), var(--bg-1));
    box-shadow: inset 0 0 0 1px var(--line-soft);
    position: relative;
  }
  .card::before {
    content: '';
    position: absolute;
    left: 0;
    top: 0;
    width: 56px;
    height: 2px;
    background: var(--mint);
    box-shadow: var(--mint-glow);
  }
  .tabs {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 2px;
  }
  .tabs button {
    height: 32px;
    color: var(--fg-3);
    background: var(--bg-1);
    box-shadow: inset 0 -2px 0 var(--line);
  }
  .tabs button.on {
    color: var(--fg-1);
    font-weight: 700;
    box-shadow: inset 0 -2px 0 var(--mint);
  }
  .ch {
    display: flex;
    align-items: baseline;
    gap: 10px;
  }
  h1 {
    margin: 0;
    font-size: 20px;
  }
  .ch .term {
    font-size: 11px;
    color: var(--mint);
  }
  .lead {
    margin: 0;
    color: var(--fg-2);
    line-height: 1.7;
    font-size: 13px;
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
  .codein {
    font-family: var(--font-term);
    font-size: 22px;
    letter-spacing: 0.4em;
    text-align: center;
  }
  .full {
    width: 100%;
    height: 40px;
  }
  .row {
    display: flex;
    justify-content: space-between;
    font-size: 12px;
  }
  .lnk {
    color: var(--mint);
  }
  .err {
    margin: 0;
  }
  .ok {
    color: var(--mint);
  }
  .foot {
    margin: 4px 0 0;
    font-size: 11.5px;
    color: var(--fg-4);
  }
  .foot a {
    color: var(--fg-3);
    text-decoration: underline;
  }
</style>
