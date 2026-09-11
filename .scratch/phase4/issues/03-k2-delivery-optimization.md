# 03: K2 传递优化缓存规则（system 类规则扩张）

**What to build:** 新增 system 类规则：Windows 传递优化缓存（`C:\Windows\SoftwareDistribution\DeliveryOptimization\...` / DOSVC）。走既有规则三关（`docs/rules.md`：证据 → 真机验证 → 签名发布），requiresElevation 按真机验证结果定；与现有「Windows 更新下载残留」规则路径不相交（无重复计费）。

**Blocked by:** 01（评估结论）。

**Status:** blocked（剩余关卡需所有者输入，见 Comments）

- [x] 规则条目 + safetyDoc + safetyNotes ≥20 字符（证据关，2026-09-12）
- [x] 未提权行为验证：现行位置 ACL 拒绝访问被引擎静默跳过、旧版位置不存在、0 命中无异常（2026-09-12，`--rules` 覆盖扫描 + 全库扫描双证）
- [x] governance-report：证据覆盖率 103/103、维护标注 103/103、超龄 0；验证覆盖率 23.3%（24/103，算术摊薄，警告项与此前相同）
- [x] 本地签名演练：v1.1.0 清单签名成功，Ed25519 经公钥独立验证有效，SHA512 与 103 条副本一致（2026-09-12，产物 releases/ 已 gitignore 未推送）
- [ ] 真机验证（提权）：提权后 DOSVC Cache 可达性与实际清理场景——需 UAC 会话（用户窗口）
- [ ] 渠道发布：rules-manifest.json + rules.v1.json 推送 rules-channel 分支——对外发布，需所有者确认（签名产物已就绪，含 maintainer 回填 + K1/K2）

**边界：** 官方缓存语义、重建无害；默认不勾选（未真机实测前不得默认勾选）。规则扩张宁缺毋滥，验证不过即弃。

## Comments

- 2026-09-11：拆票。评估依据见 `docs/phase4-tool-assessment.md` K2 节。签名渠道依赖与既有遗留（maintainer 字段回填后渠道未重发布）同窗口处理。
- 2026-09-12：实施 + 部分验证。真机侦察：Win11 实际 DOSVC 缓存位于 `C:\Windows\ServiceProfiles\NetworkService\...\DeliveryOptimization\Cache`（未提权 Test-Path 即拒绝访问，ACL 限 NetworkService/SYSTEM）；旧版位置 `SoftwareDistribution\DeliveryOptimization` 本机不存在。规则限定两个位置的 `Cache\**`（路径收窄，不触碰 DO 配置/状态），requiresElevation=true，ageDays 继承 system=14，verified 诚实标注「官方文档来源，本机未验证，默认不勾选」。未提权引擎行为取证（候选规则 `--rules` 覆盖扫描 + 全库扫描双证）：0 命中、无异常、无噪音——目录不可达被 `Directory.Exists` 静默跳过（不会产生提权提示条，提权后才可达，此行为已写入 safety-notes）。与 `windows-update-download`（仅 `SoftwareDistribution\Download\**`）路径无重叠。证据关/治理/本地签名演练全过；剩余两关（提权真机验证、渠道推送确认）需所有者输入，工单转 blocked。
