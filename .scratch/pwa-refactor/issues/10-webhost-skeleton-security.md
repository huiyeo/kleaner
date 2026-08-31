# 10 WebHost 骨架：工程、进程模型与五层安全中间件

Type: task
Status: resolved

## Task

新工程 `Kleaner.WebHost`（`net10.0-windows`、WinExe、无 UseWPF，引 Core/Executor/Analysis/SpecialOps，进 slnx），按 01/02/03/04 落定：

1. `Program.cs`：`VelopackApp.Build().Run()` → 具名互斥体单实例（二次启动读 service.json 开浏览器后自身退出）→ Kestrel 只绑 `127.0.0.1`（默认固定端口、被占回退随机高端口）→ 写 `%APPDATA%\Kleaner\service.json`（端口 + token）→ 打开浏览器。设为 csproj 默认 StartupObject（`Kleaner.App` 保留为遗留入口）。
2. 五层防护的服务端三层中间件：Host 校验 / Origin 校验 / `X-Kleaner-Token` 启动 token（回环绑定是部署形态，删除闸在 12）。
3. 空闲自动退出：无进行中 job 且无活跃连接持续 30s（实现期可调）→ 进程退出。
4. Job 基础设施留 11；本票出最小冒烟端点（如 `GET /api/health`）。
5. **本票内落定 WebHost 测试策略**（map 悬置项），写入 Comments。

## Acceptance

- 中间件各层有自动化测试：缺 token / 错 Host / 错 Origin 一律 4xx（建议 WebApplicationFactory 集成测，策略以本票落定为准）。
- 二次启动不开新服务而唤起浏览器；空闲 30s 退出可复现。
- `Kleaner.App` 不受影响，双栈均可构建；CLI 安全契约零改动。

## Comments

- 2026-08-31 完成。实现：`src/Kleaner.WebHost`（`net10.0-windows` WinExe 无 WPF，引 Core/Executor/SpecialOps/Analysis，进 slnx，默认 `StartupObject=Kleaner.WebHost.Program`）。`Program.Main` 流程 = `VelopackApp.Build().Run()` → 互斥体单实例（`Local\Kleaner.WebHost.SingleInstance`，含 AbandonedMutex 接管）→ `PortPicker.PickFreePort(45172)`（被占回退随机高端口）→ Kestrel `Listen(IPAddress.Loopback, port)` → 写 `%APPDATA%\Kleaner\service.json`（port+token）→ 开浏览器 → `WaitForShutdown`。二次启动读 service.json 唤起已有实例后自身退出。
- 五层的服务端三层 = `LoopbackSecurityMiddleware`，顺序固定 Host → Origin → Token：Host 恰为 `127.0.0.1:<port>` 否则 400；非 GET/HEAD Origin 必须恰等页面地址否则 403；`/api/*` 必须带 `X-Kleaner-Token`（`CryptographicOperations.FixedTimeEquals` 常量时间比对）否则 401；静态壳豁免 token（token 经启动 URL 注入后由前端随 API 携带）。
- 空闲退出 = `ActivityTracker`（in-flight 请求计数 + job 计数，后者的调用方在工单 11）+ `IdleExitService` 轮询（30s 宽限、5s 间隔，均在 `KleanerWebHostOptions` 可调）。SSE 长连接（工单 11）天然占用一个 in-flight 请求，页面开着不会误退。
- **测试策略落定（map 悬置项）**：新工程 `tests/Kleaner.WebHost.Tests`（xunit + `Microsoft.AspNetCore.TestHost`）。不引入 Mvc.Testing/WebApplicationFactory——WebHost 是无 MVC 的 minimal API，`WebHostAppFactory.Build` 生产与测试共用同一条管线，测试分支仅挂 `UseTestServer`。中间件三层逐层 4xx + 静态壳豁免 + 正常路径共 8 用例全绿；后续工单 11–14 的端点集成测与纯逻辑服务类（如 `CleanPlanService`）测试均进该工程，WebHost 层不再零覆盖。
- 验收核验：`dotnet build Kleaner.slnx -c Release` 全绿（双栈，App 无改动）；`dotnet test` 8/8 + 60/60；运行时冒烟（`.scratch/pwa-refactor/smoke-webhost.sh`）——实例 A 绑 45172、写 service.json，实例 B 打开 `http://127.0.0.1:45172/?token=…` 后自行退出，A 在最后一次请求 30s 后自动退出。CLI 安全契约零改动。
