# 删除路径与安全契约

**先记住一条：普通清理从不永久删除。** 其唯一入口是 `QuarantineManager.Execute(CleanupPlan)` 中的 `File.Move`；`CleanupPlan` 只能由 Core 根据白名单扫描结果构造，并在移动前复验。永久删除仅存在于用户明确确认的隔离区批次清空功能（`DeleteBatch` / `PurgeOlderThan`），不得成为普通清理或自动任务的出口。

## 四道保险如何落地

| 保险 | 实现位置 |
|---|---|
| 严格白名单 | `ScanEngine.Scan` 只遍历 `set.Rules`，且只处理 `r.Enabled`；`CleanupPlanBuilder.Create` 只从本次扫描与所选规则构造授权 |
| 年龄阈值 | `RuleSelector.Apply` + `RuleSetLoader.EffectiveAgeDays` |
| 强制预览 | CLI 无 `--apply` 只打印计划返回 0；WPF 二次确认 |
| 隔离区可还原 | `QuarantineManager`（详见下文） |

## 隔离区

### 位置

`QuarantineManager.DefaultRoot()`：在**固定盘 + 已就绪 + 非系统盘**中取剩余空间最大者，路径为 `<盘符根>\KleanerQuarantine`。找不到其他盘时回退 `%LOCALAPPDATA%\Kleaner\quarantine`（即仍在 C 盘）。全程 try-catch 静默。

用户可在 WPF 设置里覆盖，持久化在 `%APPDATA%\Kleaner\settings.json` 的 `QuarantineRoot`。**CLI 读同一个文件**——GUI 改了隔离区位置，CLI 会跟着走。

### 移入

`Execute(CleanupPlan)` 一次调用是一个批次。它不接收调用方任意拼出的路径或规则 ID；先对计划快照重新扫描并核对候选元数据，再执行以下步骤。审计日志不可写或初始 manifest 落盘失败时，到此为止，**不会移动任何文件**：

- 批次目录名 `batchId` = UTC 毫秒时间 + 随机 GUID，保证并发和同秒执行时不冲突
- 文件落位 `MapRelative` 把盘符变成首层目录：`C:\Users\x\a.txt` → `<batchDir>\C\Users\x\a.txt`。不同盘的文件因此可共存于同一批次，反向也能推回原盘
- 每项移动前先把 `pending` 条目原子写入 manifest；移动成功后更新为 `moved`，任何异常都保留已有 manifest 与隔离文件
- 逐文件 `File.Move`，**任何异常只跳过并记录，绝不强制删除**
- `manifest.json` 以同目录临时文件、落盘刷新、覆盖替换的方式更新；不接受一次移动完再补写清单
- 写入 `clean-start` 与最终 `clean` 历史；最终结果如有跳过则为 `partial`

### 清单（manifest）

位于 `<隔离区根>/<batchId>/manifest.json`，每条记录五个字段：`OriginalPath`、`QuarantinedPath`、`SizeBytes`、`RuleId`、`State`（`pending` 或 `moved`）。

**不记录修改时间、哈希、权限**。还原只保证路径与内容，不还原时间戳。

读取清单时，批次 ID 必须等于实际目录名；整份清单先校验绝对路径、隔离路径位于当前批次、与原路径映射一致、来源不重复、大小非负、状态合法。原路径不能指回隔离区。任一条目非法就拒绝整批还原，之前的合法条目也不移动。这不提供清单真实性认证。

隔离区根目录、批次、清单、来源与还原目标的现有路径段从根到叶检查 reparse point；发现目录联结等链接即拒绝。路径暂不存在允许继续，其他属性读取错误传播。移动前和清单替换前重复检查；测试已覆盖现存目录联结及操作前根目录替换。检查与实际 IO 尚非同一个原子操作，不能据此宣称已解决检查之后的并发替换。

### 还原

`RestoreBatch` 整批还原。原路径已存在同名文件时，还原为 `{原路径}.restore-{batchId}`，**绝不覆盖现有文件**。每成功还原一项就原子更新 manifest；只在所有条目均已恢复后删除空批次目录。任何缺失、被占用或移动失败都会保留整个批次与尚存隔离文件，并在 `RestoreReport` / `restore` 历史中报告 `partial`。

*坑*：`RestoreBatch` 直接 `File.ReadAllText(manifest)`，对缺失或损坏的清单没有 try-catch；GUI 层与 CLI 调用方需自行处理异常。

收尾仅移除空目录与本批次 `manifest.json`。发现未登记文件、reparse point 或目录处理异常时保留清单，禁止删除未知内容；最终 `restore` 历史在收尾之后记录，收尾失败也是 `partial`。

### 清空

`PurgeOlderThan(TimeSpan)` 按 `CreatedUtc` 比对删除过期批次。**只由用户显式触发**——全仓唯一的真实调用点是 WPF 隔离区页的「清空 7 天前批次」按钮，没有定时器、没有启动自调用。

`DeleteBatch` 落历史 `delete-batch`，`PurgeOlderThan` 落历史 `purge`。

清空文件时保留批次根目录的 `manifest.json`，只有内容处理无失败、空目录收尾成功后才移除清单。锁定文件导致部分清空失败时，重建管理器仍可列出批次、还原尚存文件；清单中已被手动清空的条目在还原时报告缺失，不声称整批完整恢复。reparse point 跳过并计入失败。

## 操作历史

只追加的 JSON Lines，路径 `%APPDATA%\Kleaner\history.jsonl`。字段：`Id`、`Utc`、`Action`、`Detail`、`FileCount`、`Bytes`、`Result`。

实际会出现的 `Action`：

| Action | 触发点 |
|---|---|
| `clean-start` / `clean` | 隔离区移入的审计起始 / 最终成功或部分成功 |
| `restore-start` / `restore` | 整批还原的审计起始 / 最终成功或部分成功 |
| `delete-batch-start` / `delete-batch` | 删除单个批次的审计起始 / 最终成功或部分成功 |
| `purge` | 清空过期批次 |
| `cli-clean` | CLI 用户在确认环节取消 |
| `startup-disable` | 禁用启动项 |
| `startup-restore` | 还原启动项 |

*坑*：`HistoryManager.cs` 的注释列举的 action 集合已经过时——漏了 `startup-*` 两项，且列了 `large-files` / `duplicates`，这两个**全仓没有调用点**。以实际代码为准，不以注释为准。

单行损坏不阻塞整体展示（`Recent` 逐行 try-catch）。

## CLI 安全契约

子命令：`scan`（默认）、`clean`、`large-files`、`duplicates`、`usage`、`startup`、`startup-test`。
通用参数：`--format text|json`（默认 text）、`--yes`、`--help`。

### `clean` 的三道闸

1. 无 `--apply` → 打印 dry-run 计划，**返回 0**
2. 有 `--apply` 但输入被重定向（非交互）且无 `--yes` → 报错并**返回 2**
3. 有 `--apply` 且交互 → 终端 `y/N` 确认；非 `y` 则记 `cli-clean` 取消并返回 0

### 退出码

| 码 | 含义 |
|---|---|
| 0 | 成功（**含 dry-run、含用户取消**） |
| 1 | 规则校验失败、未知命令/规则 id、未捕获异常、`startup-test` 自检失败 |
| 2 | 非交互环境执行删除但未传 `--yes` |

### 坑

- **`clean` 不传 `--rule` 会静默成功**：`--rule` 缺省为空集合，选中 0 条规则，plan 为 0，加 `--apply --yes` 后照常返回 0。看起来像"没有可清理项"，实为参数遗漏。
- **`RuleUpdateService` 的本地覆盖优先**：`%APPDATA%\Kleaner\rules\rules.v1.json` 存在时盖过内置规则库。

## 引擎层固定排除

| 情形 | 处理 |
|---|---|
| reparse point | `GlobScanner` 一律跳过，规则无需声明 |
| 被占用 / 无权限（移入阶段） | `File.Move` 异常 → 跳过并记入 `Skipped`，报告里提示 |
| 无权限（扫描阶段） | `ScanEngine` 捕获 `UnauthorizedAccessException`，该规则产出 0 文件并附 Note「需要管理员权限，未扫描」 |
| 其他扫描异常 | 记入 `ScanReport.Errors`，不中断整体扫描 |
| 隔离区自身 | 仅当构造 `ScanEngine` 时传入了 `quarantineRoot` 才排除 |

## 提权

两条路径，互不通用：

- **规则级**：`requiresElevation: true` 的规则，GUI 在勾选系统级清理时通过同端口、同 token 的 runas 交接整进程重启提权。
- **启动项**：HKLM 下的启动项走 `reg.exe` + `runas` 提权（`StartupManager`），失败会回滚。

## 已知问题

- 上述回归仅证明已覆盖的文件锁、未知文件和正常收尾场景。进程在移动与清单更新之间中断、并发修改批次路径、清单持久化或最终历史写入失败的恢复协议仍未完整验证，工单 10 保持打开。

- `StartupManager` 无 xunit 覆盖，只有 CLI 的 `startup-test` 往返自检，而该自检会写真实注册表与启动文件夹，CI 上不可跑。
