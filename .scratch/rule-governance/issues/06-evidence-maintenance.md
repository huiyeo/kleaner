# 06: 规则维护责任与证据更新时间

**What to build:** goals.md Phase 2 收口审计发现的最后遗留项：规则维护责任（maintainer）与证据更新时间（lastEvidenceCheck）机制——schema 可选字段（v1 不升版，同 deprecated 先例）、严格解析（非法日期 fail-closed）、治理报告维护统计（已标注数/超龄数，>180 天）与 CheckTargets 警告（不阻塞）、CLI 输出、真实规则库回填。

**Blocked by:** 无（04 撤回机制同款模式）。

**Status:** complete

- [x] Rule 模型 + Maintainer/LastEvidenceCheck 可选字段
- [x] Loader 解析：缺省 null；存在但日期非法抛 FormatException（严格解析）
- [x] RuleGovernance.Report 维护统计（MaintainedRules/StaleEvidenceRules，asOf 参考日注入保测试确定性）
- [x] CheckTargets 两类警告：维护标注缺失、证据超龄（>180 天）
- [x] CLI governance-report 人工输出加「维护责任标注」行
- [x] 真实规则库 101 条回填（maintainer=huiyeo，lastEvidenceCheck=2026-09-11 治理基线审计日）
- [x] xunit：字段往返/缺省兼容/非法日期 fail-closed + 统计与警告（先失败断言后实现）

**边界：** 维护字段不参与扫描/清理决策（非安全不变量，仅治理可见性）；签名渠道（rules-channel）重新发布留待下次规则更新窗口（对外动作需所有者确认）。

## Comments

- 2026-09-11：实施收口。goals.md Phase 2「建立规则维护责任、证据更新时间……」从此行 ✅；推荐准确率口径（隐私边界无遥测）保持如实标注。真实规则库验证：101 条全标注、零超龄、验证覆盖率警告 23.8%<25% 为已知既有项。完整测试 218/218、Release 0/0。
