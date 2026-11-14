<script lang="ts">
  import Icon from './Icon.svelte'
  import { LATEST, LINKS, dot } from '../data'
  import { reveal } from '../lib'

  // 下载：主推 Mirror酱（付费高速），另给 QQ 群文件和 GitHub 两个免费入口。官网服务器本身不放安装包
  let copied = $state(false)
  async function copyGroup() {
    await navigator.clipboard.writeText(LINKS.qqGroup)
    copied = true
    setTimeout(() => (copied = false), 1600)
  }
</script>

<div class="dl">
  <div class="ver" use:reveal>
    <span class="lbl">Current build</span>
    <b class="num">v{LATEST.version}</b>
    <span class="meta"><Icon name="windows" size={14} />Windows 10 / 11 · x64</span>
    <span class="meta lbl">Released {dot(LATEST.date)}</span>
  </div>

  <div class="cards">
    <a class="card main hud" href={LINKS.mirror} target="_blank" rel="noopener" use:reveal>
      <div class="ct">
        <span class="badge">推荐 · 国内高速</span>
        <span class="lbl">Mirror酱 · CDN</span>
      </div>
      <h3><Icon name="flash" size={26} />Mirror酱 高速下载</h3>
      <p>国内线路下载快，填进助手设置后，以后的自动更新也走高速。需要购买 Mirror酱 的 CDK，同一个 CDK 还能用在 MAA 等其他接入 Mirror酱 的软件上。</p>
      <span class="btn primary">前往 Mirror酱 下载<Icon name="arrowUp" size={16} /></span>
    </a>

    <div class="card" use:reveal={80}>
      <div class="ct">
        <span class="badge free">免费</span>
        <span class="lbl">QQ Group</span>
      </div>
      <h3><Icon name="qq" size={24} />QQ 群文件</h3>
      <p>加群后在 <b>群文件</b> 下载，最新版都会传上去。有问题也可以直接在群里问。</p>
      <div class="row">
        <a class="btn" href={LINKS.qq} target="_blank" rel="noopener">加入 QQ 群<Icon name="arrowUp" size={16} /></a>
        <button class="btn ghost num" onclick={copyGroup} title="复制群号">{#if copied}<Icon name="check" size={16} />已复制{:else}<Icon name="copy" size={16} />{LINKS.qqGroup}{/if}</button>
      </div>
    </div>

    <div class="card" use:reveal={160}>
      <div class="ct">
        <span class="badge free">免费</span>
        <span class="lbl">GitHub Releases</span>
      </div>
      <h3><Icon name="github" size={24} />GitHub Releases</h3>
      <p>开源仓库的发布页，每一版的安装包和说明都在这里。国内访问可能比较慢。</p>
      <a class="btn" href={LINKS.releases} target="_blank" rel="noopener">前往 GitHub 下载<Icon name="arrowUp" size={16} /></a>
    </div>
  </div>

  <div class="files" use:reveal>
    <div>
      <span class="lbl">Which file</span>
      <p><b>推荐安装包</b> <code>StrongholdAssistant-v{LATEST.version}-setup.exe</code>：不要管理员权限，会建快捷方式。不想安装就下 <code>…-win-x64.zip</code>，解压到单独的文件夹再运行。</p>
    </div>
    <div>
      <span class="lbl">SmartScreen</span>
      <p>程序还没有代码签名，Windows 可能提示「已保护你的电脑」，点 <b>更多信息 → 仍要运行</b>。不需要另外装 .NET。</p>
    </div>
  </div>
</div>

<style>
  .ver {
    display: flex;
    flex-wrap: wrap;
    align-items: baseline;
    gap: 8px 20px;
    margin-bottom: 28px;
  }
  .ver b {
    font-size: var(--d-num);
    font-weight: 600;
    line-height: 1;
    color: var(--fg-0);
    text-shadow: 0 0 30px rgb(var(--mint-rgb) / 0.25);
  }
  .meta {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    font-size: 14px;
    color: var(--fg-2);
  }
  .cards {
    display: grid;
    grid-template-columns: 1.35fr 1fr 1fr;
    gap: 20px;
  }
  .card {
    display: flex;
    flex-direction: column;
    gap: 14px;
    padding: 28px;
    background: linear-gradient(180deg, var(--bg-2), var(--bg-1));
    border: 1px solid var(--line);
    transition:
      border-color var(--t-mid),
      transform var(--t-mid) var(--ease-out);
  }
  .card:hover {
    border-color: var(--line-hi);
  }
  .main {
    background:
      radial-gradient(500px 240px at 0% 0%, rgb(var(--mint-rgb) / 0.14), transparent 70%),
      linear-gradient(180deg, var(--bg-2a), var(--bg-1));
    border-color: rgb(var(--mint-rgb) / 0.55);
    box-shadow: 0 0 0 1px rgb(var(--mint-rgb) / 0.15), 0 20px 60px rgb(var(--mint-rgb) / 0.08);
  }
  .main:hover {
    border-color: var(--mint);
    transform: translateY(-2px);
  }
  .ct {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 8px;
  }
  .badge {
    padding: 2px 8px;
    font-size: 12px;
    font-weight: 600;
    color: var(--mint-ink);
    background: var(--mint);
    border-radius: var(--r-1);
  }
  .badge.free {
    color: var(--fg-1);
    background: var(--bg-4);
  }
  h3 {
    display: flex;
    align-items: center;
    gap: 10px;
    font-size: 22px;
    font-weight: 700;
  }
  .main h3 {
    font-size: 26px;
    color: var(--fg-0);
  }
  .main h3 :global(.ri) {
    color: var(--mint);
  }
  .card p {
    flex: 1;
    font-size: 14px;
    color: var(--fg-2);
  }
  .card .btn {
    align-self: flex-start;
  }
  .main .btn {
    height: 48px;
    font-size: 16px;
  }
  .row {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
  }
  .files {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 20px 40px;
    margin-top: 28px;
    padding-top: 24px;
    border-top: 1px dashed var(--line);
  }
  .files p {
    margin-top: 6px;
    font-size: 14px;
    color: var(--fg-2);
  }
  @media (max-width: 980px) {
    .cards {
      grid-template-columns: 1fr 1fr;
    }
    .main {
      grid-column: 1 / -1;
    }
  }
  @media (max-width: 640px) {
    .cards,
    .files {
      grid-template-columns: 1fr;
    }
    .card {
      padding: 22px;
    }
  }
</style>
