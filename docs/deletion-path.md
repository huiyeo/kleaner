# 删除路径与安全契约

**先记住一条：Kleaner 没有永久删除。** 全仓唯一的文件"消失"出口是 `QuarantineManager.Execute` 里的 `File.Move`。任何新增代码若直接调 `File.Delete` 删用户文件，即为绕过安全模型。

## 四道保险如何落地

| 保险 | 实现位置 |
|---|---|
| 严格白名单 | `ScanEngine.Scan` 只遍历 `set.Rules`，且只处理 `r.Enabled` |
| 年龄阈值 | `RuleSelector.Apply` + `RuleSetLoader.EffectiveAgeDays` |
| 强制预览 | CLI 无 `--apply` 只打印计划返回 0；Web 二次确认 |
| 隔离区可还原 | `QuarantineManager`（详见下文） |

## 隔离区

### 位置

`QuarantineManager.DefaultRoot()`：在**固定盘 + 已就绪 + 非系统盘**中取剩余空间最大者，路径为 `<盘符根>\KleanerQuarantine`。找不到其他盘时回退 `%LOCALAPPDATA%\Kleaner\quarantine`（即仍在 C 盘）。全程 try-catch 静默。

用户可在 Web 设置里覆盖，持久化在 `%APPDATA%\Kleaner\settings.json` 的 `QuarantineRoot`。**CLI 读同一个文件**——Web 改了隔离区位置，CLI 会跟着走。

### 移入

`Execute` 一次调用是一个批次：

- 批次目录名 `batchId` = `DateTime.Now` 的 `yyyyMMdd-HHmmss`
- 文件落位 `MapRelative` 把盘符变成首层目录：`C:\Users\x\a.txt` → `<batchDir>\C\Users\x\a.txt`。不同盘的文件因此可共存于同一批次，反向也能推回原盘
- 逐文件 `File.Move`，**任何异常只跳过并记录，绝不强制删除**
- 写 `manifest.json`（缩进 JSON、小驼峰）
- 落历史 `clean`

### 清单（manifest）

位于 `<隔离区根>/<batchId>/manifest.json`，每条记录四个字段：`OriginalPath`、`QuarantinedPath`、`SizeBytes`、`RuleId`。

**不记录修改时间、哈希、权限**。还原只保证路径与内容，不还原时间戳。

### 还原

`RestoreBatch` 整批还原。原路径已存在同名文件时，还原为 `{原路径}.restore-{batchId}`，**绝不覆盖现有文件**。还原后删除批次目录，落历史 `restore`。

*坑*：`RestoreBatch` 直接 `File.ReadAllText(manifest)`，对缺失或损坏的清单没有 try-catch；GUI 层与 CLI 调用方需自行处理异常。

### 清空

`PurgeOlderThan(TimeSpan)` 按 `CreatedUtc` 比对删除过期批次。**只由用户显式触发**——全仓唯一的真实调用点是 Web 隔离区页的「清空 7 天前批次」按钮，没有定时器、没有启动自调用。

`DeleteBatch` 落历史 `delete-batch`，`PurgeOlderThan` 落历史 `purge`。

## 操作历史

只追加的 JSON Lines，路径 `%APPDATA%\Kleaner\history.jsonl`。字段：`Id`、`Utc`、`Action`、`Detail`、`FileCount`、`Bytes`、`Result`。

实际会出现的 `Action`：

| Action | 触发点 |
|---|---|
| `clean` | 隔离区移入成功/部分成功 |
| `restore` | 整批还原 |
| `delete-batch` | 删除单个批次 |
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
- **`scan` 与 `clean` 不排除隔离区自身**：CLI 用无参 `new ScanEngine()`，未传 `quarantineRoot`；GUI 传了。若隔离区落在被规则命中的路径下，CLI 可能把已隔离文件再次计入。
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

- **规则级**：`requiresElevation: true` 的规则，Web 在勾选系统级清理时通过同端口、同 token 的 runas 交接整进程重启提权。
- **启动项**：HKLM 下的启动项走 `reg.exe` + `runas` 提权（`StartupManager`），失败会回滚。

## 已知问题

- `batchId` 用 `DateTime.Now`（本地时间）命名目录，而 manifest 内的 `CreatedUtc` 用 `UtcNow`。跨时区或跨零点时两者可能落在不同日期。
- `PurgeOlderThan` 落历史时写 `purged > 0 ? "ok" : "ok"`——两个分支相同，这个三元表达式无意义。
- `TryDeleteDir` 静默吞掉所有异常，删除失败不会上报。
- Web 工具箱将 `large-files` / `duplicates` 保持为只读分析结果，不会写入隔离区或历史。
- `StartupManager` 无 xunit 覆盖，只有 CLI 的 `startup-test` 往返自检，而该自检会写真实注册表与启动文件夹，CI 上不可跑。
