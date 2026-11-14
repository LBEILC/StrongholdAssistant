// 常见问题。答案是 HTML 片段（只用 <b>、<a>、<code>、<span class="kbd">），首页摘几条，常见问题页全放
export type Qa = { id: string; q: string; a: string; home?: boolean }
export type QaGroup = { id: string; title: string; en: string; items: Qa[] }

const REL = 'https://github.com/LBEILC/StrongholdAssistant/releases/latest'

export const FAQ: QaGroup[] = [
  {
    id: 'install',
    title: '安装与更新',
    en: 'Install',
    items: [
      {
        id: 'system',
        q: '需要什么电脑？',
        a: '<b>Windows 10 / 11，64 位</b>。界面用 Microsoft Edge WebView2 显示，Windows 11 自带，Windows 10 一般也装好了；没有的话到<a href="https://developer.microsoft.com/microsoft-edge/webview2/" target="_blank" rel="noopener">微软官网</a>装 Evergreen 版本。.NET 和识别用的运行库都已经打包，不用另外装。',
      },
      {
        id: 'which-file',
        q: '安装包和免安装版选哪个？',
        a: '推荐 <b>安装包</b> <code>StrongholdAssistant-v版本号-setup.exe</code>：装在当前用户目录，不要管理员权限，会建开始菜单和桌面快捷方式。<b>免安装版</b> <code>…-win-x64.zip</code> 要先右键「全部解压」到一个单独的文件夹再运行。在 GitHub 下载时不要下 Source code。',
      },
      {
        id: 'smartscreen',
        q: 'Windows 提示「Windows 已保护你的电脑」怎么办？',
        a: '程序还没有代码签名，SmartScreen 会拦一下。点 <b>更多信息 → 仍要运行</b> 就行。安装包只从 Mirror酱、QQ 群文件和 <a href="' + REL + '" target="_blank" rel="noopener">GitHub Releases</a> 发布，其他地方下的不要用。',
        home: true,
      },
      {
        id: 'update',
        q: '怎么更新？会丢阵容吗？',
        a: '启动时自动检查新版本（设置里可以关），有新版本点 <b>更新</b>，下载校验后自动安装并重启。阵容、设置和历史存在 <code>%AppData%\\StrongholdAssistant</code>，更新不会动它们。在设置 → 版本里把更新源换成 Mirror酱 并填上 CDK，更新也走国内高速下载。',
      },
    ],
  },
  {
    id: 'game',
    title: '游戏与模拟器',
    en: 'Game & Emulator',
    items: [
      {
        id: 'sources',
        q: '支持哪些游戏客户端？',
        a: '《明日方舟》<b>PC 客户端</b>、<b>MuMu 模拟器</b>和<b>雷电模拟器</b>。设置里的「游戏来源」默认自动：先找 PC 客户端，没有再找 MuMu 和雷电里正在运行明日方舟的实例。模拟器用 ADB 截图，游戏窗口被挡住也能识别；同时开了好几个实例时，在设置里选一个就行。',
      },
      {
        id: 'ldplayer',
        q: '雷电模拟器要怎么设置？',
        a: '第一次用要改两处：雷电的 <b>设置 → 其他设置 → ADB 调试</b> 改成「<b>开启本地连接</b>」；<b>性能设置 → 分辨率</b> 用横屏 16:9，比如 1920×1080。没设好时助手标题栏会提示该改哪里。',
        home: true,
      },
      {
        id: 'mumu',
        q: 'MuMu 模拟器要怎么设置？',
        a: '一般不用改。分辨率用横屏、宽高比在 3:2 到 16:9 之间（比如 1920×1080）。找到游戏但用不了时，标题栏写「无法连接」，鼠标移上去能看到原因。',
        home: true,
      },
      {
        id: 'screen',
        q: '对分辨率和画面有要求吗？',
        a: '宽高比 3:2 到 16:9 的画面都行（1920×1080、2K、4K、16:10 都验证过）；比 16:9 更宽的带鱼屏可能识别不全。游戏里的「界面缩放」（90～100）会自动识别，不用改。',
      },
      {
        id: 'cover',
        q: '侧栏挡住游戏画面了？',
        a: '点标题栏的收起按钮缩成细条，或者按快捷键（默认 <span class="kbd">Alt</span>+<span class="kbd">S</span>，游戏在前台也能按）。全屏或右边放不下时，侧栏会盖在游戏画面左侧。编阵容、逛广场嫌窄，点 <b>展开为工作台</b> 变成宽窗口。',
      },
    ],
  },
  {
    id: 'use',
    title: '识别与使用',
    en: 'Recognition',
    items: [
      {
        id: 'scan',
        q: '开局信息没有识别出来？',
        a: '进卫戍协议后停在 <b>「确认本局信息」</b> 页，侧栏会自动录入。禁用列表长就点 <b>持续识别</b> 再慢慢往下滚，看全后点 <b>停止</b>。录错了右键改，没认出的问号格点开选。还不行的话，在设置里导出 <b>诊断包</b>，发到 QQ 群或 GitHub Issues，我们照着修。',
      },
      {
        id: 'solo',
        q: '单人模式能用吗？',
        a: '能。进对局后点左上角回合数旁边的放大镜打开 <b>对局详情</b>，侧栏会自动识别；每局只读第一次，之后反复打开不再重读。',
      },
    ],
  },
  {
    id: 'trust',
    title: '安全与费用',
    en: 'Safety & Cost',
    items: [
      {
        id: 'safe',
        q: '会不会影响游戏账号？',
        a: '助手只通过 <b>截屏</b> 看游戏画面：不读游戏内存，不注入游戏，不向游戏发送点击或按键。识别开局信息时如果侧栏挡住了画面，会短暂隐藏侧栏、把游戏切到前台再截图。它是玩家自制的第三方工具，与鹰角网络无关，是否使用请自己判断。',
        home: true,
      },
      {
        id: 'free',
        q: '收费吗？Mirror酱 是什么？',
        a: '助手 <b>免费</b>，代码开源，所有功能都不收费。<b>Mirror酱</b> 是第三方的国内高速下载服务，用它下载和更新需要购买 CDK；不买的话从 QQ 群文件或 GitHub 免费下载，用起来完全一样。同一个 CDK 也能用在 MAA 等其他接入 Mirror酱 的软件上。',
      },
      {
        id: 'data',
        q: '我的数据存在哪里？会联网吗？',
        a: '阵容、设置、追踪和历史默认只存在本机。默认联网的只有启动时检查一次新版本和下载公开的资金表；阵容广场、云同步、匿名补全资金表、森空岛都要你自己打开。详见<a href="/about/#privacy">数据与隐私</a>。',
      },
    ],
  },
]
