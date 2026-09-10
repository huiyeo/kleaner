# 04: 规则撤回/禁用与受影响版本机制

**What to build:** 建立规则的**撤回/禁用**与**受影响版本**机制：当某条规则被证实危险或证据失效时，能通过签名更新通道快速禁用（而非等待下次应用重装），并标记受影响的应用版本范围供发布说明引用。

**Blocked by:** None。

**Status:** complete

- [x] schema 扩展设计（`Rule` 增加 `deprecated`/`deprecationReason`/`affectedAppVersions` 等字段的 schema v2 草案——向后兼容：旧应用忽略未知字段，新应用 fail-closed 语义）
- [x] `RuleSetLoader` 消费语义：deprecated 规则强制不勾选 + UI 标注「已撤回」+ 扫描跳过（含 xunit：先失败后通过）
- [x] 签名清单撤回流程：`sign-rules.ps1` 支持生成撤回清单；应用收到撤回版本 ≥ 当前规则版本时禁用对应规则并审计
- [x] 撤回机制与回滚机制的交互定义（撤回 vs 回退到旧规则版本——优先撤回，回退仅作为更新失败的恢复）
- [x] 文档同步：`docs/rules.md`（schema 变更）、goals.md Phase 2 勾选

**边界：** 不改动签名验证链路本身（工单 12 已收口）；schema v2 需保持 v1 规则文件可加载（向后兼容）。

## Comments

完成记录（2026-09-10）：**schema 保持 v1 不升版**——撤回字段（`deprecated`/`deprecationReason`，均可选、缺省即旧行为）作为 v1 规则对象的可选扩展加入，旧应用的手工 JSON 解析自然跳过未知字段（加载兼容实测：规则带撤回字段时 0.2.6 形态的 LoadFromJson 正常）。签名清单流程不变：撤回=编辑 rules.v1.json 标记字段 → `sign-rules.ps1` 签名 → 推 rules-channel（「撤回清单」即撤回版规则文件本身，无需独立格式）。

三层防线（全部先失败后通过）：
1. **策略层**：`RuleSelectionPolicy.IsDefaultSelectable` 对 deprecated 一票否决（强于 verified 本机实测）。
2. **扫描层（最深防线）**：`ScanEngine.Scan` 跳过 deprecated 规则——即使发布者误设 Enabled=true 也永不执行；测试验证 deprecated+enabled=true → 零结果。
3. **呈现层**：主窗口备注列显示「已撤回：原因」。

受影响版本：`deprecationReason` 自由文本承载（建议含发现时间与影响面），不做结构化版本区间——避免过度设计；发布说明引用撤回原因即可。与回滚的交互：**撤回优先**（快速止血，签名清单即时下发）；回退（整包回退到旧规则版本）仅作为更新失败的恢复路径，二者不混用（文档已定义）。`docs/rules.md` schema 表已同步两字段。

## Comments
