# 04: K1 系统内存转储规则（system 类规则扩张）

**What to build:** 新增 system 类规则：内核内存转储 `C:\Windows\MEMORY.DMP` 与 `C:\Windows\Minidump\`。走既有规则三关（`docs/rules.md`），requiresElevation=true（系统根目录，提权已支持）。

**Blocked by:** 01（评估结论）。

**Status:** blocked（剩余关卡需所有者输入，见 Comments）

- [x] 规则条目 + safetyDoc + safetyNotes ≥20 字符（证据关，2026-09-12；调试数据非用户数据的依据已写入 safety-notes）
- [x] 未提权行为验证：MEMORY.DMP 不存在（精确路径分支 0 命中）、Minidump 目录存在且为空（未提权可读）、扫描无异常（2026-09-12）
- [x] governance-report：证据覆盖率 103/103、维护标注 103/103、超龄 0；默认勾选数 24 不变（新规则未默认勾选）
- [x] 本地签名演练：v1.1.0 清单签名 + 公钥独立验证通过（与 03 同产物）
- [ ] 真机验证（命中/被占用/删除场景）：本机无转储文件且不伪造内核转储——待任意一台有真实 MEMORY.DMP/Minidump 的机器（用户窗口；或本机自然产生转储后补验转正）
- [ ] 渠道发布：与 03 同产物同窗口，需所有者确认

**边界：** MEMORY.DMP 可能包含内核内存内容，仅作调试数据对待；默认不勾选直至真机实测转正。被占用文件跳过并在报告提示（引擎既有行为，回归锁定）。

## Comments

- 2026-09-11：拆票。评估依据见 `docs/phase4-tool-assessment.md` K1 节。
- 2026-09-12：实施 + 部分验证。规则 `kernel-dumps`：`%SystemRoot%\MEMORY.DMP`（精确路径，走 GlobScanner 精确分支）+ `%SystemRoot%\Minidump\**`；requiresElevation=true（与 windows-update-download/wer-reports 同惯例），ageDays 继承 system=14，verified 诚实标注「官方文档来源，本机未验证，默认不勾选」。真机核实：MEMORY.DMP 不存在、Minidump 存在且为空、未提权扫描 0 命中无异常。与 `crash-dumps`（应用级 `%LOCALAPPDATA%\CrashDumps`）无重叠。命中/被占用/删除场景本机无法诚实验证（无真实转储，不伪造内核转储）——转正路径：任一有真实转储的机器验证后更新 verified。发布产物与 03 同一份 v1.1.0 签名清单。
