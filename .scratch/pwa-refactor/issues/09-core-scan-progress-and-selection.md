# 09 Core 层适配：扫描进度回调与默认勾选策略

Type: task
Status: resolved

## Task

工单 04、07 落定的两处 Core 改动，都是小改，合一张票省一次回归：

1. `ScanEngine.Scan` 新增可选参数 `IProgress<ScanProgress>?`（默认 null，现有签名与行为零影响）。`ScanProgress` 为 record：`RuleId` / `FileCount` / `TotalBytes`，规则循环每完成一条上报。
2. 新增 `RuleSelectionPolicy.IsDefaultSelectable(Rule)`：verified 以「本机实测」开头或缺省 → 默认勾选（语义迁自 `Kleaner.App/RuleRow.MachineVerified`）。

## Acceptance

- Core.Tests 新增覆盖：进度事件按规则顺序与计数上报；null 进度时行为与现状一致；`IsDefaultSelectable` 各 verified 形态分支。
- 29 个既有测试全绿不动；Core 零依赖纪律不变。

## Comments

- 2026-08-31 已实施，60/60 全绿（原 29 + 新增 4 用例方法含 7 个 Theory 分支）。
- `ScanProgress` record 落在 `ScanEngine.cs`（`RuleId` / `FileCount` / `TotalBytes`）；`Scan` 新增第三个可选参数 `IProgress<ScanProgress>? progress = null`，每条规则处理完（含失败/无权限路径）上报一次；取消路径直接抛出不上报。
- `RuleSelectionPolicy.IsDefaultSelectable(Rule)` 落在新文件 `src/Kleaner.Core/RuleSelectionPolicy.cs`，语义与 `RuleRow.MachineVerified` 完全一致（Ordinal 前缀「本机实测」，null 视同已验证）。`RuleRow.MachineVerified` 暂未改为委托调用（WPF 层属过渡期遗留，随 21 删除），避免本次扩大改动面。
- 测试注意点：进度收集器用自定义 `IProgress<ScanProgress>` 直实现而非 `Progress<T>`（后者回调线程语义依赖 SynchronizationContext，存在竞态）；`Assert.Single` 带 Where 会触发 xUnit2031，改用谓词重载。
