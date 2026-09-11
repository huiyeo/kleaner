# 04: K1 系统内存转储规则（system 类规则扩张）

**What to build:** 新增 system 类规则：内核内存转储 `C:\Windows\MEMORY.DMP` 与 `C:\Windows\Minidump\`。走既有规则三关（`docs/rules.md`），requiresElevation=true（系统根目录，提权已支持）。

**Blocked by:** 01（评估结论）。

**Status:** ready-for-agent

- [ ] 规则条目 + safetyDoc + safetyNotes ≥20 字符（证据关；调试数据非用户数据的依据成文）
- [ ] 真机验证：转储文件存在/被占用（系统可能持有句柄）场景、提权执行确认（验证关）
- [ ] governance-report：覆盖率指标不回退，维护字段标注
- [ ] 签名清单发布到 rules-channel（发布关——**需所有者私钥**，与 03 同窗口）

**边界：** MEMORY.DMP 可能包含内核内存内容，仅作调试数据对待；默认不勾选直至真机实测转正。被占用文件跳过并在报告提示（引擎既有行为，回归锁定）。

## Comments

- 2026-09-11：拆票。评估依据见 `docs/phase4-tool-assessment.md` K1 节。
