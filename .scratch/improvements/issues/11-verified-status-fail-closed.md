# 11: 验证状态默认拒绝

**What to build:** `RuleSelectionPolicy` 与 WPF `RuleRow` 当前把缺少 `verified` 的规则视作“本机实测”，会默认勾选并进入普通用户的清理计划。这是验证状态的 fail-open。将缺失、空白或非“本机实测”状态统一视作未验证；只有明确的本机实测状态才可默认选中。现有规则库必须通过完整性检查，文档和测试同步为新契约。

**Blocked by:** None。该工单只改变默认选择与呈现，不新增规则、不扩大执行范围；执行仍受规则白名单、清理计划和隔离区事务约束。

**Status:** complete

- [x] 缺失、空白、非本机实测的 `verified` 一律默认不勾选
- [x] App 复用 Core 策略，不保留第二套 fail-open 判断
- [x] 正式规则库确认每条规则都有非空 `verified` 与 `safetyDoc`
- [x] xunit 覆盖缺失、空白、官方依据和本机实测状态
- [x] 同步 `docs/rules.md`、`docs/context.md`、README 与长期进度
- [x] Release 构建 0 警告/0 错误，完整测试绿色

**验收记录（2026-09-05）：** 正式规则库 101/101 条均具有非空 `verified` 与 `safetyDoc`；Release 构建 0 警告/0 错误，xunit 81/81 通过。
