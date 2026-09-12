# 01: 普查工具 rule-census（Core + CLI）

**What to build:** 只读普查能力——对规则集逐规则逐模式判定「通配符前缀目录存在性」，结合一次完整扫描的命中数据，输出逐规则证据档案（Present / AbsentOrDenied × 命中/无命中）与聚合摘要。落点：`GlobScanner.TryGetStartDir`（前缀目录推导单源化，EnumerateFileInfos 复用）+ `RuleCensus`（纯函数，可测）+ ScanCli 动词 `rule-census`。

**Blocked by:** 无（Phase 5 立项即做）。

**Status:** complete

- [x] GlobScanner.TryGetStartDir + 回归（展开/无通配/首段通配/中段通配；语义=第一个通配符之前的完整前缀，EnumerateFileInfos 经 Decompose 单源化复用）
- [x] RuleCensus 纯函数 + 测试（Present/AbsentOrDenied/精确路径/多模式混合/聚合；Verdict 在 JSON 输出为数字枚举 0=Present 1=AbsentOrDenied）
- [x] CLI `rule-census`（人读 + JSON 双输出；--rules 覆盖绕过应用本地覆盖）
- [x] 零写入纪律：普查只读（ScanEngine 只读 + Directory/File Exists 探测）

**边界：** 只读；不给任何规则「升级」结论——只产出证据档案，升级在决策包。

## Comments

- 2026-09-12：立票并开工（Phase 5 首票）。
- 2026-09-12：完成。测试 238/238（新增 8 项：TryGetStartDir 3 + RuleCensus 5；修正 2 处测试自身断言错误——起点语义与临时根缺失）。过程中 NuGet 全局缓存损坏复发（23 个骨架包缺 nuspec，`dotnet test` 静默 exit 0），按 `nuget-cache-corruption` 记忆修复（locals clear + restore）——该记忆已追加复发记录。
