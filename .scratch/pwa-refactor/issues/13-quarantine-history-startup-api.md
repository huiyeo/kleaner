# 13 隔离区、历史与启动项 API

Type: task
Status: resolved
Blocked by: 10

## Task

按 04 端点集合，把三个删除 / 改动路径相关资源落成 REST：

1. 隔离区：批次列表（读 manifest）、整批还原、删除单批、清空 7 天前批次（全部只由用户显式触发）。
2. 历史：`HistoryManager.Recent` 只读端点。
3. 启动项：枚举 / 禁用 / 还原（HKLM 走 reg.exe + runas 提权，失败回滚语义不变）。

## Acceptance

删除路径对照清单相关项逐条验收：

- [x] 还原：原路径有同名 → `{原路径}.restore-{batchId}`，绝不覆盖
- [x] 删除批次：UI 二次确认 + `delete-batch` 历史；清空：仅显式触发 + `purge` 历史（UI 对话框归工单 17 前端）
- [x] manifest 缺失 / 损坏时端点返回明确错误而非异常外溢（现 `RestoreBatch` 无 try-catch 的坑在 WebHost 层兜住）
- [x] 启动项禁用 / 还原落 `startup-disable` / `startup-restore` 历史
- [x] 所有写路径仍只经 `QuarantineManager` / `StartupManager`，WebHost 不新增任何 `File.Delete` 直调

## Answer

**落地（2026-08-31，测试 104 → 119 全绿，WebHost 44 → 59）：**

端点（全部在 `WebHostAppFactory`，走 10 的既有管线与五层防护）：

| 端点 | 语义 |
|---|---|
| `GET /api/quarantine/batches` | 批次列表（`QuarantineBatchView` 补 entryCount/totalBytes；缺失/损坏 manifest 的批次被 ListBatches 静默跳过，GUI 同语义） |
| `POST /api/quarantine/batches/{batchId}/restore` | 整批还原；manifest 缺失 → 404、损坏 → 500 明确错误（`RestoreBatch` 无 try-catch 的坑由 WebHost 层兜住） |
| `DELETE /api/quarantine/batches/{batchId}` | 删除单批；未知批次 404（不照抄 GUI 的静默 no-op） |
| `POST /api/quarantine/purge` | 清空 7 天前批次（固定 7 天与 GUI 对等，仅显式触发） |
| `GET /api/history?limit=` | `HistoryManager.Recent` 只读，limit 钳制 1–1000 |
| `GET /api/startup` | 启用 + 已禁用备份统一呈现（StartupWindow.Reload 同语义；kind/hive 枚举字符串化） |
| `POST /api/startup/disable` | 请求体只传 id，**服务端按 id 重新枚举定位目标**（不接受客户端伪造目标位置）；HKLM 由 StartupManager 内部 reg.exe + runas 提权，失败 409 + 备份记录回滚（原语义不变） |
| `POST /api/startup/restore` | 无备份记录 → 404；目标被占用等 → 409（保留备份不覆盖） |

关键决策：

- **seam 延续 12 的模式**：`HistoryProvider` / `QuarantineProvider` / `StartupProvider` 进 `KleanerWebHostOptions`，`HostRuntime` 出 `ResolveHistory/ResolveQuarantine/ResolveStartup`——生产默认同源（settings.json 的 QuarantineRoot + %APPDATA%\Kleaner\history.jsonl），测试指向临时目录 / 注入 `IStartupEnvironment` fake，绝不触碰真实注册表。
- **batchId 路径安全闸** `GuardBatchId`：路由直收客户端字符串，`Path.Combine` 前阻断 `..` / 分隔符 / 盘符逃出隔离区根（GUI 无此攻击面，Web API 新增）。
- **删除批次的 UI 二次确认归前端（工单 17）**：服务端凭据闸（03 第 5 层）按决策只覆盖 clean apply；DELETE 批次的 CSRF/误触防护由 Origin/Host/token 三层 + 前端对话框承担。
- 全部写路径只经 `QuarantineManager` / `StartupManager`，WebHost 零 `File.Delete` 直调；Core/Executor 零改动。

## Comments
