# 06: v1.0.0 发布执行

**What to build:** 汇总 01–05 的验收结论，走完 `docs/release-checklist.md` 全部六节，发布 v1.0.0。本票是 goals.md Phase 1 的收口工件：GitHub Release 附件、SHA512 校验记录、发布说明（含 SmartScreen 指引与已知边界声明）。

**Blocked by:** 01、03、04、05 全部收口（02 按其结论处理完毕）。

**Status:** complete

- [x] release-checklist 第 1 节：构建/测试/文档同步/版本号/宪法冲突全部通过
- [x] 第 2 节：打包四件齐全
- [x] 第 3 节：SHA512 校验记录入发布说明
- [x] 第 4 节：五条矩阵路径全部「已验证」并留记录
- [x] 第 5 节：依赖治理表与实际 csproj 一致
- [x] 第 6 节：GitHub Release 发布、附件核对、goals.md 进度记录
- [x] 发布说明包含：SmartScreen 指引、自动更新未接线声明（如仍适用）、已知边界（ADR 0002/0003、断电评估结论）

**边界：** 发布属对外动作——执行前向用户确认版本号与发布时机。

## Comments

发布记录（2026-09-10，用户确认「现在发布 v1.0.0」后执行）：版本 1.0.0（csproj AssemblyVersion 同步）；发布说明 `docs/release-notes-v1.0.0.md`（含 SHA512 四件、SmartScreen 指引、已知边界：回退补验完成/断电置信论证/ADR-0002 边界/自动更新未接线）；**回退矩阵行补验通过**（v1.0.0↔0.3.1 双向真机：0.3.1 启动正常、1.0.0 数据——哈希链 head/rules/历史——完整保留可读）。GitHub Release：https://github.com/huiyeo/kleaner/releases/tag/v1.0.0（四件附件）。机器上已重装 1.0.0 作为生产版本。Phase 1 四条完成定义全部满足。