<a href="https://github.com/LBEILC/StrongholdAssistant/releases/latest"><img src=".github/readme/hero.jpg" alt="卫戍协议助手 Stronghold Assistant：《明日方舟》卫戍协议 PC 辅助侧栏" width="100%"></a>

<p align="center">
  <a href="https://github.com/LBEILC/StrongholdAssistant/releases/latest"><img src=".github/readme/button-release.png" alt="下载最新版" height="36"></a>
  <a href="LICENSE"><img src=".github/readme/button-license.png" alt="许可：Apache-2.0" height="36"></a>
  <a href="#安装"><img src=".github/readme/button-platform.png" alt="平台：Windows x64" height="36"></a>
  <a href="https://github.com/LBEILC/StrongholdAssistant/issues"><img src=".github/readme/button-feedback.png" alt="问题反馈" height="36"></a>
  <a href="https://qm.qq.com/q/AuRBCSxWEg"><img src=".github/readme/button-qq.png" alt="QQ 交流群 1132134217" height="36"></a>
</p>

<p align="center">
  贴在游戏窗口旁边的卫戍协议侧栏。<br>
  打开「确认本局信息」页就自动录入盟约、禁用和敌人，按本局情况规划阵容，商店阶段一眼看到盟约还差几个、还要买谁。
</p>

<p align="center">
  QQ 交流群 <b>1132134217</b> · <a href="https://qm.qq.com/q/AuRBCSxWEg">点击链接加入群聊【卫戍小助手StrongholdAssistant交流群】</a>
</p>

<br>

<a name="安装"></a>
<img src=".github/readme/section-01-install.png" alt="//安装 INSTALLATION" width="100%">

<img src=".github/readme/sub-download.png" alt="下载与安装：Windows 10 / 11 x64" width="100%">

<table>
<tr>
<td width="120"><img src=".github/readme/chip-download.png" alt="下载" width="112"></td>
<td>在 <a href="https://github.com/LBEILC/StrongholdAssistant/releases/latest"><b>Releases</b></a> 下载 <code>StrongholdAssistant-v版本号-win-x64.zip</code>（不要下 Source code）。</td>
</tr>
<tr>
<td><img src=".github/readme/chip-install.png" alt="安装" width="112"></td>
<td>右键 <b>全部解压</b> 到一个单独的文件夹，保留里面的 <code>data</code> 等全部文件。不需要另外安装 .NET。</td>
</tr>
<tr>
<td><img src=".github/readme/chip-open.png" alt="打开" width="112"></td>
<td>双击 <b>StrongholdAssistant.exe</b>。找到游戏窗口后，侧栏会自动贴到游戏右侧；拖动侧栏后会记住相对位置，跟着游戏走。</td>
</tr>
<tr>
<td><img src=".github/readme/chip-update.png" alt="更新" width="112"></td>
<td>启动时会自动检查新版本（可在设置里关掉），有新版本时点 <b>更新</b>，下载校验后自动替换并重启。阵容和设置存在 <code>%AppData%\StrongholdAssistant</code>，更新不会动它们。</td>
</tr>
<tr>
<td><img src=".github/readme/chip-uninstall.png" alt="卸载" width="112"></td>
<td>删除解压出来的文件夹即可。想连数据一起删，再删掉 <code>%AppData%\StrongholdAssistant</code>。</td>
</tr>
</table>

> [!WARNING]
> 程序还没有代码签名，Windows SmartScreen 可能提示「Windows 已保护你的电脑」，点 **更多信息 → 仍要运行**。

<br>

<img src=".github/readme/sub-using.png" alt="使用：侧栏贴在游戏窗口旁" width="100%">

1. 游戏用 **窗口模式** 时，侧栏贴在游戏右侧；**全屏** 或右边放不下时，侧栏盖在游戏画面左侧（避开右侧的操作按钮），点标题栏的收起按钮可以缩成一条细栏。
2. 进入卫戍协议，开局时停在 **「确认本局信息」** 页，侧栏会自动录入本局的盟约、禁用干员、敌方领袖和特训敌人。禁用干员列表很长时，往下滚动后点 **重新识别**，新认出的会累加进去。录错了点 **修正** 手动改。
3. 在 **阵容 → 规划** 里选目标盟约，生成候选阵容后 **设为目标**；也可以从 **我的阵容** 里挑一套。
4. 商店阶段看侧栏顶部：盟约进度环告诉你还差几个，**待购** 头像是还没买到的干员，买到了点一下头像就标记为已持有。游戏商店里，目标阵容和追踪中的干员会被打上薄荷绿星标。

<br>

<img src=".github/readme/section-02-features.png" alt="//功能 FUNCTIONS" width="100%">

<img src=".github/readme/feat-match.jpg" alt="本局：开局信息识别" width="100%">

| 功能 | 说明 |
| --- | --- |
| **开局信息识别** | 打开「确认本局信息」页时自动截图识别：核心 / 附加盟约哪些阵容不完整、敌方领袖、特训敌人。识别失败会说明原因，不会覆盖已有结果。 |
| **禁用干员识别** | 直接认禁用列表里的头像，再加上按不完整盟约推算的结果；列表长就滚动后再识别一次，同一局的结果会累加。宁可漏认也不认错，漏掉的可以手动补。 |
| **手动修正** | 点盟约切换「可用 / 阵容不完整」，禁用干员随之重新推算；单个干员可以在详情里标记或解除禁用。 |
| **本局可用阵容** | 我的阵容按本局被禁人数排序，被禁的干员直接标出来。各分组可以折叠，禁用干员按盟约分组。 |

<img src=".github/readme/feat-lineup.jpg" alt="阵容：规划与追踪" width="100%">

| 功能 | 说明 |
| --- | --- |
| **阵容规划** | 选目标盟约的 I / II 档，可锁定必选干员，按 8 / 9 人、0 – 2 次转职搜索候选；自动避开本局被禁的干员，阵容不完整的核心盟约不能选。 |
| **目标阵容** | 设为目标后，侧栏顶部一直显示目标盟约的进度和还没买到的干员；点头像标记已持有。 |
| **商店标记** | 游戏商店里给目标阵容和追踪中的干员打上星标，在设置里选好游戏主题（卫戍协议终端 / 乌萨斯齐心团 / 可露希尔），也可以关掉。 |
| **阵容编辑** | 新建、改名、增删干员，保存在本机，下次打开还在。 |

<img src=".github/readme/feat-codex.jpg" alt="图鉴：干员与装备" width="100%">

| 功能 | 说明 |
| --- | --- |
| **干员图鉴** | 仿商店卡的网格，按等阶分组，可按等阶、盟约（多选）、追踪中、隐藏禁用筛选。 |
| **装备图鉴** | 按等阶和功能（攻击、防御、生存、技力、经济、盟约等）筛选。 |
| **详情面板** | 点一下在底部展开普通 / 精锐特性、盟约效果和成员，不靠鼠标悬停；可以追踪、规划时锁定、标记禁用。 |

<br>

<img src=".github/readme/section-03-require.png" alt="//运行要求 REQUIREMENTS" width="100%">

<table>
<tr>
<td width="120"><img src=".github/readme/chip-system.png" alt="系统" width="112"></td>
<td><b>Windows 10 / 11，64 位（x64）</b>。</td>
</tr>
<tr>
<td><img src=".github/readme/chip-runtime.png" alt="运行库" width="112"></td>
<td>界面用 <b>Microsoft Edge WebView2</b> 显示，Windows 11 自带，Windows 10 一般也已安装；没有的话到 <a href="https://developer.microsoft.com/microsoft-edge/webview2/">微软官网</a> 装 Evergreen 版本。.NET 和识别用的运行库已经打包。</td>
</tr>
<tr>
<td><img src=".github/readme/chip-game.png" alt="游戏" width="112"></td>
<td>《明日方舟》PC 版，当前数据对应 <b>卫戍协议 · 规程 · 序列 I</b>（141 名干员、70 件装备、26 个盟约）。</td>
</tr>
<tr>
<td><img src=".github/readme/chip-screen.png" alt="画面" width="112"></td>
<td>16:9 画面：1920×1080 实测，2K、4K 用放大的截图验证过；其他比例可能识别不全。设置里的 <b>界面大小</b> 默认跟随游戏画面，2K、4K 下侧栏和游戏界面一样大。</td>
</tr>
</table>

<img src=".github/readme/sub-privacy.png" alt="数据与隐私：只在本机" width="100%">

<table>
<tr>
<td width="120"><img src=".github/readme/chip-privacy.png" alt="隐私" width="112"></td>
<td>只通过截屏识别游戏画面：不读取游戏内存，不注入游戏，不向游戏发送点击或按键。识别开局信息时会短暂隐藏侧栏、把游戏窗口切到前台再截图。</td>
</tr>
<tr>
<td><img src=".github/readme/chip-data.png" alt="数据" width="112"></td>
<td>阵容、设置和追踪状态只保存在本机 <code>%AppData%\StrongholdAssistant</code>，不上传。</td>
</tr>
</table>

<br>

<img src=".github/readme/section-04-develop.png" alt="//开发 DEVELOPMENT" width="100%">

<img src=".github/readme/sub-build.png" alt="从源码构建：.NET 8 与 Node.js" width="100%">

需要 .NET 8 SDK（或更新的 SDK）和 Node.js 20.19 / 22.12 以上。

```powershell
git clone https://github.com/LBEILC/StrongholdAssistant.git
cd StrongholdAssistant
npm --prefix src/ui ci
npm --prefix src/ui run build
dotnet run --project src/StrongholdAssistant.Host
```

| 目录 | 内容 |
| --- | --- |
| `src/ui/` | 侧栏界面：Vite + TypeScript + Svelte 5。`npm --prefix src/ui run dev` 可以在浏览器里用假数据单独开发界面。 |
| `src/StrongholdAssistant.Host/` | 宿主：WPF 窗口 + WebView2，消息桥，窗口跟踪与停靠。 |
| `src/ASPAssistant.Core/` | 识别、数据、阵容规划，来自上游项目。 |
| `data/` | 当期干员、装备、盟约等数据与识别资源。 |

界面和宿主之间的接口定义在 `src/ui/src/lib/api/types.ts`，消息协议写在同目录的 `bridge.ts`。

<br>

<img src=".github/readme/section-05-credits.png" alt="//鸣谢与许可 CREDITS" width="100%">

<table>
<tr>
<td width="120"><img src=".github/readme/chip-upstream.png" alt="上游" width="112"></td>
<td>基于 <a href="https://github.com/bingyunhe/asp-assistant-arknights">bingyunhe/asp-assistant-arknights</a> 改造，它又基于 <a href="https://github.com/Moonfair/ASP_Assistant">Moonfair/ASP_Assistant</a>。识别、数据和阵容规划沿用上游，本项目重做了界面与交互。阵容规划由 <a href="https://github.com/CarlosDJY">CarlosDJY</a> 贡献给上游。</td>
</tr>
<tr>
<td><img src=".github/readme/chip-thanks.png" alt="鸣谢" width="112"></td>
<td>图像识别 <a href="https://github.com/MaaXYZ/MaaFramework">MaaFramework</a>；游戏数据 <a href="https://prts.wiki">PRTS</a>、SPDatabase；界面素材 <a href="https://github.com/ArknightsAssets/ArknightsAssets2">ArknightsAssets</a>；图标 <a href="https://remixicon.com">Remix Icon</a>；字体 <a href="https://hyperos.mi.com/font">MiSans</a>。</td>
</tr>
<tr>
<td><img src=".github/readme/chip-community.png" alt="交流" width="112"></td>
<td>QQ 交流群 <b>1132134217</b>：<a href="https://qm.qq.com/q/AuRBCSxWEg">点击链接加入群聊【卫戍小助手StrongholdAssistant交流群】</a>。使用问题和建议也可以在 <a href="https://github.com/LBEILC/StrongholdAssistant/issues">Issues</a> 提。</td>
</tr>
<tr>
<td><img src=".github/readme/chip-license.png" alt="许可" width="112"></td>
<td>代码以 <a href="LICENSE">Apache-2.0</a> 开源，上游的许可与声明保留在 <a href="LICENSE.upstream">LICENSE.upstream</a> 和 <a href="NOTICE">NOTICE</a>。</td>
</tr>
<tr>
<td><img src=".github/readme/chip-art.png" alt="美术" width="112"></td>
<td>游戏美术与数据归 <b>上海鹰角网络</b> 所有，来源与归属见 <a href="ASSETS.md">ASSETS.md</a>；权利方要求时会移除。字体、图标等第三方素材遵循各自的许可。</td>
</tr>
</table>

<sub>※ 非官方玩家自制工具，与鹰角网络无关。不收费，不接商业赞助。</sub>

<br>

<img src=".github/readme/signoff.png" alt="协议结束。See you in the next stronghold, Doctor." width="100%">

<a href="https://github.com/LBEILC/StrongholdAssistant/issues"><img src=".github/readme/footer.png" alt="[ 反馈问题 ]" width="100%"></a>
