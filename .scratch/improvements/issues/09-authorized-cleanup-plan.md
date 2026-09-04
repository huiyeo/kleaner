# 09: 收紧清理执行授权并恢复工具箱只读边界

**What to build:** 当前 `QuarantineManager.Execute` 接受任意 `(RuleId, FileCandidate)`，工具箱可把任意目录扫描结果以 `large-files` / `duplicates` 伪规则 ID 送入执行器，违反严格白名单。先移除工具箱的清理入口，再建立只能由规则引擎基于当前 `RuleSet` 与扫描结果生成、且执行器可验证来源和规则版本的清理计划；GUI 与 CLI 统一消费该计划，执行时重新校验关键安全条件。

**Blocked by:** None。自动化测试仅使用临时目录和注入依赖；不得在真实系统执行清理。

**Status:** ready-for-human

- [x] 工具箱大文件与重复文件保持只读，只允许查看、下钻、打开位置或生成候选报告，不得调用 `QuarantineManager.Execute`
- [x] `QuarantineManager` 不再公开接受裸路径与任意字符串规则 ID
- [x] 清理计划只能由 Core 基于已加载规则集、扫描报告和用户选择生成，并携带可验证的规则 ID / 规则版本 / 候选快照
- [x] 执行前复验文件仍存在、不是 reparse point、仍匹配规则路径与排除项、仍满足年龄和 keepNewest 约束；变化项跳过并报告
- [x] GUI 与 CLI 均迁移到同一授权计划路径，保留 GUI 确认和 CLI `--apply` / `--yes` 契约
- [x] xunit 覆盖伪规则、篡改路径、过期快照、执行时状态变化、正常计划和取消/跳过；全部只用临时目录
- [x] 同步更新 `docs/deletion-path.md`、`docs/architecture.md`、`docs/context.md`、README 与相关 UI 文案
- [ ] Release 构建 0 警告/0 错误，完整测试绿色，并完成只读工具箱界面走查

**验收记录（2026-09-05）：** Release 构建 0 警告/0 错误，xunit 75/75 通过。应用已在本机成功启动；当前自动化运行环境未提供原生窗口访问能力，无法取得工具箱实际截图或点击走查，故此项仅待人工视觉验收，不能据构建结果声称已经完成。
