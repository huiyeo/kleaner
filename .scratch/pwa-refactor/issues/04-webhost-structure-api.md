# 04 WebHost 工程结构与 API 契约

Type: grilling
Status: resolved

## Question

新 UI 宿主的工程与接口设计决策（HITL，结合 03 的安全模型结论）：

1. **新工程**：建议名 `Kleaner.WebHost`，`net10.0-windows` + `OutputType=WinExe`（无 `UseWPF`），引用 Core/Executor/Analysis/SpecialOps，平级于 `Kleaner.App`/`Kleaner.ScanCli`，进 slnx。是否同意？
2. **端点集合**：从现有 7 窗口功能推导——scan/clean（规则勾选+预览+执行）、toolobox（large-files/duplicates/usage）、quarantine（列表/还原/清空）、history、settings、startup、advanced（WSL/大件/注册表只读）。REST 还是 RPC 风格？`--format json` 的模型能否直接复用为 DTO？
3. **纯逻辑下沉**：现 `Kleaner.App` 中可测试的纯逻辑（`RuleRow` 默认勾选策略、`MainWindowViewModel` 的决策逻辑）搬到哪（`Kleaner.Core`/`Analysis`？新宿主内的服务类？）——继承"可剥离纯逻辑下沉并补测试"的既有约定。
4. **`Program.cs`/入口**：WebHost 的 `Main` 取代 `Kleaner.App/Program.cs` 成为 GUI 入口；`StartupObject` 变更。
5. **规则更新**：`RuleUpdateService` 的 HTTP 下载挪到服务端还是保留现状。

## Answer

**结论：全按推荐采纳。（2026-08-31 与用户 grilling 确认，五问全按推荐）**

1. **新工程 = `Kleaner.WebHost`，含 SpecialOps 引用**：`net10.0-windows` + `OutputType=WinExe`（无 `UseWPF`），引用 Core/Executor/Analysis/SpecialOps，平级于 `Kleaner.App`/`Kleaner.ScanCli`，进 slnx。高级模式（WSL/注册表只读/系统工具引导）是 GUI 独有能力，WebHost 取代 GUI 故继承；CLI 故意不引的边界不变。

2. **API = REST 资源风 + 直接序列化层 record + 薄 envelope**：
   - `POST /api/scan` → `GET /api/plans/{planId}`（dry-run 计划）→ `POST /api/plans/{planId}/confirm`；工单 03 的删除闸天然映射成「计划资源的确认状态」，planId/confirmToken 是资源属性而非请求参数，且防止「拿旧 plan 绕过新扫描」。工具箱为动作式端点（如 `POST /api/tools/large-files`）。
   - **不复用** CLI `--format json` 的临时匿名对象形状；直接序列化 Core 的 `ScanReport`/`RuleScanResult`、Analysis 扫描结果、Executor 执行报告（全是 record）。前端展示缺口（`MachineVerified` 判定、本地化展示名等）由 WebHost 层薄 envelope 类型补齐。CLI 与 API 各自形状，互不迁就。

3. **纯逻辑下沉 = 规则语义进 Core，决策编排留宿主**：
   - 默认勾选策略（`RuleRow.MachineVerified`：verified 以「本机实测」开头或缺省 → 默认勾选）→ `Kleaner.Core` 新增 `RuleSelectionPolicy.IsDefaultSelectable(Rule)`，Core.Tests 覆盖。不放 Analysis——其先例 `DuplicateSelectionPolicy` 是空间分析专属策略，清理勾选与空间分析无关。
   - 清理决策流水线（提权检查 `RequiresElevation`+未提权、零文件过滤、字节数汇总）→ `Kleaner.WebHost` 内纯函数服务类（如 `CleanPlanService`：勾选 id + 扫描报告 → plan/needsElevation/摘要），可测。

4. **入口 = 过渡期双入口并存**：新增 `Kleaner.WebHost/Program.cs`（`VelopackApp.Build().Run()` → 具名互斥体 → service.json → 起 Kestrel → 开浏览器）为 csproj 默认 `StartupObject`；`Kleaner.App/Program.cs` 保留但降级为「遗留验证入口」，工单 06 验收对等后删除。Velopack 打包目标只指向 WebHost 一个 exe。任何时刻可构建可回退。

5. **`RuleUpdateService` = 维持现状**：留在 Core，WebHost 直接调用，前端只需 `POST /api/rules/update` 薄端点。校验链（SHA512 + 语义校验）是安全关，动它要过 `docs/rules.md` 三关；挪新类只会复制逻辑、多一处漂移风险。Core 零外部依赖纪律不受影响（`HttpClient` 是 BCL）。
