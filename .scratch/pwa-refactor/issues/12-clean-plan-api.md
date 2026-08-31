# 12 清理主链路 API：scan → plan → confirm

Type: task
Status: resolved
Blocked by: 09, 10, 11

## Task

按 03/04/07 落定删除主链路（REST 资源风，删除闸映射为计划确认状态）：

1. `POST /api/scan`（job，走 11）→ `GET /api/plans/{planId}`（dry-run 计划）→ `POST /api/plans/{planId}/confirm`（执行）。
2. `CleanPlanService` 纯函数服务类（WebHost 内、可测）：勾选 id + 扫描报告 → plan / needsElevation / 摘要（提权检查 `RequiresElevation`+未提权、零文件过滤、字节汇总）。
3. 删除闸：confirm 必须携带此前预览返回的一次性 `confirmToken`；确认后不可取消（不进 job 取消体系，`QuarantineManager.Execute` 签名不动）。
4. 执行接线 `QuarantineManager.Execute` + `HistoryManager`；提权走 03 的同端口重启 + 前端重连约定（本票落 API 侧语义）。
5. 直接序列化 Core/Executor 的 record + 薄 envelope（补 `MachineVerified` 判定、本地化展示名等展示缺口），不复用 CLI json 形状。

## Acceptance

删除路径对照清单（基准 `docs/deletion-path.md`）本票相关项逐条验收：

- [ ] 一次 confirm = 一个批次，batchId / manifest 结构不变，落 `clean` 历史
- [ ] 被占用 / 无权限文件跳过并计入 `Skipped`，报告可见，绝不强制删除
- [ ] 无预览凭据调 confirm 一律拒绝；`confirmToken` 一次性
- [ ] 确认后无取消端点可达
- [ ] 白名单外路径零触碰（只遍历 `set.Rules` 且 `r.Enabled`；年龄阈值 / `keepNewest` 语义不变）
- [ ] 扫描传 `quarantineRoot` 排除隔离区自身（GUI 现状语义，勿复刻 CLI 的坑）

## Comments

- 2026-08-31 完成。端点（WebHostAppFactory，全部过既有五层防护中间件）：`POST /api/scan`（加载生效规则集 → 11 的扫描 job，202；规则加载失败 500、已有扫描进行中 409——对齐 GUI「扫描中不可再扫」）→ `POST /api/plans`（jobId + 勾选 id → dry-run 计划，201 返回 planId + 一次性 confirmToken；job 未完成 409——防拿旧 plan 绕过新扫描；空勾选集 / 未知规则 id 400——不复刻 CLI「--rule 缺省静默成功」的坑）→ `GET /api/plans/{planId}`（不回显 token）→ `POST /api/plans/{planId}/confirm`（token 校验 → 锁内烧毁 → 执行，403 错 token / 409 已确认或需提权 / 404 未知）。confirm 不进 job 体系：确认后无取消端点可达，一次 confirm = 一个批次。
- 实现（均在 `src/Kleaner.WebHost`，Core/Executor 零改动）：`CleanPlanService` 纯函数服务类（提权检查在零文件过滤之前、语义逐条迁自 `MainWindowViewModel.CleanAsync`）；`PlanRegistry`/`PlanRecord`（confirmToken 常量时间比对、校验与烧毁锁内原子、**烧毁先于执行**——执行失败也禁止凭据重放，错 token 不烧毁）；`ScanResultEnvelope` 薄 envelope（machineVerified 经 `RuleSelectionPolicy`、category/risk 字符串化）作为扫描 job 终态结果，前端经 `GET /api/jobs/{id}` 快照直接取回，无需另开 /api/scans 端点；`HostRuntime`（规则集 / 隔离区根 / 提权判定解析，与 GUI 读同一个 settings.json，AppSettings/Helpers 挂在 WPF 工程故做最小同语义实现）。`ScanJobService` 改为每次运行按 `HostRuntime.ResolveQuarantineRoot` 构造 `ScanEngine`——扫描排除隔离区自身（验收项，勿复刻 CLI 的坑）；确认执行走 `QuarantineManager.Execute` + `HistoryManager`（落 clean 历史，GUI 主清理路径反而不落历史的缺口 Web 端不复刻）。规则库 `rules.v1.json` 随 WebHost 包分发（同 Kleaner.App 做法）。
- API 侧提权语义：plan 创建时按 `RequiresElevation` + 提权探测标 `needsElevation`；confirm 一律 409 拒绝（本进程内不执行）。提权重启 + 前端重连是工单 03 的既有约定，端点随后续工单落。
- 测试：新增 21 用例全绿——`CleanPlanServiceTests` 7（零文件过滤 / 摘要 / 扫描序 / 提权判定含零文件提权规则 / 未知 id / 空勾选）、`PlanRegistryTests` 5（token 只在创建视图出现 / 一次性烧毁 / 错 token 不烧毁 / 未知 / 视图摘要）、`CleanPlanApiTests` 9（全链路含白名单外零触碰与 token 重放 409、错 token 后仍可用、未知 plan 404、未知 job 404、running job 出 plan 409、空勾选 400、未知规则 400、需提权 409 且零执行、envelope 的 machineVerified 与字符串枚举）。全链路 fake：`RuleSetProvider` / `CleanExecutor` / `ElevationProbe` 进 `KleanerWebHostOptions`，不触真实文件系统。`dotnet test` Core 60/60 + WebHost 44/44（83 → 104）。未 commit。
