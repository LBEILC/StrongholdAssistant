<script lang="ts">
  import { fly, fade } from 'svelte/transition'
  import { cubicOut } from 'svelte/easing'
  import { control } from '../lib/control.svelte'
  import Icon from './Icon.svelte'

  // 开启自动操作前的风险说明（docs/自动操作.md「风险提示与开启」，用户 2026-11-19 定：照做、给提示、后果自负）。
  // 单独一页，勾选后「开启」才能点；关掉或点「先不开」什么也不改
  let agreed = $state(false)
  const close = () => ((control.riskOpen = false), (agreed = false))
</script>

<svelte:window onkeydown={(e) => control.riskOpen && e.key === 'Escape' && close()} />

{#if control.riskOpen}
  <div class="scrim" transition:fade={{ duration: 200 }} onclick={close} role="presentation"></div>
  <div class="wrap">
    <div class="card" role="dialog" aria-modal="true" aria-label="开启自动操作前请看" in:fly={{ y: 24, duration: 280, easing: cubicOut }} out:fade={{ duration: 150 }}>
      <header>
        <span class="term tag">Auto Control</span>
        <h3>开启自动操作前请看完</h3>
        <p>打开后，助手可以照推荐、或照外部程序（agent）的指令，替你在游戏里点击、拖动、按键。</p>
      </header>
      <div class="body">
        <section class="risk">
          <Icon name="warn" size={18} />
          <div>
            <b>可能导致账号被限制或封禁</b>
            <p>用脚本或程序操作游戏可能违反游戏的用户协议。助手只看截图、模拟点击，不注入、不读游戏内存，也不做任何躲避检测的处理，但这不代表不会被发现或处罚。</p>
          </div>
        </section>
        <section>
          <b>可能点错、拖错</b>
          <p>识别认错人、画面被挡、网络卡顿都可能让操作出错，造成对局失利。每一步做之前和做完都会核对画面，对不上就停下，但不能保证不出错。</p>
        </section>
        <section>
          <b>电脑版会占用鼠标和键盘</b>
          <p>电脑客户端只认真实的鼠标和键盘，执行时助手会把游戏切到前台、接管鼠标；这时别动鼠标键盘，一动就会停下。模拟器（MuMu、雷电）走 ADB，不占鼠标，可以在后台跑。</p>
        </section>
        <section>
          <b>随时可以停</b>
          <p>按停止键（默认 Ctrl+Alt+Q）、点执行浮条上的「停止」，或在这里关掉自动操作，都会马上停。建议先用「试运行」看看会点哪里，再用「逐步确认」，熟悉了再用「自动」。</p>
        </section>
        <label class="agree">
          <input type="checkbox" bind:checked={agreed} />
          <span>我已了解上述风险，使用自动操作的后果由我自己承担</span>
        </label>
      </div>
      <footer>
        <button class="ghost" onclick={close}>先不开</button>
        <button class="primary" disabled={!agreed} onclick={() => ((agreed = false), control.agree())}>开启自动操作</button>
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
    pointer-events: none;
  }
  .card {
    pointer-events: auto;
    position: relative;
    width: 100%;
    max-width: 440px;
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
    background: var(--red);
  }
  header {
    padding: 14px 14px 8px;
  }
  .tag {
    font-size: var(--fs-2xs);
    padding: 2px 5px 1px;
    color: #fff;
    background: var(--red);
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
  .body {
    min-height: 0;
    overflow-y: auto;
    display: flex;
    flex-direction: column;
    gap: 10px;
    padding: 6px 14px 12px;
  }
  section b {
    font-size: var(--fs-md);
    color: var(--fg-1);
  }
  section p {
    margin: 3px 0 0;
    font-size: var(--fs-sm);
    line-height: 1.65;
    color: var(--fg-3);
  }
  .risk {
    display: flex;
    gap: 8px;
    padding: 9px 10px;
    background: var(--red-tint);
    box-shadow: inset 2px 0 0 var(--red);
    color: var(--red-fg-hi);
  }
  .risk b {
    color: var(--red-fg-hi);
  }
  .agree {
    display: flex;
    align-items: flex-start;
    gap: 8px;
    margin-top: 2px;
    padding: 9px 10px;
    background: var(--bg-1);
    box-shadow: inset 0 0 0 1px var(--line);
    font-size: var(--fs-base);
    color: var(--fg-1);
    cursor: pointer;
  }
  .agree input {
    margin: 2px 0 0;
    accent-color: var(--red);
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
    font-weight: var(--fw-bold);
    color: #fff;
    background: var(--red);
  }
  .primary:disabled {
    opacity: 0.4;
  }
</style>
