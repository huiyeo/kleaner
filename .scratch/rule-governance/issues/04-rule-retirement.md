# 04: 规则撤回/禁用与受影响版本机制

**What to build:** 建立规则的**撤回/禁用**与**受影响版本**机制：当某条规则被证实危险或证据失效时，能通过签名更新通道快速禁用（而非等待下次应用重装），并标记受影响的应用版本范围供发布说明引用。

**Blocked by:** None。

**Status:** ready-for-agent

- [ ] schema 扩展设计（`Rule` 增加 `deprecated`/`deprecationReason`/`affectedAppVersions` 等字段的 schema v2 草案——向后兼容：旧应用忽略未知字段，新应用 fail-closed 语义）
- [ ] `RuleSetLoader` 消费语义：deprecated 规则强制不勾选 + UI 标注「已撤回」+ 扫描跳过（含 xunit：先失败后通过）
- [ ] 签名清单撤回流程：`sign-rules.ps1` 支持生成撤回清单；应用收到撤回版本 ≥ 当前规则版本时禁用对应规则并审计
- [ ] 撤回机制与回滚机制的交互定义（撤回 vs 回退到旧规则版本——优先撤回，回退仅作为更新失败的恢复）
- [ ] 文档同步：`docs/rules.md`（schema 变更）、goals.md Phase 2 勾选

**边界：** 不改动签名验证链路本身（工单 12 已收口）；schema v2 需保持 v1 规则文件可加载（向后兼容）。

## Comments
