# 10: 隔离区事务隔离与强制审计

**What to build:** 清理、还原与隔离区清空目前允许省略 `HistoryManager`；`RestoreBatch` 对逐项失败静默吞掉后仍删除整个批次，可能永久删除未还原文件；批次 ID 只有秒级精度，manifest 在移动后一次性写入。将所有隔离区状态变化收束为带强制审计的事务：审计或事务初始化失败时不得移动文件；逐项状态和 manifest 必须原子落盘；还原只能在全部条目成功后移除批次；永久清空必须准确报告并审计失败。

**Blocked by:** None。所有自动化用例仅使用注入的临时隔离区与临时 history 路径；不得触发真实用户目录、真实隔离区、注册表或系统清理。

**Status:** ready-for-human

- [x] `QuarantineManager` 强制依赖可写 `HistoryManager`；所有 App、CLI 与测试调用点显式提供历史对象（测试使用临时路径）
- [x] 移入在首次文件状态变化前建立唯一批次、原子 manifest 和审计起始记录；初始化失败拒绝执行
- [x] manifest 记录逐项状态，移动成功后原子更新；异常与部分成功可恢复、可审计
- [x] `RestoreBatch` 对任何未恢复或失败条目保留批次及其文件，绝不调用递归删除；向调用方报告 restored/skipped/failed
- [x] `DeleteBatch` / `PurgeOlderThan` 在永久删除前后准确记录结果；删除失败不伪报成功
- [x] xunit 覆盖审计不可写拒绝执行、批次 ID 唯一、manifest 原子状态、部分还原保留、永久清空失败与审计
- [x] 同步 `docs/deletion-path.md`、`docs/architecture.md`、`docs/context.md`、README 与 UI 文案
- [x] Release 构建 0 警告/0 错误，完整测试绿色

**验收记录（2026-09-05）：** Release 构建 0 警告/0 错误，xunit 79/79 通过。全部测试使用临时目录、临时 history 文件与受控文件锁；未操作真实隔离区或用户文件。视觉走查沿用工单 09 的限制：当前自动化环境无法访问原生窗口，发布前仍需人工检查隔离区窗口的成功、部分还原和清空失败提示。
