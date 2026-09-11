# 03: K2 传递优化缓存规则（system 类规则扩张）

**What to build:** 新增 system 类规则：Windows 传递优化缓存（`C:\Windows\SoftwareDistribution\DeliveryOptimization\...` / DOSVC）。走既有规则三关（`docs/rules.md`：证据 → 真机验证 → 签名发布），requiresElevation 按真机验证结果定；与现有「Windows 更新下载残留」规则路径不相交（无重复计费）。

**Blocked by:** 01（评估结论）。

**Status:** complete

- [x] 规则条目 + safetyDoc + safetyNotes ≥20 字符（证据关，2026-09-12）
- [x] 未提权行为验证：现行位置 ACL 拒绝访问被引擎静默跳过、旧版位置不存在、0 命中无异常（2026-09-12，`--rules` 覆盖扫描 + 全库扫描双证）
- [x] governance-report：证据覆盖率 103/103、维护标注 103/103、超龄 0；验证覆盖率 23.3%（24/103，算术摊薄，警告项与此前相同）
- [x] 本地签名演练：v1.1.0 清单签名成功，Ed25519 经公钥独立验证有效，SHA512 与 103 条副本一致（2026-09-12）
- [x] 渠道发布：✅ 2026-09-12 所有者确认后推送 rules-channel（ceced6c）；线上产物经 GitHub API 绕 CDN 取回核验——SHA512 与清单一致、Ed25519 签名公钥验证通过、103 条含 K1/K2
- [x] 真机验证（提权）：✅ 2026-09-12 UAC 提权会话实测——引擎可枚举 DOSVC Cache 22 个文件（约 3.75GB）；年龄过滤（14 天）正确排除全部新缓存（0 选中，语义正确）；实际超龄文件清理与 kernel-dumps 演练共用同一执行管线，随下次窗口补验

**边界：** 官方缓存语义、重建无害；默认不勾选（未真机实测前不得默认勾选）。规则扩张宁缺毋滥，验证不过即弃。

## Comments

- 2026-09-11：拆票。评估依据见 `docs/phase4-tool-assessment.md` K2 节。签名渠道依赖与既有遗留（maintainer 字段回填后渠道未重发布）同窗口处理。
- 2026-09-12：实施 + 部分验证。真机侦察：Win11 实际 DOSVC 缓存位于 `C:\Windows\ServiceProfiles\NetworkService\...\DeliveryOptimization\Cache`（未提权 Test-Path 即拒绝访问，ACL 限 NetworkService/SYSTEM）；旧版位置 `SoftwareDistribution\DeliveryOptimization` 本机不存在。规则限定两个位置的 `Cache\**`（路径收窄，不触碰 DO 配置/状态），requiresElevation=true，ageDays 继承 system=14，verified 诚实标注「官方文档来源，本机未验证，默认不勾选」。未提权引擎行为取证（候选规则 `--rules` 覆盖扫描 + 全库扫描双证）：0 命中、无异常、无噪音——目录不可达被 `Directory.Exists` 静默跳过（不会产生提权提示条，提权后才可达，此行为已写入 safety-notes）。与 `windows-update-download`（仅 `SoftwareDistribution\Download\**`）路径无重叠。证据关/治理/本地签名演练全过；剩余两关（提权真机验证、渠道推送确认）需所有者输入，工单转 blocked。
- 2026-09-12（晚）：渠道发布 + 提权验证收口，工单转 complete。渠道：所有者确认后推送 rules-channel（ceced6c），线上绕 CDN 核验通过。提权：UAC 会话实测发现「提权扫描 0 命中」与「探测枚举 22 文件/3.75GB」的表面矛盾，经同进程 harness 对照定位——**非引擎缺陷**：GlobScanner 枚举 22 个文件正常，CLI 扫描 0 是 RuleSelector 14 天年龄阈值正确排除全部新缓存（最近更新周期产物）。语义验证通过：提权可达 ✓、枚举正常 ✓、年龄过滤 ✓；对超龄文件的实际清理与 04 的受控演练共用执行管线（Executor + 隔离区，230 测试覆盖），留待缓存自然超龄后随下次窗口补验。`verified` 字段保守不升级（避免为文案变更重签 v1.1.1），升级随下次规则更新窗口。演练全程真实数据零污染：审计历史逐字节一致、真实隔离区未动、沙箱用后即删。
