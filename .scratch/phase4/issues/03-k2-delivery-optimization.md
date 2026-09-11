# 03: K2 传递优化缓存规则（system 类规则扩张）

**What to build:** 新增 system 类规则：Windows 传递优化缓存（`C:\Windows\SoftwareDistribution\DeliveryOptimization\...` / DOSVC）。走既有规则三关（`docs/rules.md`：证据 → 真机验证 → 签名发布），requiresElevation 按真机验证结果定；与现有「Windows 更新下载残留」规则路径不相交（无重复计费）。

**Blocked by:** 01（评估结论）。

**Status:** ready-for-agent

- [ ] 规则条目 + safetyDoc + safetyNotes ≥20 字符（证据关）
- [ ] 真机验证：命中路径确认、被占用场景、提权需求确认（验证关）
- [ ] governance-report：证据覆盖率/验证覆盖率不回退，维护字段标注
- [ ] 签名清单发布到 rules-channel（发布关——**渠道重发布需所有者私钥**，见 `rules-signing-workflow`；可用真机验证 + 本地签名演练先行，渠道发布待私钥窗口）

**边界：** 官方缓存语义、重建无害；默认不勾选（未真机实测前不得默认勾选）。规则扩张宁缺毋滥，验证不过即弃。

## Comments

- 2026-09-11：拆票。评估依据见 `docs/phase4-tool-assessment.md` K2 节。签名渠道依赖与既有遗留（maintainer 字段回填后渠道未重发布）同窗口处理。
