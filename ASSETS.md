# 素材来源与归属

本项目使用的《明日方舟》游戏美术与数据，著作权归 **上海鹰角网络科技有限公司（Hypergryph）** 所有。本项目是免费的玩家自制工具，与鹰角网络无关，未获其认可或授权。权利方提出要求时，我们会移除相关素材。

`LICENSE`（Apache-2.0）只覆盖本项目的代码，不覆盖下列素材。

## 界面素材

`data/garrison_routes.json` 的敌人路线由 `scripts/generate-garrison-routes.mjs` 从 [ArknightsGameData](https://github.com/Kengxxiao/ArknightsGameData) 国服 `gamedata/levels/activities/act3autochess/` 的关卡文件和 `activity_table.json`（`act3autochess/V3_1`）生成（2026-11-13）；游戏数据归上海鹰角网络所有。`data/garrison_board_refs.json` 的棋盘镜头和各地图颜色样本改编自 [bingyunhe/asp-assistant-arknights](https://github.com/bingyunhe/asp-assistant-arknights) dev 分支（Apache-2.0），样本取自游戏画面。测试用的 4 张棋盘截图 `src/StrongholdAssistant.Tests/TestData/board-*.bmp` 同样来自该项目，画面归上海鹰角网络所有。

| 位置 | 内容 | 来源 |
|---|---|---|
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
| `branding/` | 应用图标、QQ 群头像（`icon.png`、`icon.ico`、`qq-avatar.png`），图案用了上面的「独行」盟约图标 | 由本项目渲染，生成页见 `docs/readme/panels.html` |

## 字体与通用图标

| 位置 | 内容 | 来源与许可 |
|---|---|---|
| `src/ui/src/assets/fonts/MiSans-VF.woff2` | MiSans 可变字体的子集（GB2312 汉字、ASCII、常用标点及界面与数据中出现的字符），转为 woff2 | 小米 [MiSans](https://hyperos.mi.com/font)，© Beijing Xiaomi Mobile Software Co., Ltd.，按《MiSans 字体知识产权许可协议》使用 |
| `src/ui/src/assets/fonts/fusion-pixel/fusion-pixel-12px-latin.woff2` | Fusion Pixel 12px 等宽简中版的基本拉丁区块，可露希尔皮肤的英文装饰字 | [TakWolf/fusion-pixel-font](https://github.com/TakWolf/fusion-pixel-font)，SIL Open Font License 1.1；许可证随包放在 `src/ui/public/licenses/fusion-pixel/`（含其上游 ark-pixel、cubic-11、galmuri 的许可） |
| 界面里的通用图标（设置、关闭、刷新、搜索、星标等，打包进界面代码） | [Remix Icon](https://github.com/Remix-Design/RemixIcon) 4.9.1 | Apache-2.0 |

## 数据与图标

`data/codex_extra.json` 的干员可选技能与模组取自[森空岛卫戍协议公开资源接口](https://zonai.skland.com/web/v1/game/garrison/guide/resource)（2026-11-12）；默认技能与模组取自下述 `activity_table.json` 的 `charShopChessDatas`。由 `scripts/generate-operator-options.mjs` 生成，技能顺序和中文名称保留资源接口原值。

`data/codex_extra.json` 的装备关系、进阶效果与首领血量取自 [ArknightsGameData](https://github.com/Kengxxiao/ArknightsGameData) 国服 `gamedata/excel/activity_table.json`（2026-11-11 数据，`act3autochess/V3_1`），由 `scripts/GenerateCodexExtra` 生成；游戏数据归上海鹰角网络所有。

| 位置 | 内容 | 来源 |
|---|---|---|
| `data/skills/`、`data/modules/` 及 `src/ui/public/assets/data/` 下对应目录 | 技能图标与模组类型图标，原尺寸转为无损 WebP | 森空岛公开资源接口指向的鹰角官方 `web.hycdn.cn/arknights/game/assets/char_skill/`、`uniequip/type/icon/`；游戏美术归上海鹰角网络所有，由 `scripts/convert-operator-icons.gd` 转换。官方地址缺失的 `pri-x`、`rpr-y` 两种类型（6 个模组：`uniequip_002_kaitou`、`_blaze2`、`_christ`、`_mantra`、`_nymph`，`uniequip_003_chen2`）取自 [ArknightsAssets/ArknightsAssets2](https://github.com/ArknightsAssets/ArknightsAssets2) `cn` 分支 commit `774d93398fc56df29b4f55e406edda1a9df451e9` 的 `assets/dyn/arts/ui/uniequiptype/`，按官方图标的 82×58 透明画布居中补边后转换 |
| `src/ui/public/assets/data/operators/` | 干员头像（缩放并转为 webp） | [bingyunhe/asp-assistant-arknights](https://github.com/bingyunhe/asp-assistant-arknights) `data/icons/`（取自 PRTS） |
| `data/operator_avatars/*.png` | 头像识别模板（默认头像），141 张 128×128 PNG；与界面头像同源 | 同上 `data/icons/operators/`，由原始 PNG 缩放；游戏美术归上海鹰角网络所有 |
| `data/operator_avatars/<干员名>/` | 头像识别模板（默认、精二、各皮肤头像），463 张 128×128 PNG，由 `scripts/generate-avatar-templates.ps1` 生成 | [yuanyan3060/ArknightsGameResource](https://github.com/yuanyan3060/ArknightsGameResource) `avatar/`，commit `62fe51e`，由原始 PNG 缩放；游戏美术归上海鹰角网络所有 |
| `src/ui/public/assets/data/equipment/`、`bosses/`、`enemy_types/`、`enemies/`、`strategies/` | 装备、首领、敌人类型、特训敌人候选（`enemies/`，由 `scripts/convert-enemy-icons.gd` 转换）、策略图标（缩放并转为 webp） | 同上（取自 PRTS、SPDatabase） |
| `data/*.json` | 宿主读取的当期数据（干员、盟约、装备、首领、敌人类型、策略等）；`covenant_ids.json` 是盟约中文名到盟约 id 的对照 | [bingyunhe/asp-assistant-arknights](https://github.com/bingyunhe/asp-assistant-arknights) `data/`（取自 PRTS、SPDatabase）；盟约 id 取自 [PRTS](https://prts.wiki) 的 `activity_table.json` |
| `data/model/ocr/` | 文字识别模型（`det.onnx`、`rec.onnx`、`keys.txt`），由 MaaFramework 加载 | [bingyunhe/asp-assistant-arknights](https://github.com/bingyunhe/asp-assistant-arknights) `data/model/ocr/`，为 [PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR) 模型（Apache-2.0） |
| `src/StrongholdAssistant.Tests/TestData/*.png` | 测试用的游戏截图（开局信息页、商店、局内画面），只用于测试，不进发布包 | [bingyunhe/asp-assistant-arknights](https://github.com/bingyunhe/asp-assistant-arknights) `src/ASPAssistant.Tests/TestData/`；画面内容归上海鹰角网络所有 |
| `src/StrongholdAssistant.Tests/TestData/user-ban-4-1920x1080.png` | 用户提供的禁用信息页截图，只用于测试 | 用户于 2026-11-11 提供；画面内容归上海鹰角网络所有 |
| `src/StrongholdAssistant.Tests/TestData/prep-ready-1920x1080.png`、`info-scrolled-new-match-1920x1080.png` | 用户提供的准备就绪与第二局滚动信息页截图，只用于测试，不进发布包 | 用户于 2026-11-12 提供；画面内容归上海鹰角网络所有 |
| `src/StrongholdAssistant.Tests/TestData/info-user-window-1648x933.png` | 用户提供的窗口模式信息页截图，含两行附加盟约，只用于测试，不进发布包 | 用户于 2026-11-12 提供；画面内容归上海鹰角网络所有 |
| `src/StrongholdAssistant.Tests/TestData/info-1610-*-1600x1024.png` | 用户提供的高画面信息页顶部及禁用列表截图，只用于测试，不进发布包 | 用户于 2026-11-12 提供；画面内容归上海鹰角网络所有 |
| `src/StrongholdAssistant.Tests/TestData/shop-closure-li-user-1665x1012.jpg` | 用户提供的可露希尔商店截图，只用于测试，不进发布包 | 用户于 2026-11-12 提供；游戏画面内容归上海鹰角网络所有 |
| `src/StrongholdAssistant.Tests/TestData/*-ui100-2000x1125.png` | 用户提供的 UI 100 信息页、禁用头像页和商店截图，只用于测试 | 用户于 2026-11-11 提供；画面内容归上海鹰角网络所有 |
| `src/StrongholdAssistant.Tests/TestData/solo-opening-*.png`、`mumu-solo-*.png` | 用户提供的 PC 单人开局页、MuMu 单人开局及对局详情截图，由 WebP 转为 PNG，只用于测试 | 用户于 2026-11-12 提供；画面内容归上海鹰角网络所有 |
| `src/ui/src/lib/mock/catalog.json` | 盟约、干员、装备、首领、敌人类型、策略的当期数据 | 同上 `data/*.json`；盟约 id 与激活人数取自 [PRTS](https://prts.wiki) 的 `activity_table.json`（`act3autochess` / `V3_1`） |
