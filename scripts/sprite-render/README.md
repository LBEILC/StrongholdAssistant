# 战斗小人离线渲染

在仓库根目录运行，Node.js 需支持内置 `fetch`：

```powershell
npm ci --prefix scripts/sprite-render
node scripts/sprite-render/render.cjs --priority-only
node scripts/sprite-render/render.cjs
python scripts/generate-sprite-templates.py --rendered out/214/rendered --size 60 --output out/214/all60.gz
python scripts/generate-sprite-templates.py --rendered out/214/rendered --size 120 --output out/214/all120.gz
```

`--cache` 指定只读的 FieldEval 缓存目录；默认 `out/211/cache`。先渲染商店出现过的人和暴躁铁皮，再处理其余人。`--priority-only` 只处理前一组。`--out` 可更换输出目录，默认 `out/214/rendered`。素材只写入本工作区普通目录 `docs/research/refs/spine/`。

`--names 'Pith,Pith（优等生）'` 只渲染指定产品名，不需要对局缓存。追加模板时可用独立的 `--operators` JSON
列出两名干员及阶数；5 阶 `Pith` 另设 `templateName: "char_509_acast"`，3 阶使用产品名。
这是为了保留旧包中名为 `Pith` 的 `char_612_accast` 原型记录；比对器根据是否声明原型选择对应模板键。

以 PRTS 的 `meta.json` 为皮肤和朝向清单，皮肤展示名通过 `data/codex_extra.json` 映射为产品已有皮肤 ID；默认战斗模型统一使用 `default`，精二服装仍使用默认战斗模型。正面、背面和只有单一“战斗”朝向的资源均保留；不渲染基建。Idle 周期均匀取四帧，所有模型统一为每骨骼单位 0.5 像素，支持区域、变形网格、裁剪附件；输出透明 PNG。此处为离线模型渲染，不复现游戏光照及特效。

`manifest.json` 只将完整成功的干员加入 `operators`，错误记入 `errors` 并返回非零退出码。重跑复用已下载文件和完整渲染记录。修改渲染算法后应使用新的输出目录，避免沿用旧图。

模板生成器通过清单收图，`--default-only` 仅打包默认服装；`--allow-partial` 允许离线评测未完成的干员集合，不能据此替换完整产品包。没有此参数时，缺少任意干员会失败。

`--skin-frames 2` 对每套非默认皮肤的每个朝向保留第 0、2 帧，`--skin-frames 1` 只保留第 0 帧；默认服装仍保留四帧。省略时全部保留。减少帧数后的包必须重新验证准确率与整盘耗时，不能仅凭体积下降替换产品包。

离线渲染没有 Pixi 显示对象代为初始化区域附件，因此需显式更新局部顶点，并按左下、左上、右上、右下读取纹理四角 UV。直接使用加载后的零值顶点会丢失身体部件；对已含旋转的纹理坐标再次应用旧版区域旋转逻辑，也会使 atlas 中旋转打包的部件方向出错。

清单每个朝向记录的 `origins` 以 PNG 文件名为键，保存骨骼原点的像素坐标 `x/y`（画布变换后的世界原点）。清单顶层 `pixelsPerUnit` 记录统一比例；比例不符或旧清单没有原点时会重新渲染。生成器按实际裁切、取整后的缩放尺寸换算原点，不将其强行限制在蒙版内。

SPRT0003 解压布局（均为小端）：魔数 8 字节、归一边长 uint16（60 或 120）、记录数 uint32；每条记录为姓名 UTF-8 长度 uint16 与正文、皮肤 ID UTF-8 长度 uint16 与正文、阶数/宽/高各 uint16、原点 X/Y 各 float32、紧密排列 BGR 三通道、8 位蒙版。原点以裁紧模板左上角为零的像素边界坐标计，水平镜像后 X 为宽度减 X。一个名字取允许皮肤的最高分。比对器继续读取 SPRT0001 / SPRT0002，旧包保持原外框底边搜索口径；新包必须通过两局回放门槛才替换产品包。

工具依赖固定为 `@pixi-spine/runtime-3.8@4.0.6`、`@napi-rs/canvas@0.1.80`，仅用于开发期离线渲染，不进入产品。资源来源与归属见仓库 `ASSETS.md`；游戏美术归上海鹰角网络所有。

原型外观使用 `node scripts/generate-prototypes.cjs <游戏表目录>` 生成 `data/prototypes.json`，
再运行 `node scripts/sprite-render/render.cjs --prototypes --out out/279/rendered`。
该模式不读取对局缓存，只渲染游戏表列出的默认外观。把每个原型的 `tiers` 展开为
`{"operators":[{"name":"Sharp","tier":5}, ...]}` 作为生成器的 `--operators` 输入。
`--append data/sprite-templates.bin.gz --output out/279/combined.bin.gz` 将新记录追加到已有包，
要求相同 SPRT0004 比例；原记录逐字节保留。输出采用独立路径，避免重复追加；通过回归后才替换产品包。

召唤物使用独立的零阶模板包，避免进入干员候选池：

```powershell
node scripts/generate-summons.cjs docs/research/refs/gamedata
node scripts/sprite-render/render.cjs --summons --out out/291/rendered
python scripts/generate-sprite-templates.py --rendered out/291/rendered --operators data/summons.json --allow-partial --output data/summon-templates.bin.gz
```

召唤物清单以 token ID 为模板键、游戏名称为展示名；渲染所有可用皮肤，仍使用统一比例及骨骼原点。
没有 Idle 但有 Loop 的召唤物使用 Loop。资源缺失或解码失败保留在 manifest.errors，不以其他角色代替。
`--allow-partial` 仅用于明确列出的资源缺口，不能表示已覆盖全部召唤物；291 的覆盖和缺口见
`docs/research/truth/291/resources.json`。本包独立于干员包，原干员模板不重写。

SPRT0004 在分辨率 uint16 后增加 float32 像素/骨骼单位，其余记录布局同 SPRT0003。固定比例清单在 `--size 60` 时整体缩小一半，120 时保持原尺寸，禁止按单张外框归一。旧清单仍生成 SPRT0003。新包匹配必须显式传入以 1920×1080 为口径的 `screenPixelsPerUnit`，可通过 `searchAnchor` 指定裁块内骨骼原点；尺度只取标定值的 95%、100%、105%，位置搜索为 ±4 像素。旧包继续使用冻结参数。223 尚未通过两局门槛，产品包仍保留原版；标定参数和试验包留在 `out/223/`，不得直接用于发布。
