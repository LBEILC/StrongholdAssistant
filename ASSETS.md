# 素材来源与归属

本项目使用的《明日方舟》游戏美术与数据，著作权归 **上海鹰角网络科技有限公司（Hypergryph）** 所有。本项目是免费的玩家自制工具，与鹰角网络无关，未获其认可或授权。权利方提出要求时，我们会移除相关素材。

`LICENSE`（Apache-2.0）只覆盖本项目的代码，不覆盖下列素材。

## 界面素材

`data/garrison_routes.json` 的敌人路线由 `scripts/generate-garrison-routes.mjs` 从 [ArknightsGameData](https://github.com/Kengxxiao/ArknightsGameData) 国服 `gamedata/levels/activities/act3autochess/` 的关卡文件和 `activity_table.json`（`act3autochess/V3_1`）生成（2026-11-13）；游戏数据归上海鹰角网络所有。`data/garrison_board_refs.json` 的棋盘镜头和各地图颜色样本改编自 [bingyunhe/asp-assistant-arknights](https://github.com/bingyunhe/asp-assistant-arknights) dev 分支（Apache-2.0），样本取自游戏画面。测试用的 4 张棋盘截图 `src/StrongholdAssistant.Tests/TestData/board-*.bmp` 同样来自该项目，画面归上海鹰角网络所有。

| 位置 | 内容 | 来源 |
|---|---|---|
| `src/ui/public/assets/game/module-none.png` | 不带模组图标，64×64 PNG，原样复制，保留透明度 | [ArknightsAssets/ArknightsAssets2](https://github.com/ArknightsAssets/ArknightsAssets2) `cn` 分支 commit `774d93398fc56df29b4f55e406edda1a9df451e9`，`assets/dyn/ui/uniequip/uniequip_clickable_type_icon_item/empty_gray_back.png`；游戏美术归上海鹰角网络科技有限公司所有 |
| `data/covenant_bar_digits.json`、`src/StrongholdAssistant.Tests/TestData/covenant-bar-*.png` | 顶栏层数的二值字形模板与只含顶栏的测试裁片，不含玩家昵称；模板取 2026-11-15 录屏 950、1050、1600 秒和 2026-11-14 录屏 630、900、1800 秒，测试取 2026-11-15 录屏 140、500、1222、550、1785、2015 秒和 2026-11-14 录屏 1200 秒 | 用户于 2026-11-14、2026-11-15 录制的卫戍协议对局；游戏画面与字形归上海鹰角网络所有 |
| `data/field/tier-1.png`、`tier-2.png`、`tier-3.png`、`tier-5.png` | 场上阶数识别模板，40×40 裁剪 | 用户提供的 `prep-ready-1920x1080.png` 游戏截图；游戏美术归上海鹰角网络所有 |
| `data/field/tier-4.png`、`tier-6.png` | 场上 IV、VI 阶识别模板，原图补到 40×40 黑底画布 | [ArknightsAssets/ArknightsAssets2](https://github.com/ArknightsAssets/ArknightsAssets2) `cn` 分支 commit `774d93398fc56df29b4f55e406edda1a9df451e9`，`assets/dyn/ui/autochess/[uc]autochesscommon/chess/common_chess_level/img_chess_level_4.png`、`img_chess_level_6.png`；游戏美术归上海鹰角网络所有 |
| `src/StrongholdAssistant.Tests/TestData/field-*.jpg` | 场上占用及拖动详情测试，5 张录屏帧，只用于测试 | 用户于 2026-11-13 提供的三主题录屏第 47、55.5、57、345、350 秒；游戏画面归上海鹰角网络所有 |
| `src/StrongholdAssistant.Tests/TestData/board-boss-right-*.jpg`、`board-city-r13-expanded.jpg` | 联机首领回合（玩家在右侧）和普通回合的棋盘识别测试，3 张录屏帧，只用于测试 | 用户于 2026-11-14 提供的整局录屏第 40:00、44:00、45:45；游戏画面归上海鹰角网络所有 |
| `src/StrongholdAssistant.Tests/TestData/roster-*-1920x1080.jpg` | 物资调配页（我的干员）识别测试，4 张截图，只用于测试 | 2026-11-15 在用户的游戏里实机截取（任务 162）；游戏画面归上海鹰角网络所有 |
| `src/ui/public/assets/game/bond/` | 盟约图标（文件名为游戏盟约 id 去掉 `Ship`） | [ArknightsAssets/ArknightsAssets2](https://github.com/ArknightsAssets/ArknightsAssets2) `cn` 分支，commit `774d93398fc56df29b4f55e406edda1a9df451e9`，`assets/dyn/ui/autochess/[uc]autochesscommon/arts/bondicon/` |
| `src/ui/public/assets/game/card/` | 商店卡底、等阶框与等阶角标、冻结等 | 同上，`…/[uc]autochessbattle/shop/autochess_shop_card_item/` |
| `src/ui/public/assets/game/card/icon_golden.png` | 精锐标记（图鉴里的精锐状态标签） | 同上，`…/[uc]autochessouter/handbook/act_auto_chess_handbook_state/` |
| `src/ui/public/assets/game/hud/` | 首领框、装饰框等 | 同上，`…/[uc]autochessbattle/hud/autochess_hud_panel/` |
| `src/ui/public/assets/game/hud/s_icon_gold.png` | 资金图标（三颗菱形，白色原图，界面里染成金色） | 同上，`…/[uc]autochesscommon/arts/garrisontypeicon/` |
| `src/ui/public/assets/game/band/` | 禁用标记、禁用提示底板 | 同上，`…/[uc]autochessouter/bandchoose/` |
| `src/ui/public/assets/game/stageinfo/` | 「确认本局信息」页的标题图标、盟约与干员禁用角标、底板等 | 同上，`…/[uc]autochessouter/stageinfo/stageinfogroup/` |
| `src/ui/public/assets/game/skin/terminal/` | 界面皮肤「卫戍协议终端」：盟约圆盘、等阶角标、商店卡框、调度中心等级牌（文件改了名，对应关系见 `src/ui/src/lib/skin.svelte.ts` 里的说明） | 同上，`…/autochessvariantskin/[uc]ac_n_initial/`：`prefabs/bondicon/bond_icon_view_item/`、`arts/chesslevelicon/`、`arts/chessframe/`、`arts/playerdetailshoplevel/` |
| `src/ui/public/assets/game/skin/ursus/` | 界面皮肤「乌萨斯齐心团」，同上几类；另有界面外框：标语条（`flag`）、回合数网格底（`round`）、资金栏长条（`panel`）、标题栏红色斜扫（`sweep`） | 同上，`…/autochessvariantskin/[uc]ac_n_iron/` 的同名目录，以及 `prefabs/banner/round_result/text_bg.png`、`prefabs/group_hud/bg_round.png`、`prefabs/banner/game_start/goal_bg.png`、`prefabs/shop/panel_shop_main/back_left.png` |
| `src/ui/public/assets/game/skin/closure/` | 界面皮肤「『全能的』可露希尔」，同上几类；另有 6 阶「VI」字形遮罩、渐变和角标星星，以及闪光星（`sparkle`）、文件夹（`folder`） | 同上，`…/autochessvariantskin/[uc]ac_s_closur/` 的同名目录，以及 `arts/shop/notag/mask_text_vi.png`、`prefabs/shop/chess_level_6_obj/`、`prefabs/group_hud/round_num_back.png`、`prefabs/shop/panel_shop_main/img_fx_star_outline.png` |

| `.github/readme/` | README 与发版页的图片（含上面的界面素材、盟约图标与界面截图） | 由本项目用上面的素材和界面截图渲染 |
| `src/site/src/assets/shots/` | 官网用的界面截图（助手界面、阵容广场网站，示例数据）和一张游戏商店画面（由测试样图 `shop-terminal-ui100-2000x1125.png` 缩小转为 WebP）；每张另有 `src/site/scripts/shot-sizes.py` 缩出的几档小尺寸 | 界面截图由本项目拍摄，画面里的游戏素材同上；游戏画面归上海鹰角网络所有 |
| `branding/` | 应用图标、QQ 群头像（`icon.png` 1024×1024、`icon.ico` 16～256 多尺寸、`qq-avatar.png` 是徽章放在深色方底上）；v1.0 起换成「高效雅雅」徽章：Q 版霍尔海雅托腮，从青绿发光圆框里探出来，左下角城堡塔徽章 | 本项目用 AI 生图做的同人二创（2026-11-15）：外观参考官方立绘，构图与贴纸画风参考上游「鸿雪·卫戍助手」的图标；角色形象归上海鹰角网络科技有限公司所有 |
| `src/ui/public/brand/icon-128.png`、`src/site/static/`、`src/plaza/site/static/` 的 favicon 与图标、`src/plaza/site/public/mail/logo.png` | 同上图标缩小的版本（网站 favicon、标题栏、关于页、邮件 logo） | 由 `branding/` 的原图缩放 |

## 字体与通用图标

| 位置 | 内容 | 来源与许可 |
|---|---|---|
| `src/ui/src/assets/fonts/MiSans-VF.woff2` | MiSans 可变字体的子集（GB2312 汉字、ASCII、常用标点及界面与数据中出现的字符），转为 woff2 | 小米 [MiSans](https://hyperos.mi.com/font)，© Beijing Xiaomi Mobile Software Co., Ltd.，按《MiSans 字体知识产权许可协议》使用 |
| `src/ui/src/assets/fonts/fusion-pixel/fusion-pixel-12px-latin.woff2` | Fusion Pixel 12px 等宽简中版的基本拉丁区块，可露希尔皮肤的英文装饰字 | [TakWolf/fusion-pixel-font](https://github.com/TakWolf/fusion-pixel-font)，SIL Open Font License 1.1；许可证随包放在 `src/ui/public/licenses/fusion-pixel/`（含其上游 ark-pixel、cubic-11、galmuri 的许可） |
| 界面里的通用图标（设置、关闭、刷新、搜索、星标等，打包进界面代码） | [Remix Icon](https://github.com/Remix-Design/RemixIcon) 4.9.1 | Apache-2.0 |

## 数据与图标

`data/codex_extra.json` 的干员可选技能与模组取自[森空岛卫戍协议公开资源接口](https://zonai.skland.com/web/v1/game/garrison/guide/resource)（2026-11-12）；默认技能与模组取自下述 `activity_table.json` 的 `charShopChessDatas`。由 `scripts/generate-operator-options.mjs` 生成，技能顺序和中文名称保留资源接口原值。

`data/codex_extra.json` 的装备关系、进阶效果与首领血量取自 [ArknightsGameData](https://github.com/Kengxxiao/ArknightsGameData) 国服 `gamedata/excel/activity_table.json`（2026-11-11 数据，`act3autochess/V3_1`），由 `scripts/GenerateCodexExtra` 生成；游戏数据归上海鹰角网络所有。

`data/enemies.json` 的敌人资料（卫戍协议各关卡、活动敌人池、特训敌人附带的全部敌人、悬赏怪，以及首领的名字、等级、攻击方式、基础属性、能力、描述，313 条）取自 [ArknightsGameData](https://github.com/Kengxxiao/ArknightsGameData) 国服 `gamedata/excel/enemy_handbook_table.json`、`gamedata/levels/enemydata/enemy_database.json`、`gamedata/excel/activity_table.json` 和 `gamedata/levels/activities/act3autochess/`、`act1autochess/` 的关卡文件（2026-11-14 数据，悬赏怪 2026-11-15 补入，ArknightsGameData 提交 `6f63c14`）；`data/bounties.json` 的悬赏（135 条：效果名、加的敌人和数量、奖励资金、持续场数、原文）由同一个脚本从 `activity_table.json`（`act3autochess/V3_1`）的 `effectInfoDataDict`、`effectBuffInfoDataDict` 生成；`data/strategy_portraits.json` 是 61 个策略图标（同下 `strategies/`）圆内区域的灰度特征，由 `scripts/generate-strategy-portraits.ps1` 生成，用来认对局里的策略头像；游戏数据归上海鹰角网络所有。界面上的「在 PRTS 查看」只是打开 [PRTS](https://prts.wiki) 对应页面的链接，不收录 PRTS 的内容。

| 位置 | 内容 | 来源 |
|---|---|---|
| `data/skills/`、`data/modules/` 及 `src/ui/public/assets/data/` 下对应目录 | 技能图标与模组类型图标，原尺寸转为无损 WebP | 森空岛公开资源接口指向的鹰角官方 `web.hycdn.cn/arknights/game/assets/char_skill/`、`uniequip/type/icon/`；游戏美术归上海鹰角网络所有，由 `scripts/convert-operator-icons.gd` 转换。官方地址缺失的 `pri-x`、`rpr-y` 两种类型（6 个模组：`uniequip_002_kaitou`、`_blaze2`、`_christ`、`_mantra`、`_nymph`，`uniequip_003_chen2`）取自 [ArknightsAssets/ArknightsAssets2](https://github.com/ArknightsAssets/ArknightsAssets2) `cn` 分支 commit `774d93398fc56df29b4f55e406edda1a9df451e9` 的 `assets/dyn/arts/ui/uniequiptype/`，按官方图标的 82×58 透明画布居中补边后转换 |
| `src/ui/public/assets/data/operators/` | 干员头像（缩放并转为 webp） | [bingyunhe/asp-assistant-arknights](https://github.com/bingyunhe/asp-assistant-arknights) `data/icons/`（取自 PRTS） |
| `data/operator_avatars/*.png` | 头像识别模板（默认头像），141 张 128×128 PNG；与界面头像同源 | 同上 `data/icons/operators/`，由原始 PNG 缩放；游戏美术归上海鹰角网络所有 |
| `data/operator_avatars/<干员名>/` | 头像识别模板（默认、精二、各皮肤头像），463 张 128×128 PNG，由 `scripts/generate-avatar-templates.ps1` 生成 | [yuanyan3060/ArknightsGameResource](https://github.com/yuanyan3060/ArknightsGameResource) `avatar/`，commit `62fe51e`，由原始 PNG 缩放；游戏美术归上海鹰角网络所有 |
| `data/operator_icons/*.webp` | 界面头像的无损 WebP 版（v1.2.8 起，目前是两个 Pith） | 与上一行同源：[yuanyan3060/ArknightsGameResource](https://github.com/yuanyan3060/ArknightsGameResource) `avatar/`，转为无损 WebP；游戏美术归上海鹰角网络所有 |
| `data/sprite-templates.bin.gz` | 场上小人比对模板：卫戍协议 218 名干员全部皮肤的战斗小人（v1.2.8 补「优等生」赠送的 Pith（优等生）`char_616_pithst`，5 阶自选 Pith 改用自己的 `char_509_acast` 外观，追加 16 条），正背面，默认皮肤 Idle 4 个姿势、其余皮肤 1 个，按游戏里同一比例渲染（带脚底原点）的小图加透明蒙版，由 `scripts/sprite-render/` 离线渲染、`scripts/generate-sprite-templates.py` 生成；另含 13 个原型干员外观（玩家没有某名干员时游戏换上的默认干员，如 Sharp、领主·Sharp、预备干员）的正背面 Idle 模板，角色 ID 与 Spine 地址见 `data/prototypes.json` | 小人素材取自 [PRTS](https://prts.wiki) 的 Spine 资源，开发时用 Spine 运行库离线渲染成图片（运行库不进发布包，用户 2026-11-15 定可以发布渲染图）；游戏美术归上海鹰角网络所有 |
| `data/prototypes.json` | 卫戍协议原型干员与逐棋子替代关系（谁被哪个默认干员顶替、排序位置、盟约、备用技能、Spine 地址），由 `scripts/generate-prototypes.cjs` 从 [ArknightsGameData](https://github.com/Kengxxiao/ArknightsGameData/tree/master/zh_CN/gamedata/excel) 的 `activity_table.json`（`act3autochess/V3_1`）、`character_table.json` 生成，输入 SHA-256 见文件 | 游戏数据归上海鹰角网络所有 |
| `data/prototype-bonds/*.png` | 27 个盟约图标，物资调配页识别原型卡下方的盟约用；复用 `src/ui/public/assets/game/bond/`，来源为 ArknightsAssets/ArknightsAssets2 `cn` 分支 commit `774d93398fc56df29b4f55e406edda1a9df451e9` 的 `assets/dyn/ui/autochess/[uc]autochesscommon/arts/bondicon/`，未改图像 | 游戏美术归上海鹰角网络所有 |
| `data/summons.json` | 卫戍协议召唤物目录（鸟笼、全自动造型仪、机动盾牌等 32 个 token：名字、主人、阶数证据、占格、基础阻挡、外观地址），由 `scripts/generate-summons.cjs` 从 [ArknightsGameData](https://github.com/Kengxxiao/ArknightsGameData/tree/master/zh_CN/gamedata/excel) 的 `activity_table.json`（`act3autochess/V3_1`）、`character_table.json` 提取，输入 SHA-256 与角色 ID 见文件 | 游戏数据归上海鹰角网络所有 |
| `data/summon-templates.bin.gz` | 召唤物小人比对模板（独立于干员模板包），25 个资源 300 条，沿用干员模板的比例与脚底原点；资源 ID、覆盖的皮肤和缺口见 `docs/research/truth/291/resources.json` | Spine 取自 [PRTS](https://prts.wiki)（`torappu.prts.wiki/assets/char_spine/<tokenID>/meta.json`），开发时离线渲染，运行库不进发布包；游戏美术归上海鹰角网络所有 |
| `data/skins/*.webp`、`data/roster_templates.json` | 我的干员（任务 163）：当期 218 名干员的皮肤头像（v1.2.8 补 Pith（优等生），起初 217 名 710 套）（96×96 WebP，界面显示用，约 11 MiB），以及物资调配页识别用的皮肤 / 技能 / 模组特征；皮肤编号和名字进 `data/codex_extra.json` | 头像、半身图来自 [yuanyan3060/ArknightsGameResource](https://github.com/yuanyan3060/ArknightsGameResource) `avatar/`、`portrait/`；皮肤编号和名字来自 [ArknightsGameData](https://github.com/Kengxxiao/ArknightsGameData) `skin_table.json`；游戏美术与数据归上海鹰角网络所有 |
| `src/ui/public/assets/data/equipment/`、`bosses/`、`enemy_types/`、`enemies/`、`strategies/` | 装备、首领、敌人类型、敌人（`enemies/`，特训敌人候选和悬赏怪等，原图取自 [yuanyan3060/ArknightsGameResource](https://github.com/yuanyan3060/ArknightsGameResource) 的 `enemy/`，由 `scripts/convert-enemy-icons.gd` 转换）、策略图标（缩放并转为 webp） | 同上（取自 PRTS、SPDatabase） |
| `data/*.json` | 宿主读取的当期数据（干员、盟约、装备、首领、敌人类型、策略等）；`covenant_ids.json` 是盟约中文名到盟约 id 的对照 | [bingyunhe/asp-assistant-arknights](https://github.com/bingyunhe/asp-assistant-arknights) `data/`（取自 PRTS、SPDatabase）；盟约 id 取自 [PRTS](https://prts.wiki) 的 `activity_table.json` |
| `data/enemy_buffs.json` | 卫戍协议联机四个模式（标准 / 险境 / 绝境 / 终极模拟）每回合的敌人累计加成（生命、攻击倍率，攻速加数，移速倍率），由 `scripts/generate-enemy-buffs.mjs` 生成 | 加成参数取自 [Kengxxiao/ArknightsGameData](https://github.com/Kengxxiao/ArknightsGameData) zh_CN `activity_table.json`（commit `6f63c14`）的 `effectBuffInfoDataDict`；各回合发放哪些加成取自[路标档案馆·卫戍协议出怪预览](https://www.lubiao.wiki/tools/garrison-spawn-preview)（经 [CarlosDJY/asp-assistant-arknights](https://github.com/CarlosDJY/asp-assistant-arknights) 的 `data/garrison_spawns.json` 整理，用户 2026-11-14 确认可用）；游戏数据归上海鹰角网络所有 |
| `data/operator_profiles.json`、`data/chess_skill_overrides.json` | 站位推荐（任务 170）：218 名干员（含自选干员、暴躁铁皮和「优等生」赠送的 Pith，v1.2.8 补后一名）普通 / 精锐棋子的战斗面板、全部技能（582 个）和模组（330 个）、潜能、天赋、召唤物和攻击范围，由 `data/build_operator_profiles.py`、`data/talent_effects.py` 生成 | 生成器和最初的 141 份资料改编自 [CarlosDJY/asp-assistant-arknights](https://github.com/CarlosDJY/asp-assistant-arknights) `feature/port-upstream-v1.8.0`（`d3dc2f9`，Apache-2.0）；原始数据取自 [Kengxxiao/ArknightsGameData](https://github.com/Kengxxiao/ArknightsGameData) zh_CN 的 `activity_table.json`、`character_table.json`、`skill_table.json`、`uniequip_table.json`、`battle_equip_table.json`、`range_table.json`、`handbook_team_table.json`；游戏数据归上海鹰角网络所有 |
| `data/garrison_maps.json` | 站位推荐：10 张卫戍协议地图每格能不能放人（地面 / 高台 / 特殊地形）、地形效果，由 `data/build_garrison_terrain.py` 生成 | 改编自 [CarlosDJY/asp-assistant-arknights](https://github.com/CarlosDJY/asp-assistant-arknights)（`d3dc2f9`，Apache-2.0），地图 id 换成本项目的 m01～m12；原始数据取自 [ArknightsGameData](https://github.com/Kengxxiao/ArknightsGameData) 卫戍协议关卡文件；游戏数据归上海鹰角网络所有 |
| `data/garrison_spawns.json` | 站位推荐：联机 1～13 回合的怪组（敌人、数量、出怪间隔、概率）、第 14～15 回合首领波次和 225 种敌人的模拟属性，由 `data/build_garrison_spawns.py` 生成（数据生成于 2026-11-11 00:08）；运行时的敌人加成改用本项目 `data/enemy_buffs.json` | 怪组、波次和部分属性取自[路标档案馆·卫戍协议出怪预览](https://www.lubiao.wiki/tools/garrison-spawn-preview)（用户 2026-11-14 确认可用），经 [CarlosDJY/asp-assistant-arknights](https://github.com/CarlosDJY/asp-assistant-arknights)（`d3dc2f9`）整理；速度、攻击间隔等取自 [ArknightsGameData](https://github.com/Kengxxiao/ArknightsGameData) `enemy_database.json`、`activity_table.json`；游戏数据归上海鹰角网络所有 |
| `data/equipment_effects.json`、`data/strategy_effects.json` | 站位推荐（任务 173）：70 件装备、61 个策略的战斗效果（模拟器能用的加成和触发，以及不模拟的原因），由 `data/build_combat_effects.py` 从 `data/equipment.json`、`data/strategies.json` 的效果文字解析加人工补 | 效果文字同 `data/equipment.json`、`data/strategies.json`，取自 [Kengxxiao/ArknightsGameData](https://github.com/Kengxxiao/ArknightsGameData) `activity_table.json`；游戏数据归上海鹰角网络所有 |
| `data/model/ocr/` | 文字识别模型（`det.onnx`、`rec.onnx`、`keys.txt`），由 MaaFramework 加载 | [bingyunhe/asp-assistant-arknights](https://github.com/bingyunhe/asp-assistant-arknights) `data/model/ocr/`，为 [PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR) 模型（Apache-2.0） |
| `src/StrongholdAssistant.Tests/TestData/field-badge-*.png` | 棋盘角标与占用回归测试，保留棋盘区域 | 玩家提供的 2026-11-17 卫戍协议采集帧，游戏画面归上海鹰角网络所有 |
| `src/StrongholdAssistant.Tests/TestData/*.png` | 测试用的游戏截图（开局信息页、商店、局内画面），只用于测试，不进发布包 | [bingyunhe/asp-assistant-arknights](https://github.com/bingyunhe/asp-assistant-arknights) `src/ASPAssistant.Tests/TestData/`；画面内容归上海鹰角网络所有 |
| `src/StrongholdAssistant.Tests/TestData/user-ban-4-1920x1080.png` | 用户提供的禁用信息页截图，只用于测试 | 用户于 2026-11-11 提供；画面内容归上海鹰角网络所有 |
| `src/StrongholdAssistant.Tests/TestData/prep-ready-1920x1080.png`、`info-scrolled-new-match-1920x1080.png` | 用户提供的准备就绪与第二局滚动信息页截图，只用于测试，不进发布包 | 用户于 2026-11-12 提供；画面内容归上海鹰角网络所有 |
| `src/StrongholdAssistant.Tests/TestData/info-user-window-1648x933.png` | 用户提供的窗口模式信息页截图，含两行附加盟约，只用于测试，不进发布包 | 用户于 2026-11-12 提供；画面内容归上海鹰角网络所有 |
| `src/StrongholdAssistant.Tests/TestData/info-1610-*-1600x1024.png` | 用户提供的高画面信息页顶部及禁用列表截图，只用于测试，不进发布包 | 用户于 2026-11-12 提供；画面内容归上海鹰角网络所有 |
| `src/StrongholdAssistant.Tests/TestData/shop-closure-li-user-1665x1012.jpg` | 用户提供的可露希尔商店截图，只用于测试，不进发布包 | 用户于 2026-11-12 提供；游戏画面内容归上海鹰角网络所有 |
| `src/StrongholdAssistant.Tests/TestData/*-ui100-2000x1125.png` | 用户提供的 UI 100 信息页、禁用头像页和商店截图，只用于测试 | 用户于 2026-11-11 提供；画面内容归上海鹰角网络所有 |
| `src/StrongholdAssistant.Tests/TestData/solo-opening-*.png`、`mumu-solo-*.png` | 用户提供的 PC 单人开局页、MuMu 单人开局及对局详情截图，由 WebP 转为 PNG，只用于测试 | 用户于 2026-11-12 提供；画面内容归上海鹰角网络所有 |
| `src/StrongholdAssistant.Tests/TestData/strategy-selection.jpg`、`strategy-board.jpg` | 用户录屏里的选策略页和对局休整期画面，用来测试认策略；选策略页的昵称和玩家列表已涂黑，只用于测试，不进发布包 | 用户于 2026-11-14 录制；画面内容归上海鹰角网络所有 |
| `src/StrongholdAssistant.Tests/TestData/custom-picks-selection.png`、`custom-picks-none.png` | 用户录屏里的「确定自选编队」页（选择中、倒计时结束未选择），用来测试读自选干员；左侧玩家列表已涂黑，只用于测试，不进发布包 | 用户于 2026-11-15 录制；画面内容归上海鹰角网络所有 |
| `src/StrongholdAssistant.Tests/TestData/custom-picks-solo-selecting-2v2.png`、`custom-picks-solo-final-2v2.png` | 用户截图的单人局「确定自选编队」页（选择中、选完两阶各 2 名），用来测试读自选干员；左侧玩家栏已涂黑，只用于测试，不进发布包 | 用户于 2026-11-15 截图；画面内容归上海鹰角网络所有 |
| `data/sprite-model.onnx`、`data/sprite-model-classes.json` | 场上小人识别模型（MobileNetV3-Small，217 类）和类别表；由 `tools/sprite-model/` 训练：合成图用上面小人模板同源的渲染小人贴在用户录屏截取的空格子背景上，另有用户录屏里少量裁块（程序按拖动面板名字、唯一商店卡自动标注） | 渲染小人取自 [PRTS](https://prts.wiki) 的 Spine 资源、背景与裁块来自用户 2026-11-14/15 录屏，游戏美术归上海鹰角网络所有；以 torchvision 的 ImageNet 预训练权重 `IMAGENET1K_V1` 为起点微调——torchvision 代码为 BSD-3-Clause，预训练权重可能受 ImageNet 数据条款约束（见 torchvision README「Pre-trained Model License」），本项目为非商业用途 |
| `src/StrongholdAssistant.Tests/TestData/sprite-templates-v1.bin.gz` | 旧版（只有默认皮肤）小人模板包里 9 名干员的 57 条模板，测试冻结比对算法用，不进发布包 | 同上 |
| `src/StrongholdAssistant.Tests/TestData/sprite-N24.png`、`sprite-N29.png`、`sprite-N30.png` | 用户录屏里整备区单格裁块（906 秒灰喉、2046 秒霍尔海雅和星极），用来测试小人比对；只含目标人物，只用于测试，不进发布包 | 用户于 2026-11-15 录制；画面内容归上海鹰角网络所有 |
| `src/StrongholdAssistant.Tests/TestData/equipment-panel-2139.png`、`equipment-panel-2298.png`、`equipment-panel-263.png`、`equipment-panel-269.png` | 用户录屏里干员详情面板的装备列表，用来测试读装备；只裁面板一块，不含昵称，只用于测试，不进发布包 | 用户于 2026-11-15 录制；画面内容归上海鹰角网络所有 |
| `src/StrongholdAssistant.Tests/TestData/board-enemy-panel.png`、`shop-wide-ui100.png`、`shop-terminal-frozen-1610.png` | 诊断包中的敌人面板、宽屏商店和冻结商店截图；商店图只保留底部区域，只用于测试，不进发布包 | 用户于 2026-11-13 提供；画面内容归上海鹰角网络所有 |
| `src/ui/src/lib/mock/catalog.json` | 盟约、干员、装备、首领、敌人类型、策略的当期数据 | 同上 `data/*.json`；盟约 id 与激活人数取自 [PRTS](https://prts.wiki) 的 `activity_table.json`（`act3autochess` / `V3_1`） |
