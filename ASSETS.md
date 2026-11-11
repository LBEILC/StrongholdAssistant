# 素材来源与归属

本项目使用的《明日方舟》游戏美术与数据，著作权归 **上海鹰角网络科技有限公司（Hypergryph）** 所有。本项目是非商业的玩家自制工具，与鹰角网络无关，未获其认可或授权。权利方提出要求时，我们会移除相关素材。

`LICENSE`（Apache-2.0）只覆盖本项目的代码，不覆盖下列素材。

## 界面素材

| 位置 | 内容 | 来源 |
|---|---|---|
| `src/ui/public/assets/game/bond/` | 盟约图标（文件名为游戏盟约 id 去掉 `Ship`） | [ArknightsAssets/ArknightsAssets2](https://github.com/ArknightsAssets/ArknightsAssets2) `cn` 分支，commit `774d93398fc56df29b4f55e406edda1a9df451e9`，`assets/dyn/ui/autochess/[uc]autochesscommon/arts/bondicon/` |
| `src/ui/public/assets/game/card/` | 商店卡底、等阶框与等阶角标、冻结等 | 同上，`…/[uc]autochessbattle/shop/autochess_shop_card_item/` |
| `src/ui/public/assets/game/hud/` | 首领框、装饰框等 | 同上，`…/[uc]autochessbattle/hud/autochess_hud_panel/` |
| `src/ui/public/assets/game/band/` | 禁用标记、禁用提示底板 | 同上，`…/[uc]autochessouter/bandchoose/` |
| `src/ui/public/assets/game/stageinfo/` | 「确认本局信息」页的标题图标、盟约与干员禁用角标、底板等 | 同上，`…/[uc]autochessouter/stageinfo/stageinfogroup/` |

| `.github/readme/` | README 与发版页的图片（含上面的界面素材、盟约图标与界面截图） | 由本项目用上面的素材和界面截图渲染 |
| `branding/` | 应用图标、QQ 群头像（`icon.png`、`icon.ico`、`qq-avatar.png`），图案用了上面的「独行」盟约图标 | 由本项目渲染，生成页见 `docs/readme/panels.html` |

## 字体与通用图标

| 位置 | 内容 | 来源与许可 |
|---|---|---|
| `src/ui/src/assets/fonts/MiSans-VF.woff2` | MiSans 可变字体的子集（GB2312 汉字、ASCII、常用标点及界面与数据中出现的字符），转为 woff2 | 小米 [MiSans](https://hyperos.mi.com/font)，© Beijing Xiaomi Mobile Software Co., Ltd.，按《MiSans 字体知识产权许可协议》使用 |
| 界面里的通用图标（设置、关闭、刷新、搜索、星标等，打包进界面代码） | [Remix Icon](https://github.com/Remix-Design/RemixIcon) 4.9.1 | Apache-2.0 |

## 数据与图标

| 位置 | 内容 | 来源 |
|---|---|---|
| `src/ui/public/assets/data/operators/` | 干员头像（缩放并转为 webp） | [bingyunhe/asp-assistant-arknights](https://github.com/bingyunhe/asp-assistant-arknights) `data/icons/`（取自 PRTS） |
| `data/operator_avatars/` | 头像识别模板，141 张 128×128 PNG；与界面头像同源 | 同上 `data/icons/operators/`，由原始 PNG 缩放；游戏美术归上海鹰角网络所有 |
| `src/ui/public/assets/data/equipment/`、`bosses/`、`enemy_types/`、`strategies/` | 装备、首领、敌人类型、策略图标（缩放并转为 webp） | 同上（取自 PRTS、SPDatabase） |
| `data/*.json` | 宿主读取的当期数据（干员、盟约、装备、首领、敌人类型、策略等）；`covenant_ids.json` 是盟约中文名到盟约 id 的对照 | [bingyunhe/asp-assistant-arknights](https://github.com/bingyunhe/asp-assistant-arknights) `data/`（取自 PRTS、SPDatabase）；盟约 id 取自 [PRTS](https://prts.wiki) 的 `activity_table.json` |
| `data/model/ocr/` | 文字识别模型（`det.onnx`、`rec.onnx`、`keys.txt`），由 MaaFramework 加载 | [bingyunhe/asp-assistant-arknights](https://github.com/bingyunhe/asp-assistant-arknights) `data/model/ocr/`，为 [PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR) 模型（Apache-2.0） |
| `src/StrongholdAssistant.Tests/TestData/*.png` | 测试用的游戏截图（开局信息页、商店、局内画面），只用于测试，不进发布包 | [bingyunhe/asp-assistant-arknights](https://github.com/bingyunhe/asp-assistant-arknights) `src/ASPAssistant.Tests/TestData/`；画面内容归上海鹰角网络所有 |
| `src/StrongholdAssistant.Tests/TestData/user-ban-4-1920x1080.png` | 用户提供的禁用信息页截图，只用于测试 | 用户于 2026-11-11 提供；画面内容归上海鹰角网络所有 |
| `src/ui/src/lib/mock/catalog.json` | 盟约、干员、装备、首领、敌人类型、策略的当期数据 | 同上 `data/*.json`；盟约 id 与激活人数取自 [PRTS](https://prts.wiki) 的 `activity_table.json`（`act3autochess` / `V3_1`） |
