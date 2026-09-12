# 02: 真机普查执行与证据档案

**What to build:** 在本机运行 `rule-census`（未提权），产出 103 条规则的证据档案 JSON（落 `.scratch/rule-verification/`），按 verdict×分类聚合，产出两类清单：**验证转正候选**（Present+命中）与 **AbsentOrDenied 清单**（按 requiresElevation 拆分「撤回候选 vs 提权重核候选」）。

**Blocked by:** 01。

**Status:** complete

**边界：** 只读；提权重核候选若必要，另立票走 UAC 流程（需所有者到场点击）。

## Comments

- 2026-09-12：立票。
- 2026-09-12：完成。档案 `.scratch/rule-verification/census-20260912.json`（未提权，--rules 仓库 103 条文件绕过应用本地覆盖）。结果：Present 33（命中 5 / 无命中 28）、AbsentOrDenied 70（未提权 69 + 提权类 1）。**命中 5 条全部为既有「本机实测」规则**（dev-nuget-cache 562 文件/112.8MB、chrome/edge/user-temp/shader-cache）；AbsentOrDenied 绝大多数为本机未安装软件——不构成撤回依据（规则库服务于所有用户），定位问题入决策包 D2。提权重核候选：delivery-optimization-cache 已于 K2 验证中提权实测（22 文件/3.75GB 可达），无新增 UAC 需求。
