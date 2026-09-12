# 08: AI 区无障碍走查发现（needs-triage）

**来源：** 工单 04（AI 区 Narrator 深度走查）的两个真实发现，2026-09-12 UIA 走查 + 插桩取证。

**Status:** needs-triage

## 发现清单

1. **行按压（AXPress）语义是勾选而非选中**：对规则行发 AXPress 会聚焦/切换首列 checkbox，而 AI 解释与安全性说明依赖的是**行选中**（AXSelect 才触发）。屏幕阅读器用户按行激活后得到的是勾选反馈，安全性说明与 AI 解释入口均无响应，输出「请先选中一行规则。」。
   - 影响：中等（主路径对读屏用户不直觉）；建议：行选中逻辑对 AXPress 也成立，或行元素对外暴露更明确的选中语义/名称。
2. **AI 输出无 live-region 播报**：AiOutputText 是普通 TextBlock，解释结果到达时不会主动向读屏播报（无 AutomationLiveSetting），用户不知道解释已完成。
   - 影响：低-中（AI 是次级入口）；建议：设 AutomationProperties.LiveSetting=Polite（或等效自动化属性）。

**边界：** 两项均为 UI 层无障碍改进，不触碰扫描/清理链路；修复需附 UIA 走查证据。

## Comments

- 2026-09-12：立票（走查证据见工单 04 的 Comments 与 s-3「请先选中一行规则」重现记录）。
