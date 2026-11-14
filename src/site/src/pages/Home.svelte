<script lang="ts">
  import Icon, { type IconName } from '../components/Icon.svelte'
  import SectionHead from '../components/SectionHead.svelte'
  import ShopStage from '../components/ShopStage.svelte'
  import DemoDialog from '../components/DemoDialog.svelte'
  import Downloads from '../components/Downloads.svelte'
  import ReleaseList from '../components/ReleaseList.svelte'
  import FaqList from '../components/FaqList.svelte'
  import Bond from '../components/Bond.svelte'
  import { FAQ } from '../faq'
  import { FIRST, LATEST, LINKS, RELEASES, STATS, dot } from '../data'
  import { reveal } from '../lib'
  import match from '../assets/shots/match.webp'
  import target from '../assets/shots/target.webp'
  import plan from '../assets/shots/plan.webp'
  import codex from '../assets/shots/codex.webp'
  import operator from '../assets/shots/operator.webp'
  import plaza from '../assets/shots/plaza.webp'
  import ursus from '../assets/shots/skin-ursus.webp'
  import closure from '../assets/shots/skin-closure.webp'

  let demo = $state(false)

  // ---- 盟约排：每隔一会儿随机点亮几个，像局里盟约在激活 ----
  const bonds = STATS.bonds.filter((b) => b.id !== 'empty')
  let lit = $state(new Set(['sargon', 'siracusa', 'iberia', 'yan']))
  $effect(() => {
    if (matchMedia('(prefers-reduced-motion: reduce)').matches) return
    const t = setInterval(() => {
      const next = new Set<string>()
      while (next.size < 4) next.add(bonds[Math.floor(Math.random() * bonds.length)].id)
      lit = next
    }, 2400)
    return () => clearInterval(t)
  })

  // ---- 功能频道：左边节目单，右边画面；在视口里时每 8 秒换台，点过就停 ----
  type Feature = { id: string; en: string; title: string; tag: string; desc: string; points: string[] }
  const FEATURES: Feature[] = [
    {
      id: 'match',
      en: 'Match',
      tag: '本局',
      title: '开局信息，一进来就录好',
      desc: '停在「确认本局信息」页，侧栏自动截图识别：哪些盟约阵容不完整、禁用了谁、敌方领袖和特训敌人。没认出的格子按游戏顺序推测，录错了右键直接改。',
      points: ['直接认头像，换了皮肤、精二的也认得', '商店里顺带读回合和资金，算出下回合大约多少钱', '关注的盟约和干员被禁时，本局页顶部醒目提示'],
    },
    {
      id: 'lineup',
      en: 'Lineup',
      tag: '阵容',
      title: '定好目标，按回合阶段追踪',
      desc: '目标盟约、想抓的干员、想要的装备、配合的策略，一套阵容全写清。照攻略加「1～3 回合」「过渡」这样的阶段，读到回合自动切换，要买的都进顶部的追踪列表。',
      points: ['干员池按目标盟约推荐，被禁的自动变暗', '分享码一句话发到群里，别人粘贴就能导入', '能导入森空岛攻略，或用自己的战绩生成阵容'],
    },
    {
      id: 'shop',
      en: 'Shop',
      tag: '商店',
      title: '要买的卡，在游戏里直接描框',
      desc: '游戏商店里给整张卡描框：关键干员是金色「关键」，追踪中的是薄荷绿「追踪」，目标盟约里其他还能抓的是虚线「可抓」。刷新商店时标记立刻撤掉，认出新商店再标。',
      points: ['只看画面，不点击、不改游戏', '16:9、16:10、2K、4K 和三套游戏主题都认得', '刷新键可以改成你习惯的键'],
    },
    {
      id: 'codex',
      en: 'Archive',
      tag: '图鉴',
      title: '干员、装备、策略、首领一处查',
      desc: '仿商店卡的网格，按等阶、盟约、追踪中筛选。点开干员，普通和精锐特性同屏对照、变化的数字高亮；装备写明能合成什么、哪些干员会带来它；首领各难度血量都有。',
      points: ['可以隐藏本局被禁的干员', '给干员设「我的默认」技能和模组', '本局用到的策略和首领排在最前'],
    },
    {
      id: 'plaza',
      en: 'Plaza',
      tag: '广场',
      title: '看看大家怎么打',
      desc: '阵容广场汇集大家分享的阵容，在助手里默认按本局能不能用排序，一键导入。网站上也能浏览和新建，点「在助手里打开」直接唤起助手。',
      points: ['浏览和导入不用登录', '点赞、收藏、评论，好阵容会被设为精选', '登录后可以云同步自己的阵容和偏好'],
    },
  ]
  let cur = $state(0)
  let auto = $state(true)
  let tick = $state(0)
  let guideEl: HTMLElement
  let visible = $state(false)
  $effect(() => {
    const io = new IntersectionObserver(([e]) => (visible = e.isIntersecting), { threshold: 0.35 })
    io.observe(guideEl)
    return () => io.disconnect()
  })
  $effect(() => {
    if (!auto || !visible) return
    tick = 0
    const t = setInterval(() => {
      tick += 1
      if (tick >= 80) {
        cur = (cur + 1) % FEATURES.length
        tick = 0
      }
    }, 100)
    return () => clearInterval(t)
  })
  function pick(i: number) {
    cur = i
    auto = false
  }

  const MORE: { icon: IconName; title: string; en: string; text: string }[] = [
    { icon: 'history', title: '历史战绩', en: 'Record', text: '每局自动记下开局信息、目标和每回合资金；按联机 / 单人看通关率、平均到达回合和常用干员。' },
    { icon: 'masonry', title: '工作台', en: 'Workbench', text: '一键展开成宽窗口，左边导航右边详情，编阵容、逛广场、翻战绩更顺手；收回就贴回游戏旁边。' },
    { icon: 'route', title: '敌人路线', en: 'Routes', text: '在棋盘上画出敌人会怎么走，按组合键显示，特训敌人可能出现的也一起标出。' },
    { icon: 'keyboard', title: '一键收起', en: 'Hotkey', text: '默认 Alt+S 收起和展开侧栏，游戏在前台也能按；也能设成单个键。' },
    { icon: 'cloud', title: '云同步', en: 'Sync', text: '登录广场后同步我的阵容和偏好，换电脑、在网站上都能接着用。默认关闭。' },
    { icon: 'shield', title: '只看画面', en: 'Screen only', text: '只通过截屏识别：不读游戏内存，不注入游戏，不向游戏发送点击或按键。' },
  ]

  const homeFaq = FAQ.flatMap((g) => g.items).filter((q) => q.home)
  const totalChanges = RELEASES.reduce((n, r) => n + r.items.length, 0)
</script>

<!-- ============ 首屏 ============ -->
<section class="hero">
  <div class="grid-bg" aria-hidden="true"></div>
  <div class="wrap hero-in">
    <div class="copy">
      <span class="lbl eyebrow"><i class="sq"></i>Rhodes Island <span class="mint">//</span> Stronghold Protocol</span>
      <h1>卫戍协议助手</h1>
      <span class="en" aria-hidden="true">Stronghold Assistant</span>
      <div class="bars" aria-hidden="true"><i></i><i></i><i></i></div>
      <p class="lead">贴在游戏窗口旁边的《明日方舟》卫戍协议侧栏，陪你打完一整局。</p>
      <ol class="flow">
        <li><span class="num">01</span>开局自动录入盟约和禁用</li>
        <li><span class="num">02</span>定好目标阵容</li>
        <li><span class="num">03</span>商店里给要买的卡描框</li>
        <li><span class="num">04</span>打完自动记进历史</li>
      </ol>
      <div class="cta">
        <a class="btn primary big" href="#download"><Icon name="download" size={20} />免费下载<span class="sub">v{LATEST.version}</span></a>
        <button class="btn big" onclick={() => (demo = true)}><Icon name="play" size={20} />在网页里试一试</button>
      </div>
      <div class="meta">
        <span><span class="lbl">Platform</span>Windows 10 / 11</span>
        <span><span class="lbl">Source</span>PC · MuMu · 雷电</span>
        <span><span class="lbl">License</span>免费 · 开源</span>
      </div>
    </div>

    <div class="visual" aria-label="助手侧栏贴在游戏窗口旁边">
      <div class="game hud">
        <ShopStage />
        <div class="cap"><span class="live-dot"></span><span class="lbl">Shop phase · 追踪 3 张卡</span></div>
      </div>
      <img class="side shot" src={match} alt="助手侧栏：本局页" width="380" height="860" />
      <span class="tagline lbl"><span class="mint">◆</span> Docked · 贴在游戏右侧</span>
    </div>
  </div>

  <div class="wrap">
    <div class="bondrow" aria-hidden="true">
      {#each bonds as b (b.id)}<Bond id={b.id} size={30} on={lit.has(b.id)} />{/each}
    </div>
  </div>
</section>

<!-- ============ 数字 ============ -->
<section class="stats">
  <div class="wrap st-in">
    {#each [[STATS.operators, '名干员', 'Operators'], [STATS.equipment, '件装备', 'Equipment'], [STATS.covenants, '个盟约', 'Covenants'], [STATS.strategies, '条策略', 'Strategies'], [RELEASES.length, '次更新', 'Releases']] as [n, cn, en], i (en)}
      <div class="st" use:reveal={i * 70}>
        <span class="lbl">{en}</span>
        <b class="num">{n}</b>
        <span class="cn">{cn}</span>
      </div>
    {/each}
  </div>
  <p class="wrap st-note lbl">卫戍协议 · 规程 · 序列 I 当期数据 · 自 {dot(FIRST.date)} 起共 {totalChanges} 项改动</p>
</section>

<!-- ============ 功能 ============ -->
<section class="section" id="features">
  <div class="wrap">
    <SectionHead no="01" title="一局里用得到的，都在侧栏里" en="Functions">从开局到商店再到结算，侧栏跟着一局的进程走。下面是真实界面，数据是示例。</SectionHead>

    <div class="guide" bind:this={guideEl}>
      <ol class="prog" aria-label="功能">
        {#each FEATURES as f, i (f.id)}
          <li>
            <button class:on={cur === i} onclick={() => pick(i)} aria-current={cur === i}>
              <span class="no num">0{i + 1}</span>
              <span class="pt">
                <span class="tl"><b>{f.tag}</b><span class="lbl">{f.en}</span></span>
                <span class="st2">{f.title}</span>
              </span>
              {#if cur === i && auto}<i class="timer" style:transform="scaleX({tick / 80})"></i>{/if}
            </button>
          </li>
        {/each}
      </ol>

      <div class="screen">
        <div class="bar">
          <span class="lbl">CH <b class="mint">0{cur + 1}</b> / 05 · {FEATURES[cur].en}</span>
          <span class="lbl">{auto ? 'Auto' : 'Manual'}</span>
        </div>
        <div class="tv">
          {#key cur}
            <div class="ch" class:shop={cur === 2}>
              <div class="pic pic-{FEATURES[cur].id}">
                {#if cur === 0}
                  <img class="shot s1" src={match} alt="本局页" />
                {:else if cur === 1}
                  <img class="shot s1" src={target} alt="阵容：目标" />
                  <img class="shot s2" src={plan} alt="阵容：编辑" />
                {:else if cur === 2}
                  <div class="hud wide"><ShopStage crop /></div>
                {:else if cur === 3}
                  <img class="shot s1" src={codex} alt="图鉴：干员" />
                  <img class="shot s2" src={operator} alt="图鉴：干员详情" />
                {:else}
                  <img class="shot plz" src={plaza} alt="阵容广场网站" />
                {/if}
              </div>
              <div class="info">
                <h3>{FEATURES[cur].title}</h3>
                <p>{FEATURES[cur].desc}</p>
                <ul>
                  {#each FEATURES[cur].points as p (p)}<li><Icon name="check" size={16} />{p}</li>{/each}
                </ul>
                {#if cur === 4}<a class="btn sm" href={LINKS.plaza}>打开阵容广场<Icon name="arrow" size={15} /></a>{/if}
              </div>
            </div>
          {/key}
        </div>
      </div>
    </div>

    <!-- 手机：逐条排下来 -->
    <div class="stack">
      {#each FEATURES as f, i (f.id)}
        <article use:reveal>
          <span class="lbl"><b class="mint">0{i + 1}</b> · {f.en}</span>
          <h3>{f.tag}<span class="dim"> · </span>{f.title}</h3>
          <p>{f.desc}</p>
          <div class="pic">
            {#if i === 0}<img class="shot" src={match} alt="本局页" loading="lazy" />
            {:else if i === 1}<img class="shot" src={plan} alt="阵容编辑" loading="lazy" />
            {:else if i === 2}<div class="hud"><ShopStage crop /></div>
            {:else if i === 3}<img class="shot" src={operator} alt="干员详情" loading="lazy" />
            {:else}<img class="shot" src={plaza} alt="阵容广场" loading="lazy" />{/if}
          </div>
          <ul>{#each f.points as p (p)}<li><Icon name="check" size={15} />{p}</li>{/each}</ul>
          {#if i === 4}<a class="btn sm" href={LINKS.plaza}>打开阵容广场<Icon name="arrow" size={15} /></a>{/if}
        </article>
      {/each}
    </div>
  </div>
</section>

<!-- ============ 皮肤 ============ -->
<section class="section alt skins">
  <div class="wrap sk-in">
    <div class="sk-copy" use:reveal>
      <span class="lbl"><b class="mint">// 02</b>&nbsp;&nbsp;Themes</span>
      <h2>跟着游戏主题换装</h2>
      <p>游戏里换了卫戍协议的主题，侧栏自动跟着换：卫戍协议终端、乌萨斯齐心团、『全能的』可露希尔，用的都是游戏里的原素材。也可以在设置里固定一套。</p>
      <button class="btn" onclick={() => (demo = true)}><Icon name="play" size={18} />在演示里切换看看</button>
    </div>
    <div class="sk-pics">
      <figure use:reveal={0}><img class="shot" src={match} alt="卫戍协议终端主题" width="380" height="860" loading="lazy" /><figcaption class="lbl">Terminal · 终端</figcaption></figure>
      <figure use:reveal={90}><img class="shot" src={ursus} alt="乌萨斯齐心团主题" width="380" height="860" loading="lazy" /><figcaption class="lbl">Ursus · 乌萨斯</figcaption></figure>
      <figure use:reveal={180}><img class="shot" src={closure} alt="可露希尔主题" width="380" height="860" loading="lazy" /><figcaption class="lbl">Closure · 可露希尔</figcaption></figure>
    </div>
  </div>
</section>

<!-- ============ 还有 ============ -->
<section class="section">
  <div class="wrap">
    <SectionHead no="03" title="还有这些" en="More" />
    <div class="more">
      {#each MORE as m, i (m.title)}
        <div class="mc" use:reveal={(i % 3) * 70}>
          <span class="ic"><Icon name={m.icon} size={22} /></span>
          <span class="lbl">{m.en}</span>
          <h3>{m.title}</h3>
          <p>{m.text}</p>
        </div>
      {/each}
    </div>
  </div>
</section>

<!-- ============ 下载 ============ -->
<section class="section alt" id="download">
  <div class="wrap">
    <SectionHead no="04" title="下载" en="Download">三个地方的安装包都一样，选一个顺手的。</SectionHead>
    <Downloads />
  </div>
</section>

<!-- ============ 更新 + 问答 ============ -->
<section class="section">
  <div class="wrap two">
    <div>
      <SectionHead no="05" title="最近更新" en="Updates" small />
      <ReleaseList releases={RELEASES.slice(0, 3)} compact />
      <a class="more-link" href="/changelog/">全部 {RELEASES.length} 个版本<Icon name="arrow" size={16} /></a>
    </div>
    <div>
      <SectionHead no="06" title="常见问题" en="FAQ" small />
      <FaqList items={homeFaq} />
      <a class="more-link" href="/faq/">更多问题<Icon name="arrow" size={16} /></a>
    </div>
  </div>
</section>

<DemoDialog bind:open={demo} />

<style>
  /* ---------- 首屏 ---------- */
  .hero {
    position: relative;
    padding: calc(var(--head-h) + 64px) 0 40px;
    overflow: hidden;
  }
  .grid-bg {
    position: absolute;
    inset: 0;
    background-image:
      linear-gradient(rgb(var(--hl-rgb) / 0.035) 1px, transparent 1px),
      linear-gradient(90deg, rgb(var(--hl-rgb) / 0.035) 1px, transparent 1px);
    background-size: 64px 64px;
    mask: radial-gradient(ellipse 80% 70% at 70% 40%, #000 30%, transparent 75%);
    pointer-events: none;
  }
  .hero-in {
    position: relative;
    display: grid;
    grid-template-columns: minmax(0, 5fr) minmax(0, 7fr);
    gap: 48px;
    align-items: center;
  }
  .eyebrow {
    display: inline-flex;
    align-items: center;
    gap: 10px;
    color: var(--fg-2);
  }
  .sq {
    width: 10px;
    height: 10px;
    background: var(--mint);
    box-shadow: var(--mint-glow);
  }
  h1 {
    margin-top: 22px;
    font-size: clamp(40px, 5.3vw, 88px);
    white-space: nowrap;
    font-weight: 700;
    line-height: 1.05;
    letter-spacing: 0.02em;
    color: var(--fg-0);
    text-shadow: 0 2px 30px rgb(var(--shade-rgb) / 0.6);
  }
  .en {
    display: block;
    margin: 14px 0 22px;
    font-family: var(--font-term);
    font-size: clamp(14px, 1.5vw, 20px);
    font-weight: 600;
    letter-spacing: 0.42em;
    text-transform: uppercase;
    color: var(--mint);
    text-shadow: 0 0 18px rgb(var(--mint-rgb) / 0.45);
  }
  .lead {
    margin-top: 28px;
    font-size: clamp(17px, 1.5vw, 20px);
    color: var(--fg-1);
  }
  .flow {
    list-style: none;
    margin: 18px 0 0;
    padding: 0;
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 8px 20px;
    font-size: 14px;
    color: var(--fg-2);
  }
  .flow li {
    display: flex;
    gap: 10px;
    align-items: baseline;
  }
  .flow .num {
    font-size: 12px;
    color: var(--mint);
  }
  .cta {
    display: flex;
    flex-wrap: wrap;
    gap: 12px;
    margin-top: 34px;
  }
  .big {
    height: 52px;
    padding: 0 24px;
    font-size: 16px;
  }
  .meta {
    display: flex;
    flex-wrap: wrap;
    gap: 10px 28px;
    margin-top: 30px;
    font-size: 13px;
    color: var(--fg-2);
  }
  .meta > span {
    display: flex;
    flex-direction: column;
    gap: 2px;
  }
  .meta .lbl {
    font-size: 10px;
  }

  .visual {
    position: relative;
    aspect-ratio: 700 / 560;
  }
  .game {
    position: absolute;
    left: 0;
    top: 13%;
    width: 72%;
    box-shadow: 0 30px 80px rgb(var(--shade-rgb) / 0.6);
  }
  .cap {
    position: absolute;
    left: 0;
    top: 100%;
    margin-top: 10px;
    display: flex;
    align-items: center;
    gap: 8px;
  }
  .side {
    position: absolute;
    right: 0;
    top: 0;
    width: 34%;
    height: auto;
    animation: dock 900ms var(--ease-out) 200ms both;
  }
  @keyframes dock {
    from {
      opacity: 0;
      transform: translateX(24px);
    }
  }
  .tagline {
    position: absolute;
    right: 0;
    top: calc(34% / 380 * 860 * 700 / 560 + 2%);
    font-size: 10px;
  }
  .bondrow {
    display: flex;
    justify-content: space-between;
    gap: 6px;
    margin-top: 56px;
    padding: 18px 4px 0;
    border-top: 1px solid var(--line-soft);
    overflow: hidden;
  }

  /* ---------- 数字 ---------- */
  .stats {
    padding: 40px 0 8px;
  }
  .st-in {
    display: grid;
    grid-template-columns: repeat(5, 1fr);
  }
  .st {
    display: flex;
    flex-direction: column;
    padding: 4px 0 4px 20px;
    border-left: 1px solid var(--line);
  }
  .st:first-child {
    border-left-color: var(--mint);
  }
  .st b {
    font-size: var(--d-num);
    font-weight: 600;
    line-height: 1.05;
    color: var(--fg-0);
  }
  .st .cn {
    font-size: 14px;
    color: var(--fg-2);
  }
  .st-note {
    margin-top: 22px;
    font-size: 10px;
  }

  /* ---------- 功能频道 ---------- */
  .guide {
    display: grid;
    grid-template-columns: 340px 1fr;
    gap: 28px;
  }
  .prog {
    list-style: none;
    margin: 0;
    padding: 0;
    display: flex;
    flex-direction: column;
    border-top: 1px solid var(--line);
  }
  .prog button {
    position: relative;
    width: 100%;
    display: flex;
    gap: 16px;
    align-items: flex-start;
    padding: 20px 14px;
    text-align: left;
    border-bottom: 1px solid var(--line);
    transition: background var(--t-fast);
  }
  .prog button:hover {
    background: rgb(var(--hl-rgb) / 0.03);
  }
  .prog button.on {
    background: linear-gradient(90deg, rgb(var(--mint-rgb) / 0.12), rgb(var(--mint-rgb) / 0.02));
  }
  .prog button.on::before {
    content: '';
    position: absolute;
    left: 0;
    top: 0;
    bottom: 0;
    width: 3px;
    background: var(--mint);
    box-shadow: var(--mint-glow);
  }
  .no {
    font-size: 22px;
    line-height: 1.2;
    color: var(--fg-4);
  }
  .on .no {
    color: var(--mint);
  }
  .pt {
    display: flex;
    flex-direction: column;
    gap: 2px;
  }
  .tl {
    display: flex;
    align-items: baseline;
    gap: 10px;
  }
  .tl b {
    font-size: 17px;
  }
  .st2 {
    font-size: 13px;
    color: var(--fg-3);
  }
  .on .st2 {
    color: var(--fg-2);
  }
  .timer {
    position: absolute;
    left: 0;
    right: 0;
    bottom: -1px;
    height: 2px;
    background: var(--mint);
    transform-origin: 0 0;
  }

  .screen {
    min-width: 0;
    border: 1px solid var(--line);
    background:
      var(--hatch),
      linear-gradient(180deg, var(--bg-1a), var(--bg-0));
  }
  .screen .bar {
    display: flex;
    justify-content: space-between;
    padding: 10px 16px;
    border-bottom: 1px solid var(--line);
    background: var(--bg-1);
  }
  .tv {
    position: relative;
    height: 600px;
    overflow: hidden;
  }
  .tv .pic {
    mask: linear-gradient(180deg, #000 82%, transparent);
  }
  .ch {
    position: absolute;
    inset: 0;
    display: grid;
    grid-template-columns: minmax(0, 1.25fr) minmax(0, 1fr);
    animation: ch 500ms var(--ease-out) both;
  }
  @keyframes ch {
    0% {
      opacity: 0;
      filter: brightness(2) saturate(0);
    }
    30% {
      opacity: 1;
    }
    100% {
      opacity: 1;
      filter: brightness(1) saturate(1);
    }
  }
  .pic {
    position: relative;
    min-width: 0;
  }
  .ch .pic .shot {
    position: absolute;
    height: 560px;
    width: auto;
    max-width: none;
  }
  .ch .pic .s1 {
    left: 28px;
    top: 24px;
  }
  .ch .pic .s2 {
    left: 150px;
    top: 56px;
    height: 520px;
  }
  .pic-match .s1 {
    left: 50% !important;
    transform: translateX(-50%);
  }
  /* 商店台：实拍图横放在上面，文字在下面 */
  .ch.shop {
    grid-template-columns: 1fr;
    grid-template-rows: auto 1fr;
  }
  .ch.shop .pic {
    mask: none;
    padding: 36px 36px 0;
  }
  .ch.shop .info {
    background: none;
    justify-content: flex-start;
    display: grid;
    grid-template-columns: 1fr 1fr;
    column-gap: 36px;
    row-gap: 12px;
    padding-top: 28px;
  }
  .ch.shop .info h3 {
    grid-column: 1 / -1;
  }
  .ch.shop .info ul {
    grid-row: 2 / 4;
    grid-column: 2;
  }
  .ch .pic .plz {
    left: 28px;
    top: 40px;
    height: 520px;
  }
  .info {
    position: relative;
    z-index: 1;
    display: flex;
    flex-direction: column;
    justify-content: center;
    gap: 16px;
    padding: 32px 36px;
    background: linear-gradient(90deg, transparent, rgb(var(--bg-0-rgb) / 0.92) 18%);
  }
  .info h3 {
    font-size: var(--d-2);
    font-weight: 700;
    line-height: 1.3;
  }
  .info p {
    color: var(--fg-2);
  }
  ul {
    list-style: none;
    margin: 0;
    padding: 0;
    display: flex;
    flex-direction: column;
    gap: 8px;
    font-size: 14px;
    color: var(--fg-1);
  }
  ul li {
    display: flex;
    gap: 8px;
    align-items: flex-start;
  }
  ul :global(.ri) {
    margin-top: 3px;
    color: var(--mint);
  }
  .info .btn {
    align-self: flex-start;
  }
  .stack {
    display: none;
  }

  /* ---------- 皮肤 ---------- */
  .sk-in {
    display: grid;
    grid-template-columns: minmax(0, 4fr) minmax(0, 7fr);
    gap: 56px;
    align-items: center;
  }
  .sk-copy h2 {
    margin: 10px 0 18px;
    font-size: var(--d-1);
    line-height: 1.2;
  }
  .sk-copy p {
    margin-bottom: 26px;
    color: var(--fg-2);
  }
  .sk-pics {
    display: grid;
    grid-template-columns: repeat(3, 1fr);
    gap: 18px;
  }
  figure {
    margin: 0;
  }
  figure:nth-child(2) {
    margin-top: 40px;
  }
  figure:nth-child(3) {
    margin-top: 80px;
  }
  figure img {
    width: 100%;
    height: auto;
  }
  figcaption {
    margin-top: 10px;
    font-size: 10px;
  }

  /* ---------- 还有 ---------- */
  .more {
    display: grid;
    grid-template-columns: repeat(3, 1fr);
    gap: 1px;
    background: var(--line);
    border: 1px solid var(--line);
  }
  .mc {
    display: flex;
    flex-direction: column;
    gap: 6px;
    padding: 28px;
    background: var(--bg-0);
    transition: background var(--t-mid);
  }
  .mc:hover {
    background: var(--bg-1);
  }
  .ic {
    display: grid;
    place-items: center;
    width: 44px;
    height: 44px;
    margin-bottom: 12px;
    color: var(--mint);
    background: var(--mint-tint);
    border: 1px solid rgb(var(--mint-rgb) / 0.3);
  }
  .mc h3 {
    font-size: 18px;
  }
  .mc p {
    font-size: 14px;
    color: var(--fg-2);
  }

  /* ---------- 更新 + 问答 ---------- */
  .two {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 64px;
  }
  .more-link {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    margin-top: 20px;
    font-size: 14px;
    color: var(--mint);
  }
  .more-link:hover {
    gap: 10px;
  }

  @media (prefers-reduced-motion: reduce) {
    .ch,
    .side {
      animation: none;
    }
  }

  /* ---------- 窄屏 ---------- */
  @media (max-width: 1080px) {
    h1 {
      font-size: clamp(44px, 11vw, 80px);
    }
    .hero-in {
      grid-template-columns: 1fr;
      gap: 56px;
    }
    .visual {
      max-width: 720px;
    }
    .guide {
      display: none;
    }
    .stack {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 56px 40px;
    }
    .stack article {
      display: flex;
      flex-direction: column;
      gap: 12px;
    }
    .stack h3 {
      font-size: 20px;
    }
    .stack p {
      color: var(--fg-2);
      font-size: 14px;
    }
    .stack .pic {
      margin: 8px 0;
      max-height: 520px;
      overflow: hidden;
      mask: linear-gradient(180deg, #000 80%, transparent);
    }
    .stack .pic img {
      width: 100%;
      max-width: 380px;
      height: auto;
    }
    .sk-in,
    .two {
      grid-template-columns: 1fr;
      gap: 48px;
    }
    .more {
      grid-template-columns: 1fr 1fr;
    }
  }
  @media (max-width: 720px) {
    .hero {
      padding-top: calc(var(--head-h) + 36px);
    }
    .flow {
      grid-template-columns: 1fr;
    }
    .cta .btn {
      flex: 1;
    }
    .bondrow {
      flex-wrap: wrap;
      justify-content: flex-start;
      gap: 12px;
      margin-top: 40px;
    }
    .st-in {
      grid-template-columns: repeat(3, 1fr);
      row-gap: 20px;
    }
    .st {
      padding-left: 12px;
    }
    .stack {
      grid-template-columns: 1fr;
    }
    .more {
      grid-template-columns: 1fr;
    }
    .mc {
      padding: 22px;
    }
    .sk-pics {
      gap: 10px;
    }
  }
</style>
