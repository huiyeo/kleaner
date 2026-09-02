# Wayfinder 地图：PWA 前端重构

`Label: wayfinder:map` · `Status: open`

## Destination

决策全部落定的重构方案（ADR + 规格）：用「本地 ASP.NET Core 最小 API 托管静态 PWA（manifest + service worker，可安装）」取代 WPF 层；`Kleaner.Core` / `Executor` / `Analysis` / `SpecialOps` 与 `Kleaner.ScanCli` 原样保留；CLI 安全契约不动。终点是达到可直接开工实施的清晰度。

## Notes

- 领域文档：术语用 `docs/context.md`；动删除/提权路径前必读 `docs/deletion-path.md`；工程结构见 `docs/architecture.md`。
- 不可协商（继承自 AGENTS.md）：删除类操作必须走 `QuarantineManager` + `HistoryManager`；reparse point 一律排除；Core 引擎改动附 xunit 用例。
- 已定基线（2026-08-31 与用户 grilling 确认）：
  - 痛点 = WPF/XAML 开发体验 + 想要现代 Web UI（a+d）；
  - 路线 = 只换 UI 层（A）：保留全部 C# 引擎与 29 个测试，WPF → 本地 ASP.NET Core 最小 API + JSON API；
  - PWA 形态 = b：manifest + SW 可安装，观感接近桌面 app，不做原生窗口能力；
  - 本次 wayfinder 只规划不施工，终点是决策完毕而非代码完成。
- 规划纪律：单会话至多解决一张工单（research 工单除外）。

## Decisions so far

- [研究：ASP.NET Core 托管 PWA 的发布形态](issues/01-aspnet-publish-pwa.md)：自包含单文件发布可行，wwwroot 静态资源以散文件随包分发；体积与现有 Velopack 自包含包同量级（~90 MB 级），增量来自 ASP.NET Core 共享框架部分。
- [研究：Velopack 发布链路适配](issues/02-velopack-chain.md)：Velopack 对非 WPF 的 WinExe 完全兼容，`VelopackApp.Build().Run()` 照常；安装快捷方式指向同一 exe，由程序自身负责"起服务 + 开浏览器"。
- [决策：进程模型与 API 安全模型](issues/03-process-and-api-security.md)：空闲自动退出（无托盘，宽限期 30s）；具名互斥体单实例 + `service.json` 状态文件（端口+token），二次启动开浏览器后退出；API 五层防护（仅回环 / Host 校验 / Origin 校验 / 启动 token / 删除类需 planId+一次性 confirmToken）；提权走同端口同 token + 前端指数退避自动重连，与 Velopack 更新重启共用一套逻辑。
- [决策：WebHost 工程结构与 API 契约](issues/04-webhost-structure-api.md)：新工程 `Kleaner.WebHost`（net10.0-windows WinExe，无 WPF，引 Core/Executor/Analysis/SpecialOps）；REST 资源风（scan → plan → confirm，删除闸映射为计划确认状态），直接序列化各层 record + WebHost 薄 envelope，不复用 CLI json 形状；纯逻辑下沉——默认勾选策略进 `Kleaner.Core`（`RuleSelectionPolicy`），清理决策流水线留 WebHost 纯函数服务类；过渡期双入口并存，WebHost 为默认 StartupObject；`RuleUpdateService` 维持现状由 WebHost 直调。

- [决策：前端选型与主界面形态](issues/05-frontend-prototype.md)：无构建 vanilla JS + 原生 Web Components（零工具链零依赖，原型即证据；manifest + SW 实施期补齐）；主界面采用变体 C「分类聚合」——左栏环形汇总 + 类别折叠卡 + 确认右滑抽屉。原型资产在 `.scratch/pwa-refactor/prototype/`。

- [决策：功能对等范围与迁移策略](issues/06-parity-and-migration.md)：6/7 窗口进 web v1（Main/Toolbox/Quarantine/History/Settings/Startup；treemap 可降级列表），Advanced 唯一后补且是删 WPF 的前置条件；双栈过渡 + 对等后带 WPF 发布 1 版观察期再删工程；回退靠 Velopack 装回旧版，不做双 exe 出口；CLI 命令面与安全契约冻结、SpecialOps 只进 Web 且维持"只指引不改动"；验收 = 以 deletion-path.md 为基准的删除路径逐项对照清单 + 29 测试全绿。

- [决策：扫描进度推送机制](issues/07-progress-push.md)：SSE（fetch 流式，带 token 头，单一多路复用 /api/events 流）；`ScanEngine.Scan` 加可选 `IProgress<ScanProgress>` 每规则完成上报（Core 改动附测试）；通用 `/api/jobs/{id}/cancel` 取消扫描与工具箱任务、清理确认后不可取消；断连不取消任务（继承 03），重连 = REST 快照 + SSE 增量、不做事件回放。

- [实施工单切分](issues/08-implementation-breakdown.md)：实施期共 13 张工单（09–21），后端 API 轨（09–14）与前端 PWA 轨（15–19）并行，20 发布对等验收 + 观察期版本、21 删 WPF 与文档同步收尾；06 的删除路径对照清单拆挂到 12/13 验收标准、20 全量收口；四个悬置项挂靠具体工单内落定（见下）。
- [实施：Core 层适配](issues/09-core-scan-progress-and-selection.md)：`ScanEngine.Scan` 增可选 `IProgress<ScanProgress>`（取消路径不上报）；`RuleSelectionPolicy.IsDefaultSelectable` 迁自 `RuleRow.MachineVerified` 语义。60/60 测试全绿，双栈构建不受影响。
- [实施：WebHost 骨架](issues/10-webhost-skeleton-security.md)：`Kleaner.WebHost`（net10.0-windows WinExe 无 WPF）落地——Velopack → 互斥体单实例 → 端口回退 → Kestrel 只绑 127.0.0.1 → service.json → 开浏览器 → 空闲 30s 退出；Host/Origin/Token 三层中间件（顺序固定、常量时间比对、静态壳豁免 token）。**测试策略落定**：`tests/Kleaner.WebHost.Tests`（xunit + TestHost，生产与测试共用 `WebHostAppFactory.Build` 管线，不引入 WebApplicationFactory），中间件 8 用例全绿，后续端点集成测与纯逻辑服务类测试均进该工程。
- [实施：Job 体系与 SSE 事件流](issues/11-jobs-events-sse.md)：`JobRegistry`（`ConcurrentDictionary`、job 常驻到进程退出、tracker job 计数接通）+ `JobRecord` 状态机 running→cancelling→cancelled/completed（终态锁内清理 CTS、取消与完成竞态先到先得）；`POST /api/jobs/{id}/cancel`（202/404/409，Cancelling 幂等）；`GET /api/events` 单一多路复用 SSE（per-subscriber channel 广播、`connected` 确认事件、无回放、断连只断连接不取消任务），扫描 job 经 09 的 `IProgress<ScanProgress>` 逐规则发 `scan.progress`、工具箱 v1 只发开始/结束；重连 = `GET /api/jobs` REST 快照 + SSE 增量；测试 seam `ScanExecutor` 注入可控 fake。Core 零改动，测试 68 → 83 全绿（WebHost 8 → 23）。
- [实施：清理主链路 API](issues/12-clean-plan-api.md)：`POST /api/scan`（202 起 11 的 job，同一时间单扫描对齐 GUI）→ `POST /api/plans`（jobId+勾选 id → plan + 一次性 confirmToken；running job 出 plan 409 防绕过新扫描；空勾选/未知 id 400 不复刻 CLI 坑）→ `GET /api/plans/{planId}`（不回显 token）→ `POST /api/plans/{planId}/confirm`（403 错 token / 409 已确认或需提权；token 锁内烧毁先于执行、失败禁重放）；confirm 不进 job 体系，一次 confirm = 一个批次。`CleanPlanService` 纯函数（语义迁自 WPF CleanAsync）、`PlanRegistry`/`PlanRecord`、`ScanResultEnvelope` 薄 envelope 作扫描 job 终态（machineVerified + 枚举字符串化，快照直取）、`HostRuntime`（读 GUI 同款 settings.json）；扫描引擎传隔离区根（验收项）；执行走 `QuarantineManager.Execute`+`HistoryManager`（GUI 主路径不落历史的缺口不复刻）。测试 83 → 104 全绿（WebHost 23 → 44）。

- [实施：隔离区、历史与启动项 API](issues/13-quarantine-history-startup-api.md)：七端点落地（批次列表 / restore / DELETE 单批 / purge、history 只读、startup 列表 / disable / restore）。关键决策：seam 延续 12 模式（History/Quarantine/Startup 三 Provider 进 options，HostRuntime 解析，测试指向临时目录 + `IStartupEnvironment` fake）；新增 `GuardBatchId` 路径安全闸（路由直收客户端 batchId，阻断 `..`/分隔符/盘符逃出隔离区根——GUI 无此攻击面）；启动项 disable 服务端按 id 重新枚举定位（不接受客户端伪造目标位置），HKLM 提权失败 409 + 备份回滚语义不变；manifest 缺失 404 / 损坏 500，`RestoreBatch` 异常外溢坑由 WebHost 层兜住；删除批次 UI 二次确认归工单 17 前端，服务端凭据闸仍只覆盖 clean apply。写路径全部经 `QuarantineManager` / `StartupManager`，Core/Executor 零改动。测试 104 → 119 全绿（WebHost 44 → 59）。

- [实施：设置、规则更新与工具箱 API](issues/14-settings-rules-tools-api.md)：`GET/PUT /api/settings` 与 GUI/CLI 共用同一份三字段 `settings.json`（`QuarantineRoot` / `RuleUpdateUrl` / `RuleUpdateSha512`），WebHost 隔离区根同源解析；`POST /api/rules/update` 只直调既有 `RuleUpdateService`，下载、SHA512 与语义校验链不动。`POST /api/tools/large-files`、`/duplicates`、`/usage` 都进入 11 的 job 体系（202 + jobId，可取消，SSE v1 仅开始/结束），只做 Analysis 只读扫描，绝不产生隔离区或历史副作用。测试 119 → 124 全绿（WebHost 59 → 64）。
- [实施：PWA 壳](issues/15-pwa-shell.md)：`wwwroot` 无构建原生 Web Components 壳（变体 C 左栏/状态栏/六入口），Web SDK `MapStaticAssets` + `MapFallbackToFile`；入口固定内容根且随包复制 `wwwroot`，从任意工作目录启动也能回退 SPA。前端静态 `zh-CN` i18n（离线内嵌回退），token 只存 `sessionStorage`；每次 fetch SSE 连接前先带 token 恢复 `/api/jobs` 快照，401/403 明确报令牌失效，其余以 1–15 秒退避重连；SW `kleaner-shell-v1` 预缓存壳、导航失败回 `index.html`、不缓存 API、用户确认后更新。Chrome 实测安装入口、离线壳与同端口同 token 的自动重连；测试 124 → 127 全绿（WebHost 64 → 67）。
- [实施：Web 主界面（变体 C）](issues/16-web-main-screen.md)：分类折叠卡、勾选汇总、风险/验证/管理员权限标识、每规则 SSE 实时扫描条与取消、计划确认抽屉均接入既有 API；未验证规则默认不勾选，确认仅移入隔离区。新增受保护的 `/api/elevate`：runas 子进程沿用端口与 token，旧实例退出后依赖已有 SSE 退避重连；兼容发布 exe 与 `dotnet app.dll` 启动。测试 127 → 134 全绿（WebHost 67 → 74）。
- [实施：次级 Web 页面](issues/17-web-pages-secondary.md)：隔离区批次列表、整批还原、单批永久删除和 7 天过期清空；只读历史；启用/已禁用启动项的禁用与还原；三项设置均接入既有 API。永久删除操作采用浏览器即时二次确认并明示不可还原；HKLM 启动项通过 16 的提权重连后再由用户确认。测试 134 → 135 全绿（WebHost 74 → 75）。
- [实施：Web 工具箱](issues/18-web-toolbox-page.md)：大文件、重复文件、空间占用三个只读 job 视图接入 SSE 快照与取消；v1 空间占用使用列表而非 treemap。结果不会伪装成规则或进入清理路径；系统大件指引从 `SystemToolGuide` 只读暴露，WebHost 不执行命令。测试 135 → 137 全绿（WebHost 75 → 77）。
- [实施：发布对等验收（观察期 0.2.2）](issues/20-release-parity-acceptance.md)：发布脚本切换为裁剪、压缩的 WebHost 单文件 exe + 散文件 `wwwroot`；开启 JSON 反射兼容以修复裁剪启动。删除路径全量复核完成；真机验证 0.2.1 WPF → 0.2.2 WebHost 安装升级、已安装 WebHost HTTP 200，以及安装器回退至 0.2.1 全部成功。裁剪分析仍有 JSON 反射告警，留作后续 source-generation 改进。

## Not yet specified

（前两项已由工单 15 落定；当前前沿：16 前端轨，无阻塞。）

- 文档同步时机：`architecture.md` / README / `deletion-path.md` 中 WPF 表述的更新 → 工单 21 删 WPF 时一并完成。

## Out of scope

- 换语言整体重写引擎（路线 C 已否决：四道保险是既有资产，重写风险不可接受）。
- 跨平台（Linux/macOS）与移动端适配。
- 远程访问 / 局域网 / 云端形态：只做本机 localhost。
- WPF 与 Web UI 双栈长期并存维护（过渡期除外，见「功能对等范围与迁移策略」）。
- v1 明确不做项（自动定期清理、注册表删除等），维持原 roadmap 边界。
