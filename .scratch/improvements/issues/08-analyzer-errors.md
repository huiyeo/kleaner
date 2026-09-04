# 08: 将既有分析器规则升级为错误级

**What to build:** 阶段 A 的观察期内持续为零告警，满足提前收紧条件。仅把根 `.editorconfig` 中当前已启用的代码风格与可空性七条规则从 `warning` 提升至 `error`；不启用全局 `TreatWarningsAsErrors`，避免把 SDK、第三方或未来未审查的告警一并升级。

**Blocked by:** None（2026-09-05 Release 构建仍为 0 警告/0 错误）

**Status:** complete

- [x] 七条既有规则改为 `error`，其余规则级别不扩大
- [x] `docs/conventions.md` 与 `docs/analyzer-baseline.md` 记录收紧边界和验证结论
- [x] Windows Release 构建 0 警告/0 错误，完整测试 72/72 通过

完成于 2026-09-05：未启用全局 `TreatWarningsAsErrors`；只将已清零并完成观察的规则提升为 error。
