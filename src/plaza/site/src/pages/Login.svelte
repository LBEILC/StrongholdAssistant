<script lang="ts">
  import { api, type Me } from '../api'
  import { router, link } from '../router.svelte'
  import { session, toast } from '../session.svelte'
  import Turnstile from '../components/Turnstile.svelte'
  import Icon from '../components/Icon.svelte'

  // 登录（2026-11-14 改）：默认密码登录；「注册」用邮箱验证码建账号并必须设密码；「忘记密码」用邮箱验证码重设密码。
  // 验证码邮件每天能发的数量有限，只在注册和忘记密码时发。新账号最后起昵称
  let { next }: { next: string } = $props()
  // 在助手的登录小窗口里打开时（?app=1），登录成功后把凭据交给助手，昵称在助手里起
  const webview = (window as unknown as { chrome?: { webview?: { postMessage: (m: unknown) => void } } }).chrome?.webview
  const inApp = new URLSearchParams(location.search).get('app') === '1' && !!webview
  let handedOff = $state(false)
  // 必须有密码（2026-11-14 用户定）：验证码登录后还没密码的，先设密码才算登录完成，以后用密码登录、不再发邮件
  let pending: { token: string; me: Me } | null = null
  function done(r: { token: string; me: Me }) {
    session.me = r.me
    if (mode === 'reset' || !r.me.hasPassword) {
      pending = r
      step = 'password'
      return
    }
    finish(r)
  }
  function finish(r: { token: string; me: Me }) {
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

  let step = $state<'email' | 'code' | 'password' | 'nickname'>('email')
  let pw1 = $state('')
  let pw2 = $state('')
  const pwOk = $derived([...pw1].length >= 8 && [...pw1].length <= 64 && pw1 === pw2)
  let mode = $state<'password' | 'register' | 'reset'>('password')
  const switchMode = (m: typeof mode) => ((mode = m), (step = 'email'), (error = ''))
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
      // 发送按钮倒计时：后端说了等几秒就按它的；其他失败也等 60 秒（后端已经记了这一次，马上再点只会得到「太频繁」）
      const s = error.match(/(\d+)\s*秒/)
      countdown(s ? Number(s[1]) : 60)
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

  async function savePassword() {
    busy = true
    error = ''
    try {
      session.me = await api.setPassword(pw1)
      toast.show(mode === 'reset' ? '密码已重设，其他设备上的登录已退出。' : '密码设好了，以后用邮箱和密码登录。')
      finish({ ...pending!, me: session.me })
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
      <h1>{step === 'nickname' ? '起个昵称' : step === 'password' ? (mode === 'reset' ? '设置新密码' : '设置登录密码') : mode === 'reset' ? '忘记密码' : mode === 'register' ? '注册阵容广场' : '登录阵容广场'}</h1>
      <span class="term">{step === 'nickname' ? 'Nickname' : step === 'password' || mode === 'reset' ? 'Password' : mode === 'register' ? 'Sign up' : 'Sign in'}</span>
    </div>

    {#if handedOff}
      <p class="lead ok">登录成功，回到卫戍协议助手就好，这个窗口会自动关闭。</p>
    {:else if step === 'email'}
      {#if mode !== 'reset'}
        <div class="tabs">
          <button class:on={mode === 'password'} onclick={() => switchMode('password')}>登录</button>
          <button class:on={mode === 'register'} onclick={() => switchMode('register')}>注册</button>
        </div>
      {/if}
      {#if mode === 'register'}
        <p class="lead">用邮箱注册：我们发一封验证码邮件，验证后设一个密码，以后用邮箱和密码登录。登录后才能点赞；发布阵容在卫戍协议助手里操作。</p>
      {:else if mode === 'reset'}
        <p class="lead">我们往你的邮箱发一封验证码邮件，验证后设一个新密码。</p>
      {:else}
        <p class="lead">用邮箱和密码登录。以前只用验证码登录、没设过密码的，点下面「忘记密码」设一个。</p>
      {/if}
      <label class="f">
        <span>邮箱</span>
        <input type="email" bind:value={email} placeholder="name@example.com" autocomplete={mode === 'password' ? 'username' : 'email'} onkeydown={(e) => e.key === 'Enter' && mode !== 'password' && emailOk && token && !busy && !wait && send()} />
      </label>
      {#if mode === 'password'}
        <label class="f">
          <span>密码</span>
          <input type="password" bind:value={password} autocomplete="current-password" onkeydown={(e) => e.key === 'Enter' && emailOk && password && token && !busy && passwordLogin()} />
        </label>
      {/if}
      <Turnstile bind:this={ts} ontoken={(t) => (token = t)} />
      {#if mode === 'password'}
        <button class="btn primary full" disabled={!emailOk || !password || !token || busy} onclick={passwordLogin}>{busy ? '登录中…' : '登录'}</button>
        <div class="row">
          <button class="lnk" onclick={() => switchMode('reset')}>忘记密码</button>
          <button class="lnk" onclick={() => switchMode('register')}>还没有账号？注册</button>
        </div>
      {:else}
        <button class="btn primary full" disabled={!emailOk || !token || busy || wait > 0} onclick={send}><Icon name="mail" size={16} />{busy ? '发送中…' : wait ? `${wait} 秒后可以再发送` : '发送验证码'}</button>
        {#if mode === 'reset'}<div class="row"><button class="lnk" onclick={() => switchMode('password')}>返回登录</button></div>{/if}
      {/if}
    {:else if step === 'code'}
      <p class="lead">验证码已发到 <b>{email}</b>，10 分钟内有效。没收到的话看看垃圾箱，QQ 邮箱、163 邮箱有时会把它当成广告。</p>
      <label class="f">
        <span>6 位验证码</span>
        <input class="codein" inputmode="numeric" maxlength="6" bind:value={code} autocomplete="one-time-code" onkeydown={(e) => e.key === 'Enter' && code.trim().length === 6 && !busy && verify()} />
      </label>
      <button class="btn primary full" disabled={code.trim().length !== 6 || busy} onclick={verify}>{busy ? '验证中…' : '下一步'}</button>
      <div class="row">
        <button class="lnk" onclick={() => ((step = 'email'), (error = ''))}>换个邮箱</button>
        <span class="muted">{wait ? `${wait} 秒后可重新发送` : '没收到？回上一步重新发送'}</span>
      </div>
    {:else if step === 'password'}
      <p class="lead">{mode === 'reset' ? '设一个新密码，以后用它登录；其他设备上的登录会退出。' : '以后用邮箱和这个密码登录，不用再等验证码邮件。'}8～64 位，不能和邮箱一样。</p>
      <input type="email" value={email.trim() || session.me?.emailMasked} autocomplete="username" hidden readonly />
      <label class="f">
        <span>密码</span>
        <input type="password" bind:value={pw1} autocomplete="new-password" maxlength="64" />
      </label>
      <label class="f">
        <span>再输一次</span>
        <input type="password" bind:value={pw2} autocomplete="new-password" maxlength="64" onkeydown={(e) => e.key === 'Enter' && pwOk && !busy && savePassword()} />
      </label>
      {#if pw2 && pw1 !== pw2}<p class="err">两次输入的不一样。</p>{/if}
      <button class="btn primary full" disabled={!pwOk || busy} onclick={savePassword}>{busy ? '保存中…' : '保存并登录'}</button>
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
