# 02: B3 Windows.old 旧系统安装残留（只读检测 + 引导官方）

**What to build:** 高级模式新增「Windows.old」Tab：自动只读检测系统盘根目录 `Windows.old`（存在性 + reparse point 排除，零遍历）；检测到时提供显式触发的**有界**占用测量（流式枚举、上限截断提示）与官方路径引导——打开「设置 > 存储」（ms-settings）与磁盘清理 cleanmgr。Kleaner 不提供任何删除入口，不进扫描/清理链路。

**Blocked by:** 01（评估结论）。

**Status:** complete

- [x] SpecialOps 检测器（存在性 / reparse 排除 / 有界测量）+ xunit 回归（含 mklink 联结排除、截断边界）
- [x] App Tab + 文案走 Strings.zh-CN.json（S.Get/S.Format）
- [x] 回滚风险提示（期限表述以官方界面为准，Kleaner 不自行表述）
- [x] 真机走查（UIA：未检测到状态 + 文案与按钮可用性）

**边界：** 宪法第 8 条「只读分析或引导至 Windows 官方工具」逐字适用；删除即丢失系统回滚能力，Kleaner 永不执行、永不引导到非官方删除路径。检测与测量只读，不写入历史（非文件状态变化）。

## Comments

- 2026-09-11：拆票。评估依据见 `docs/phase4-tool-assessment.md` B3 节。开工即认领（claimed）。
- 2026-09-11：实施完成并收口。落点：`Kleaner.SpecialOps/WindowsOldInspector.cs`（Inspect 零遍历存在性 + Measure 有界测量，reparse 纪律与引擎一致）+ `Kleaner.App`「Windows.old」Tab（加载即检测、测量显式触发后台执行、官方入口仅打开系统自带工具）+ Strings 10 词条。测试 230/230（新增 6 项：存在性双向、reparse 排除、测量全量/截断/跳过联结子树；测试 Dispose 需先摘联结再删树——Directory.Delete(recursive) 遇联结抛拒绝访问）。真机走查（UIA，走查全程只读）：本机无 `C:\Windows.old`，加载即显示「未检测到」，测量/两个官方入口按钮正确禁用，警示文案完整、未自行表述回滚期限；「重新检测」行为正确；主窗口自动扫描不受影响（主流程零影响）。检测到状态的界面路径由临时根注入的单测覆盖（本机无法真实构造 C:\Windows.old）。
