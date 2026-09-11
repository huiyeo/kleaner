# 04: K1 系统内存转储规则（system 类规则扩张）

**What to build:** 新增 system 类规则：内核内存转储 `C:\Windows\MEMORY.DMP` 与 `C:\Windows\Minidump\`。走既有规则三关（`docs/rules.md`），requiresElevation=true（系统根目录，提权已支持）。

**Blocked by:** 01（评估结论）。

**Status:** complete

- [x] 规则条目 + safetyDoc + safetyNotes ≥20 字符（证据关，2026-09-12；调试数据非用户数据的依据已写入 safety-notes）
- [x] 未提权行为验证：MEMORY.DMP 不存在（精确路径分支 0 命中）、Minidump 目录存在且为空（未提权可读）、扫描无异常（2026-09-12）
- [x] governance-report：证据覆盖率 103/103、维护标注 103/103、超龄 0；默认勾选数 24 不变（新规则未默认勾选）
- [x] 本地签名演练：v1.1.0 清单签名 + 公钥独立验证通过（与 03 同产物）
- [x] 渠道发布：✅ 2026-09-12 随 03 推送 rules-channel（ceced6c），线上核验通过（SHA512 + 签名）
- [x] 真机验证（命中/隔离管线）：✅ 2026-09-12 UAC 提权受控演练——合成测试转储（回拨 mtime 越过 14 天阈值）提权扫描命中 1 项，`clean --apply` 移入沙箱隔离区（moved=1 skipped=0），manifest 与沙箱审计记录完整；真实数据零污染（审计历史逐字节一致、真实隔离区未动）。真实转储内容的被占用场景由引擎占用跳过机制兜底（回归锁定）

**边界：** MEMORY.DMP 可能包含内核内存内容，仅作调试数据对待；默认不勾选直至真机实测转正。被占用文件跳过并在报告提示（引擎既有行为，回归锁定）。

## Comments

- 2026-09-11：拆票。评估依据见 `docs/phase4-tool-assessment.md` K1 节。
- 2026-09-12：实施 + 部分验证。规则 `kernel-dumps`：`%SystemRoot%\MEMORY.DMP`（精确路径，走 GlobScanner 精确分支）+ `%SystemRoot%\Minidump\**`；requiresElevation=true（与 windows-update-download/wer-reports 同惯例），ageDays 继承 system=14，verified 诚实标注「官方文档来源，本机未验证，默认不勾选」。真机核实：MEMORY.DMP 不存在、Minidump 存在且为空、未提权扫描 0 命中无异常。与 `crash-dumps`（应用级 `%LOCALAPPDATA%\CrashDumps`）无重叠。命中/被占用/删除场景本机无法诚实验证（无真实转储，不伪造内核转储）——转正路径：任一有真实转储的机器验证后更新 verified。发布产物与 03 同一份 v1.1.0 签名清单。
- 2026-09-12（晚）：渠道发布 + 提权受控演练收口，工单转 complete。演练设计：提权创建合成测试文件 `Minidump\synthetic-verify.dmp`（34 字节，mtime 回拨 15 天越过阈值）→ 单规则 `clean --rule kernel-dumps --apply --yes`，隔离区与审计全部注入沙箱（`--quarantine-root`/`--history-path`）。结果：提权扫描命中 1 → 移动 1/跳过 0；沙箱 manifest 原路径映射完整、clean-start/clean-summary 审计记录成链；演练后真实 Minidump 为空、真实审计历史逐字节一致、真实隔离区未动、沙箱已删除（合成文件随沙箱永久清除，属计划内）。被占用场景：引擎占用跳过机制 + 回归锁定覆盖，真实 MEMORY.DMP 锁定场景（蓝屏后重启前）风险由「失败时保留数据」宪法条款兜底。`verified` 字段保守不升级（避免为文案变更重签渠道清单），默认勾选升级随下次规则更新窗口与治理目标一并评估。
