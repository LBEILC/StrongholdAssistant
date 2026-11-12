# 阵容广场后端

Cloudflare Pages Functions + D1。网站在 `site/`（Vite + Svelte，单独的 npm 包，本地开发 `npm --prefix site run dev`，地址加 `?demo` 看示例阵容）。接口定义见 [shared/api.ts](shared/api.ts)，全部 API 由 `functions/api/[[path]].ts` 处理。站点目前只有建设中占位页。

## 本地开发

需要已安装的 Node.js 和 npm；重新生成 catalog 还需要 Windows 上的 .NET 8 SDK 及宿主构建所需依赖。

在仓库根目录生成当期数据（换赛季或更新数据后重复执行）：

```powershell
# 首次构建宿主要求先有界面产物；已有 src/ui/dist 时可省略这两行。
npm --prefix src/ui ci
npm --prefix src/ui run build
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/export-plaza-catalog.ps1
```

导出程序直接调用 `HostApi.CreateAsync(...).Catalog`，仅投影盟约 id、干员名、装备名、策略 id，保留原顺序。它使用独立临时用户目录，不启动窗口或识别。提交更新后的 `data/catalog.json` 后重新构建、部署后端。可传 `-FullCatalogPath out/plaza-host-catalog.json` 导出完整宿主数据，供指纹比对；完整数据不提交。

进入 `src/plaza`：

```powershell
npm ci
Copy-Item .dev.vars.example .dev.vars
# 在 .dev.vars 填本地使用的机密变量。
npm run migrate:local
npm run build
npm run dev
```

`.dev.vars` 和本地 D1 的 `.wrangler/` 已忽略。普通开发需提供有效的 Resend、Turnstile 配置；测试会替换网络请求，不会实际发信。网站 Cookie 标记为 Secure，本地验证 Cookie 可用 `npm run dev -- --local-protocol=https`，Bearer 接口不依赖 Cookie。

验证命令：

```powershell
npm test
npm run typecheck
npm run build
```

测试用 Miniflare 的真实本地 D1 执行迁移、SQL、唯一约束和触发器；后台 JWT 使用测试临时生成的密钥签名。构建同时生成占位页和编译 Pages Functions。

## 部署步骤

以下云端操作由部署负责人执行。代码中的示例地址和空配置必须先替换，不把机密提交到仓库。

1. 在 Resend 配置发信域名与 DNS 验证，取得 API Key；创建 Turnstile 站点，取得服务端 Secret。前端使用对应 Site Key，第一期后端不含验证小部件。
2. 在 `src/plaza` 登录 Cloudflare 并创建 Pages 项目、D1：

   ```powershell
   npx wrangler login
   npx wrangler pages project create stronghold-plaza --production-branch main
   npx wrangler d1 create stronghold-plaza
   ```

   把返回的数据库 UUID 填入 `wrangler.toml` 的 `database_id`，然后执行远程迁移：

   ```powershell
   npx wrangler d1 migrations apply stronghold-plaza --remote
   ```

3. 设置三个机密（命令交互输入，不写进命令行或文件提交）：

   ```powershell
   npx wrangler pages secret put RESEND_API_KEY --project-name stronghold-plaza
   npx wrangler pages secret put TURNSTILE_SECRET --project-name stronghold-plaza
   npx wrangler pages secret put CODE_PEPPER --project-name stronghold-plaza
   ```

   `CODE_PEPPER` 用独立的高熵随机字符串，长期保存；改变它会让现有验证码和登录凭据失效。`MAIL_FROM`、`ADMIN_EMAILS`、`ACCESS_TEAM_DOMAIN`、`ACCESS_AUD` 含域名和邮箱，同样用 `pages secret put` 设置，不写进 `wrangler.toml`（仓库会公开）。账号下有多个 Cloudflare 账户时，命令前设置环境变量 `CLOUDFLARE_ACCOUNT_ID`。团队域名不带协议，AUD 为下一步创建的 Access 应用标识。按需扩充 `ALLOWED_ORIGINS`；同源请求自动允许。

4. 在 Cloudflare Zero Trust 的 Access Applications 新建 **Self-hosted** 应用。在同一应用内添加网站域名的两个路径 `/admin*` 和 `/api/admin/*`，使用同一个 AUD；Allow 策略只包含管理员邮箱，邮箱同时写入 `ADMIN_EMAILS`。应用的登录方式由部署负责人配置。后台还会验证 JWT 的签名、issuer、audience、到期时间和邮箱白名单。为启用的其他访问域名配置同等保护；预览环境若启用后台，也要配置相应绑定和机密。参见 [Access 应用配置](https://developers.cloudflare.com/cloudflare-one/access-controls/applications/http-apps/self-hosted-public-app/) 和 [JWT 校验](https://developers.cloudflare.com/cloudflare-one/access-controls/applications/http-apps/authorization-cookie/validating-json/)。
5. 先在仓库根目录运行上述 catalog 生成脚本，再到 `src/plaza` 执行：

   ```powershell
   npm ci
   npm --prefix site ci
   npm run build
   npx wrangler pages deploy site/dist --project-name stronghold-plaza --branch main
   ```

   Wrangler 会从当前项目的 `functions/` 一并部署后端。按需要在 Pages 中绑定实际站点域名。命令说明见 [Pages 命令](https://developers.cloudflare.com/workers/wrangler/commands/pages/)，D1 和变量绑定见 [Pages bindings](https://developers.cloudflare.com/pages/functions/bindings/)。

## 实现约定

- 日配额按 UTC 自然日；发码记录验证成功后只作废有效期，仍保留用于配额统计。
- 昵称去首尾空白，按 Unicode 码点计数，以小写键保证不分大小写唯一。
- 作者删除帖子使用 deleted 状态；注销账号物理删除其帖子和点赞，清除邮箱、昵称、验证码记录及全部会话。
- 精选排序把精选帖子置顶，其余按新到旧；热度相同按时间和 id 排序。游标绑定筛选条件。
- 重发同一分享码只更新简介，不恢复可见状态，不增加发布次数。
- 导入设备 id 存 SHA-256 哈希。点赞和导入通过唯一键去重，数据库触发器维护计数。
- 未指定的请求、权限、网络错误使用简短中文提示。上述待设计确认的产品约定已在代码标注 `TODO(design)`。

## 依赖

全部直接依赖锁定版本，以下日期为 npm registry 发布记录：

| 依赖 | 版本 | 发布日期 | 用途 |
| --- | --- | --- | --- |
| jose | 6.1.0 | 2025-08-27 | Access JWT 与远程公钥校验 |
| @cloudflare/workers-types | 4.20251011.0 | 2025-10-11 | Pages、D1 类型 |
| esbuild | 0.25.4 | 2025-05-06 | 测试中打包同一服务端代码与文本资源 |
| miniflare | 4.20251011.0 | 2025-10-21 | 本地真实 D1 测试 |
| typescript | 5.9.3 | 2025-09-30 | 类型检查 |
| wrangler | 4.45.0 | 2025-10-23 | Pages 构建、本地开发和部署命令 |
