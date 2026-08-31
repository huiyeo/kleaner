# 07 扫描进度推送机制

Type: grilling
Status: resolved
Blocked by: 04

## Question

扫描/清理的进度与取消如何到达 PWA（依赖 04 的 API 契约）：

1. SSE / WebSocket / 轮询三选一：进度事件是单向低频推送，SSE 大概率够用且实现最简；
2. 与服务生命周期的关系：浏览器断连（关标签）时扫描继续跑还是取消？重连后如何重建状态（现 `ScanEngine.Scan` 支持 `CancellationToken`，语义如何映射）；
3. 取消按钮的 API 形态（`CancellationTokenSource` 挂到会话/任务 id 上）。

## Answer

**结论：SSE（fetch 流式）+ 每规则进度事件 + 通用 job 取消端点（扫描/工具箱可取消，清理不可）+ REST 快照与 SSE 增量重建。（2026-08-31 与用户 grilling 确认，四问全按推荐采纳）**

1. **推送通道 = SSE，且走 fetch 流式而非原生 EventSource**：事件单向低频、无需双向，WebSocket 的双向能力用不上（取消走 POST）。**决定性理由**：原生 `EventSource` 不能携带自定义请求头，与工单 03 第 4 层防护（所有请求带 `X-Kleaner-Token` 头）直接冲突——退化为 query 传 token 等于放弃头校验。fetch 流式读取可带 token 头、断线检测与指数退避重连与 03 已定的重连逻辑（提权重启 / Velopack 更新）统一复用，零依赖。SSE 端点：`GET /api/events` 单一多路复用流（按 jobId 区分事件），避免 HTTP/1.1 每主机 6 连接限制在多任务并行时被逐任务流占满。

2. **进度粒度 = 每规则完成事件**：`Kleaner.Core` 的 `ScanEngine.Scan` 新增可选参数 `IProgress<ScanProgress>?`（record：RuleId / FileCount / TotalBytes），规则循环每完成一条上报；默认 null，现有签名与行为零影响。Core 改动按 AGENTS 纪律附 xunit 用例。理由：白名单扫盘是慢操作，29 条规则里单条慢规则一卡几十秒，「只报开始/结束」的反馈空洞在 web 形态下更明显；每文件粒度事件量过大且要动 GlobScanner 内层循环，不值。工具箱操作 v1 只报开始/结束。

3. **取消 = 通用 job 资源，覆盖扫描 + 工具箱，清理明确不可取消**：
   - 所有长操作（主扫描、large-files/duplicates/usage）是服务端 job：`ConcurrentDictionary<jobId, JobRecord>` 持有自己的 `CancellationTokenSource`（照搬现 `ToolboxWindow._cts` 模式）；
   - 取消端点 `POST /api/jobs/{jobId}/cancel` → 202，job 状态机 `running → cancelling → cancelled/completed`；
   - **清理（plan confirm）不在 job 体系内、确认后不可取消**：删除闸已过、整批 `QuarantineManager.Execute` 通常很快，半途虽可还原但徒增暴露面；不为超小概率收益给 Executor 加 token、过一遍删除路径对照。`QuarantineManager.Execute` 签名不动。

4. **断连语义与重连 = 任务与连接彻底解耦，REST 快照 + SSE 增量**：
   - 断连（关标签/网络断）**不取消**后端任务——继承 03 已定的「任务照跑，完成后才进入空闲倒计时」；空闲判定 = 无进行中 job 且无活跃 SSE 连接持续 30s；
   - 事件流只发增量、随时可断、不做事件回放（Last-Event-ID 不实现）：重连后先 `GET /api/jobs`（或按资源 GET）取全量快照，再接 SSE 流，实现最简；
   - job 记录服务端常驻到进程退出，重开浏览器即可取回已完成扫描的结果。

## Comments

- 事实依据：`ScanEngine.Scan` 现为同步方法，按规则/模式/文件逐级检查 `CancellationToken`，无任何进度回调（WPF 仅显示「扫描中…」一行文本）；`QuarantineManager.Execute` 无 token；Toolbox 三个操作各持 `CancellationTokenSource`。
- 实施提醒：`GET /api/events` 必须过 token/Origin 中间件（与普通 API 同一防线）；job 取消后仍占用中的 `CancellationTokenSource` 要在 job 终态时清理，防泄漏。
