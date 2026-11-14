<script lang="ts">
  import Icon from '../components/Icon.svelte'
  import PageHead from '../components/PageHead.svelte'
  import { LINKS } from '../data'
  import { reveal } from '../lib'

  // 内容和 README、ASSETS.md、NOTICE 一致；改那边时这里跟着改
  const CREDITS = [
    { name: 'MaaFramework', what: '图像识别', href: 'https://github.com/MaaXYZ/MaaFramework' },
    { name: 'PRTS', what: '游戏数据', href: 'https://prts.wiki' },
    { name: 'SPDatabase', what: '游戏数据', href: '' },
    { name: 'ArknightsAssets', what: '界面素材', href: 'https://github.com/ArknightsAssets/ArknightsAssets2' },
    { name: 'ArknightsGameData', what: '关卡与活动数据', href: 'https://github.com/Kengxxiao/ArknightsGameData' },
    { name: 'PaddleOCR', what: '文字识别模型', href: 'https://github.com/PaddlePaddle/PaddleOCR' },
    { name: 'Remix Icon', what: '通用图标', href: 'https://remixicon.com' },
    { name: 'MiSans', what: '字体', href: 'https://hyperos.mi.com/font' },
  ]
</script>

<PageHead en="About" title="开源与素材">卫戍协议助手是免费的玩家自制工具，代码开源。这里写清楚它从哪来、用了谁的东西、会碰你的哪些数据。</PageHead>

<div class="wrap body">
  <section id="license" use:reveal>
    <h2><span class="lbl"><b class="mint">01</b> · License</span>开源许可</h2>
    <div class="prose">
      <p>代码以 <b>Apache-2.0</b> 开源，仓库在 <a href={LINKS.repo} target="_blank" rel="noopener">GitHub · LBEILC/StrongholdAssistant</a>。欢迎提 Issue 和 PR。</p>
      <p>
        本项目基于 <a href="https://github.com/bingyunhe/asp-assistant-arknights" target="_blank" rel="noopener">bingyunhe/asp-assistant-arknights</a> 改造，它又基于
        <a href="https://github.com/Moonfair/ASP_Assistant" target="_blank" rel="noopener">Moonfair/ASP_Assistant</a>。识别、数据和阵容规划沿用上游，本项目重做了界面与交互。阵容规划由
        <a href="https://github.com/CarlosDJY" target="_blank" rel="noopener">CarlosDJY</a> 贡献给上游；MuMu 模拟器支持参考了
        <a href="https://github.com/hesperine" target="_blank" rel="noopener">hesperine</a> 给上游的 PR。上游的许可与声明原样保留在 <a href={LINKS.notice} target="_blank" rel="noopener">NOTICE</a>。
      </p>
    </div>
    <div class="links">
      <a class="btn sm" href={LINKS.repo} target="_blank" rel="noopener"><Icon name="github" size={16} />源代码</a>
      <a class="btn sm ghost" href={LINKS.license} target="_blank" rel="noopener"><Icon name="scales" size={16} />LICENSE</a>
      <a class="btn sm ghost" href={LINKS.notice} target="_blank" rel="noopener">NOTICE</a>
    </div>
  </section>

  <section id="credits" use:reveal>
    <h2><span class="lbl"><b class="mint">02</b> · Credits</span>鸣谢</h2>
    <ul class="credits">
      {#each CREDITS as c (c.name)}
        <li>
          {#if c.href}<a href={c.href} target="_blank" rel="noopener">{c.name}<Icon name="arrowUp" size={13} /></a>{:else}<span>{c.name}</span>{/if}
          <span class="dim">{c.what}</span>
        </li>
      {/each}
    </ul>
  </section>

  <section id="assets" use:reveal>
    <h2><span class="lbl"><b class="mint">03</b> · Assets</span>素材归属</h2>
    <div class="prose">
      <p>本项目和本站使用的《明日方舟》游戏美术与数据（盟约图标、商店卡框、干员头像、游戏截图等），著作权归 <b>上海鹰角网络科技有限公司（Hypergryph）</b> 所有。本项目是免费的玩家自制工具，与鹰角网络无关，未获其认可或授权。权利方提出要求时，我们会移除相关素材。</p>
      <p>Apache-2.0 只覆盖本项目的代码，不覆盖上面这些素材。字体、图标等第三方素材遵循各自的许可：MiSans 按《MiSans 字体知识产权许可协议》使用，Remix Icon 为 Apache-2.0。每一项素材的具体来源见 <a href={LINKS.assets} target="_blank" rel="noopener">ASSETS.md</a>。</p>
    </div>
  </section>

  <section id="privacy" use:reveal>
    <h2><span class="lbl"><b class="mint">04</b> · Privacy</span>数据与隐私</h2>
    <div class="prose">
      <p>助手只通过截屏识别游戏画面：<b>不读游戏内存，不注入游戏，不向游戏发送点击或按键</b>。</p>
      <p>阵容、设置、追踪状态和历史记录默认只存在本机 <code>%AppData%\StrongholdAssistant</code>。默认联网的只有两件事：启动时检查一次新版本（问 GitHub；更新源选了 Mirror酱 就问 Mirror酱，填了 CDK 会一起发过去；设置里可以关），以及下载一次公开的资金表。</p>
    </div>
    <ul class="opt">
      <li><b>阵容广场</b><span>浏览和导入不用账号；登录后发布的阵容、简介和昵称公开显示，邮箱只用于登录、不公开。</span></li>
      <li><b>云同步</b><span>我的阵容和几项偏好存到你的广场账号，只有你自己看得到。默认关。</span></li>
      <li><b>补全资金表</b><span>每局结束匿名上报各回合的起始资金，只带一个可重置的本机随机编号。</span></li>
      <li><b>森空岛</b><span>打开森空岛官方页面读攻略和战绩，助手不读写你的森空岛凭据。</span></li>
      <li><b>诊断包</b><span>只含日志、识别失败时的游戏画面截图和版本信息，不含账号和凭据，你自己决定发不发。</span></li>
    </ul>
    <p class="note dim">以上除了检查更新和资金表，都要你自己在助手里打开。</p>
  </section>
</div>

<style>
  section {
    max-width: 860px;
  }
  .body {
    padding-top: 56px;
    padding-bottom: 40px;
    display: flex;
    flex-direction: column;
    gap: 72px;
  }
  h2 {
    display: flex;
    flex-direction: column;
    gap: 4px;
    margin-bottom: 20px;
    font-size: var(--d-2);
  }
  .prose {
    display: flex;
    flex-direction: column;
    gap: 14px;
    color: var(--fg-2);
  }
  .prose b {
    color: var(--fg-1);
  }
  .links {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
    margin-top: 22px;
  }
  .credits {
    list-style: none;
    margin: 0;
    padding: 0;
    display: grid;
    grid-template-columns: repeat(2, 1fr);
    border-top: 1px solid var(--line);
  }
  .credits li {
    display: flex;
    justify-content: space-between;
    gap: 12px;
    padding: 14px 4px;
    border-bottom: 1px solid var(--line);
  }
  .credits li:nth-child(odd) {
    padding-right: 24px;
  }
  .credits li:nth-child(even) {
    padding-left: 24px;
    border-left: 1px solid var(--line);
  }
  .credits a {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    font-weight: 600;
  }
  .credits a:hover {
    color: var(--mint);
  }
  .opt {
    list-style: none;
    margin: 22px 0 0;
    padding: 0;
    border-top: 1px solid var(--line);
  }
  .opt li {
    display: grid;
    grid-template-columns: 120px 1fr;
    gap: 16px;
    padding: 14px 0;
    border-bottom: 1px solid var(--line);
    font-size: 14px;
  }
  .opt span {
    color: var(--fg-2);
  }
  .note {
    margin-top: 14px;
    font-size: 13px;
  }
  @media (max-width: 640px) {
    .credits {
      grid-template-columns: 1fr;
    }
    .credits li:nth-child(n) {
      padding: 12px 4px;
      border-left: 0;
    }
    .opt li {
      grid-template-columns: 1fr;
      gap: 4px;
    }
  }
</style>
