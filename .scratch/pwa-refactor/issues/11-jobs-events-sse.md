# 11 Job 体系与 SSE 事件流

Type: task
Status: resolved
Blocked by: 09, 10

## Task

按 07 落定：

1. Job 基础设施：`ConcurrentDictionary<jobId, JobRecord>`，各持 `CancellationTokenSource`（照搬 `ToolboxWindow._cts` 模式），状态机 `running → cancelling → cancelled/completed`；终态清理 CTS 防泄漏。
2. `POST /api/jobs/{jobId}/cancel` → 202；job 记录服务端常驻到进程退出（重开浏览器可取回已完成结果）。
3. `GET /api/events` 单一多路复用 SSE 流（fetch 流式、过 token/Origin 中间件、按 jobId 发增量）；扫描事件粒度 = 每规则完成（用 09 的 `IProgress<ScanProgress>`），工具箱操作 v1 只发开始/结束。
4. 断连不取消任务；不做事件回放（无 Last-Event-ID）；重连 = REST 快照 + SSE 增量。

## Acceptance

- job 取消语义有自动化测试（取消后状态与结果可见）；SSE 流带 token 校验（无 token 4xx）。
- 扫描 job 逐规则发事件（依赖 09）；取消端点对清理类不可达（确认闸语义，与 12 对齐）。
- 断开 SSE 连接后任务继续跑完，重开连接可从 REST 快照取回结果。

## Comments

- 2026-08-31 接管：本票此前被认领但停滞无产出（WebHost 两个目录均无 Job/SSE 新文件），本代理直接接管施工。
- 2026-08-31 完成。实现（均在 `src/Kleaner.WebHost`，Core 零改动）：`JobRegistry`（`ConcurrentDictionary<jobId, JobRecord>`，记录常驻到进程退出；job 计数接 `IActivityTracker`，补齐工单 10 留下的调用方缺口）；`JobRecord` 每票 `CancellationTokenSource`（照搬 ToolboxWindow._cts 模式），状态机 running → cancelling → cancelled/completed，**终态在锁内 Dispose CTS 防泄漏**，取消与终态竞态先到先得（取消请求先到但工作已完成则落 Completed、结果保留）。`ScanJobService` 调 09 的 `ScanEngine.Scan`，`IProgress<ScanProgress>` 每规则完成桥接为 `scan.progress` 事件；`JobEventBus`（per-subscriber unbounded channel）广播到 `GET /api/events` 单一多路复用 SSE 流——工具箱 v1 只发 `job.started` / `job.completed` / `job.cancelled`，扫描另加每规则 `scan.progress`。
- 端点：`GET /api/jobs`（全量快照）、`GET /api/jobs/{id}`（单 job 快照，终态后可取回 ScanReport 结果）、`POST /api/jobs/{id}/cancel`（202 受理 Running→Cancelling、Cancelling 幂等；404 未知；409 终态）、`GET /api/events`（fetch 流式；无 token 401；连接先发 `connected` 确认事件；无 Last-Event-ID 回放，重连 = REST 快照 + SSE 增量）。SSE 过既有 Host/Origin/Token 中间件，长连接占一个 in-flight 请求；断连只结束连接、绝不取消任务（07 语义）。清理类（plan confirm）不进 job 体系，取消端点对它天然不可达（12 的确认闸）。测试 seam：`KleanerWebHostOptions.ScanExecutor` 注入慢速/可控 fake。
- 测试：新增 15 用例全绿——`JobRegistryTests`（状态机/终态清理/幂等取消/tracker 计数）、`JobEventBusTests`（广播/退订/无回放）、`JobsApiTests`（404/409/202、取消后状态与 null 结果可见、列表快照、终态结果可取回）、`SseEventsTests`（无 token 401、connected→started→逐规则 progress→completed 序列、断连后任务跑完且 REST 快照可取回、双 job 多路复用按 jobId 不串流、取消事件上流）。`dotnet build Kleaner.slnx -c Release` 全绿（双栈，App 无改动）；`dotnet test` Core 60/60 + WebHost 23/23（基线 68 → 83）。未 commit。
