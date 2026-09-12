# 06: 签名发布预备（v1.2.0 物料）

**What to build:** 决策包裁决后（D1-D4 落定），把证据复核回填（lastEvidenceCheck、verified 升级清单、撤回清单）一次性合入 rules.v1.json，重签 v1.2.0 并完成线上核验——**推送本身是决策项 D3，本票只做物料**。

**Blocked by:** 03（决策包）+ 所有者对 D1/D2/D4 的裁决。

**Status:** complete（其目标已由工单 07 的流程提前实现：v1.2.0 已签发并发布；本票的「物料脚本化」部分由 sign-rules.ps1 的 LF 归一化加固承接）

**边界：** 未获 D3 确认不推送 rules-channel；签名私钥使用遵循 `rules-signing-workflow`。

## Comments

- 2026-09-12：立票。
