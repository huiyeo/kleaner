# 20 发布链路落地与对等验收（观察期版本）

Type: task
Status: claimed
Blocked by: 16, 17, 18, 19

## Task

1. 发布形态落地（01/02）：自包含单文件 exe + wwwroot 散文件，`PublishTrimmed` + `EnableCompressionInSingleFile` 真机回归（裁剪与反射敏感代码验证）；`scripts/release.sh` 改喂 WebHost 产物，`vpk pack` 出安装 / 便携包，快捷方式语义不变。
2. 对等验收：以 `docs/deletion-path.md` 为基准的删除路径逐项对照清单**全量**过一遍（12/13 已各自验收的段落复核汇总）；29 个引擎测试全绿是底线而非充分条件。
3. 发布「带 WPF 的观察期版本」（06：双栈最后一版），收集回归反馈。

## Acceptance

- 对照清单全量打勾记录在本票 Comments；任何一项不过 → 回退修对应票，不带病发布。
- 观察期版本经 Velopack 安装、更新、回退（装回旧版）三路径真机验证。

## Comments

### 已完成：发布 smoke 与删除路径对照（2026-09-02）

- [x] 发布脚本改为 `Kleaner.WebHost` 的 win-x64 自包含单文件，启用 `PublishTrimmed`、单文件压缩，并保留散文件 `wwwroot` 与规则库。
- [x] 裁剪发布真机 smoke：交接模式启动发布 exe 后，`http://127.0.0.1:45173/` 返回 200；修复了裁剪默认关闭 JSON 反射导致 `ServiceStateFile.Write` 崩溃的问题。裁剪分析仍报告 JSON 反射告警，已显式记录为后续 source-generation 改进项。
- [x] 严格白名单：Web `POST /api/scan` 仅加载生效规则集；工具箱/高级模式均为独立只读分析，结果不进入规则或清理路径。
- [x] 年龄阈值与 keepNewest：扫描仍经 Core 的 `RuleSelector` / `RuleSetLoader.EffectiveAgeDays`；默认勾选仅使用 `RuleSelectionPolicy`。
- [x] 强制预览与确认：Web 清理固定为 scan → plan（一次性 confirmToken）→ confirm；空/未知规则、未完成扫描、错误或重放凭据均拒绝。
- [x] 可还原清理与审计：Web confirm 唯一执行路径是 `QuarantineManager.Execute`，同时落 `HistoryManager`；不会直接永久删除文件。
- [x] reparse point、无权限/被占用文件：继续由 Core 扫描排除与 QuarantineManager 跳过记录，前端展示报告/跳过数。
- [x] 隔离区还原不覆盖、批次路径闸、历史只读：13 的 API 回归测试覆盖；永久删除批次与 7 天清空均经 QuarantineManager，浏览器二次确认且明示不可还原。
- [x] CLI 契约未改动：CLI 项目仍独立入口，WebHost 不影响其 dry-run、`--apply`、`--yes` 与退出码语义。
- [x] 回归：`dotnet test Kleaner.slnx -c Release --no-restore`（Core 60、WebHost 78），`node --check`、`git diff --check` 均通过。

### 未完成的发布前置

- [ ] Velopack 安装、更新与装回旧版回退需以指定观察期版本在真实安装环境执行；本票保持 `claimed`，不把本地 publish smoke 视为该验收的替代。
