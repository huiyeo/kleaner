# 08: AI 区无障碍走查发现（needs-triage）

**来源：** 工单 04（AI 区 Narrator 深度走查）的两个真实发现，2026-09-12 UIA 走查 + 插桩取证。

**Status:** complete

## 发现与修复（2026-09-13）

1. **行按压（AXPress）语义问题** ✅ 已修复：
   - 插桩定位真实路径：对 DataGrid 行发 AXPress 只把焦点移到行首单元格（连勾选都不切换），不触发行选中——初版修复（VM 层勾选→选中同步，保留为补充路径）因此未命中。
   - 最终修复（View 层）：`RulesGrid.CurrentCellChanged` 把当前单元格所在行同步为 `SelectedRow`——AXPress 聚焦、鼠标点击、键盘方向键导航三条路径全部联动安全性说明与 AI 解释。真实点击本就选中行，无副作用。
   - 验证：UIA 走查——AXPress user-temp 行后安全面板立即显示其 safetyNotes（修复前同路径面板为空）；VM 单测 3 项（勾选同步/取消勾选保持/默认勾选不抢占）。
2. **AI 输出无 live-region 播报** ✅ 已修复：
   - `AiOutputText` 设 `AutomationProperties.LiveSetting="Polite"` 与 `AutomationProperties.Name="AI 解释结果"`。
   - 验证方式（诚实标注）：XAML 属性到 UIA LiveSetting 的映射为编译期确定；Narrator 运行时实际播报效果待有人值守屏幕阅读器会话复核（UIA 快照无法代表语音行为）。

测试 243/243（新增 3 项 VM 行为测试）。

**边界：** 两项均为 UI 层无障碍改进，不触碰扫描/清理链路；修复需附 UIA 走查证据 ✓。

## Comments

- 2026-09-12：立票（走查证据见工单 04 的 Comments 与 s-3「请先选中一行规则」重现记录）。
- 2026-09-13：两项修复完成收口（见上）。
