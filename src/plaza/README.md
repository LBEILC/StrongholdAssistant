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

`.dev.vars` 和本地 D1 的 `.wrangler/` 已忽略。普通开发需提供有效的发信服务、Turnstile 配置；阿里云三项配置齐全时使用 DirectMail，否则使用 Resend。需要本地使用阿里云时，在 `.dev.vars` 添加下文同名三项变量。测试会替换网络请求，全部使用假凭据，不会实际发信。网站 Cookie 标记为 Secure，本地验证 Cookie 可用 `npm run dev -- --local-protocol=https`，Bearer 接口不依赖 Cookie。

验证命令：

```powershell
npm test
npm run typecheck
npm run build
```

测试用 Miniflare 的真实本地 D1 执行迁移、SQL、唯一约束和触发器；后台 JWT 使用测试临时生成的密钥签名。构建同时生成占位页和编译 Pages Functions。

## 部署步骤

以下云端操作由部署负责人执行。代码中的示例地址和空配置必须先替换，不把机密提交到仓库。

1. 按下节准备阿里云邮件推送；切换期间也可继续在 Resend 配置发信域名与 DNS 验证，取得 API Key。创建 Turnstile 站点，取得服务端 Secret。前端使用对应 Site Key，第一期后端不含验证小部件。
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

3. 设置机密（命令交互输入，不写进命令行或文件提交）：

   ```powershell
   npx wrangler pages secret put TURNSTILE_SECRET --project-name stronghold-plaza
   npx wrangler pages secret put CODE_PEPPER --project-name stronghold-plaza
   npx wrangler pages secret put IP_SECRET --project-name stronghold-plaza
   npx wrangler pages secret put ALIYUN_DM_ACCESS_KEY_ID --project-name stronghold-plaza
   npx wrangler pages secret put ALIYUN_DM_ACCESS_KEY_SECRET --project-name stronghold-plaza
   npx wrangler pages secret put ALIYUN_DM_ACCOUNT --project-name stronghold-plaza
   ```

   `ALIYUN_DM_ACCESS_KEY_ID` / `ALIYUN_DM_ACCESS_KEY_SECRET` 为下节 RAM 用户的 AccessKey，`ALIYUN_DM_ACCOUNT` 为发信地址。三项均非空才启用阿里云，缺少任意一项仍走 Resend；切换时应补齐三项。使用 Resend 时另执行 `npx wrangler pages secret put RESEND_API_KEY --project-name stronghold-plaza` 和 `npx wrangler pages secret put MAIL_FROM --project-name stronghold-plaza`。阿里云发信失败不会自动转发到 Resend，也不重试。

   `IP_SECRET` 使用独立的高熵随机密钥，用于按用途和 UTC 日期生成 IP 的 HMAC；部署新代码前设置该密钥并应用 `0005_server_privacy.sql`（增加来源列及导入唯一索引，清空旧资金汇总和限额记录，旧样本没有来源，按每个编号各算一个来源，现有资金表不受影响）。迁移后立刻部署新代码：旧代码写资金样本和导入记录时列数对不上，中间这段时间资金上报和导入计数会失败。

   `CODE_PEPPER` 用独立的高熵随机字符串，长期保存；改变它会让现有验证码和登录凭据失效。`MAIL_FROM`、`ADMIN_EMAILS`、`ACCESS_TEAM_DOMAIN`、`ACCESS_AUD` 含域名和邮箱，同样用 `pages secret put` 设置，不写进 `wrangler.toml`（仓库会公开）。账号下有多个 Cloudflare 账户时，命令前设置环境变量 `CLOUDFLARE_ACCOUNT_ID`。团队域名不带协议，AUD 为下一步创建的 Access 应用标识。按需扩充 `ALLOWED_ORIGINS`；同源请求自动允许。

4. 在 Cloudflare Zero Trust 的 Access Applications 新建 **Self-hosted** 应用。在同一应用内添加网站域名的两个路径 `/admin*` 和 `/api/admin/*`，使用同一个 AUD；Allow 策略只包含管理员邮箱，邮箱同时写入 `ADMIN_EMAILS`。应用的登录方式由部署负责人配置。后台还会验证 JWT 的签名、issuer、audience、到期时间和邮箱白名单。为启用的其他访问域名配置同等保护；预览环境若启用后台，也要配置相应绑定和机密。参见 [Access 应用配置](https://developers.cloudflare.com/cloudflare-one/access-controls/applications/http-apps/self-hosted-public-app/) 和 [JWT 校验](https://developers.cloudflare.com/cloudflare-one/access-controls/applications/http-apps/authorization-cookie/validating-json/)。
   经 ESA 回源时，在 Pages 设置加密变量 `EDGE_SECRET`，与 ESA 回源头 `X-Plaza-Edge` 完全一致，并开启托管转换 `ali-real-client-ip`；后台使用受 Access 保护的 `sa-admin.lubeiluchen.com`（上述两个路径配置在此域名），`sa.lubeiluchen.com` 本身不再经过 Access。

5. 先在仓库根目录运行上述 catalog 生成脚本，再到 `src/plaza` 执行：

   ```powershell
   npm ci
   npm --prefix site ci
   npm run build
   npx wrangler pages deploy site/dist --project-name stronghold-plaza --branch main
   ```

   Wrangler 会从当前项目的 `functions/` 一并部署后端。按需要在 Pages 中绑定实际站点域名。命令说明见 [Pages 命令](https://developers.cloudflare.com/workers/wrangler/commands/pages/)，D1 和变量绑定见 [Pages bindings](https://developers.cloudflare.com/pages/functions/bindings/)。

## 阿里云邮件推送准备

以下由用户在阿里云和 DNS 控制台完成，工程实现和测试不读取真实凭据、不操作账号或发送真实邮件。

1. 在中国内地（杭州）开通邮件推送按量付费，确认账号可用及发信额度。
2. 添加发信域名 `mail.sa.lubeiluchen.com`，按控制台给出的记录配置 SPF（TXT）、DKIM、DMARC（TXT）及 MX，并完成域名验证。记录名、值和 MX 优先级以该域名的控制台配置为准，不照抄其他域名的值。
3. 创建发信地址 `noreply@mail.sa.lubeiluchen.com`，邮件类型选择「触发邮件」，确认地址可用；将完整地址填入 `ALIYUN_DM_ACCOUNT`。本实现不启用回信地址。
4. 创建专用 RAM 用户及 AccessKey，只授予本接口所需的 `dm:SingleSendMail` 权限；不用主账号 AccessKey。按 [SingleSendMail 授权信息](https://help.aliyun.com/zh/direct-mail/api-dm-2015-11-23-singlesendmail)，该操作使用 `Resource: "*"`，可使用以下自定义策略：

   ```json
   { "Version": "1", "Statement": [{ "Effect": "Allow", "Action": ["dm:SingleSendMail"], "Resource": "*" }] }
   ```

5. 按上节 `wrangler pages secret put` 命令录入三项机密，勿写入源码、部署配置或提交到仓库。

请求固定使用杭州 HTTPS 端点、`Version=2015-11-23` 和 RPC 1.0 HMAC-SHA1 签名，使用 Web Crypto，无额外 SDK 依赖。签名测试采用[官方固定示例](https://help.aliyun.com/zh/direct-mail/signature)。

错误映射：HTTP 429、`InvalidQuota`、`Throttling`、`Throttling.User`、`Throttling.Api` 返回 `429 mail_quota`，沿用现有额度提示；其他失败（包括网络或响应解析失败）返回 `502 mail_failed`。阿里云[公共错误码](https://help.aliyun.com/zh/direct-mail/api-dm-2015-11-23-errorcodes)中的 `InvalidSendMail.Spam` 同时可能表示限频、额度或内容反垃圾，无法仅凭该码区分，按一般失败处理。限流也沿用现有「今天能发的验证码邮件用完了」提示。

## 实现约定

- 日配额按 UTC 自然日；发码记录验证成功后只作废有效期，仍保留用于配额统计。
- 昵称去首尾空白，按 Unicode 码点计数，以小写键保证不分大小写唯一。
- 作者删除帖子使用 deleted 状态；注销账号物理删除其帖子和点赞，清除邮箱、昵称、验证码记录及全部会话。
- 精选排序把精选帖子置顶，其余按新到旧；热度相同按时间和 id 排序。游标绑定筛选条件。
- 重发同一分享码只更新简介，不恢复可见状态，不增加发布次数。
- 导入设备 id 存 SHA-256 哈希，同一设备或同一当天来源对同一帖子只计一次。资金及导入的来源存按用途、UTC 日期隔离的 IP HMAC；资金汇总要求至少三个编号及三个来源。验证码和密码登录记录在发码或密码登录时清理超过 30 天的行，资金限额记录在汇总时清理超过两天的行。数据库触发器维护点赞和导入计数。
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
