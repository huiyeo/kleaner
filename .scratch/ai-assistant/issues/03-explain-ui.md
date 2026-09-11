# 03: 解释 UI 与免责标注

**What to build:** 扫描结果页的 AI 解释入口与展示 UI（形态待所有者确认：内嵌问答 vs 独立对话窗），含强制免责标注「AI 解释不构成安全保证，以规则库 safetyNotes 为准」。

**Blocked by:** 02（适配器与校验）。

**Status:** complete

- [x] UI 形态实现（按所有者确认）
- [x] 免责标注（每条 AI 输出可见处）
- [x] 启用/关闭开关（默认关闭）与设置持久化
- [x] 无障碍走查（工单 v1-core 03 同标准：DPI/键盘/对比度）

**边界：** AI 输出不修改任何状态；不替代 safetyNotes。

## Comments

- 2026-09-11：内嵌形态落地（按 ADR 0004 推荐形态，待所有者追认）：主窗口安全说明区 Row 2 = 「AI 解释（实验）」按钮 + 固定免责文本「AI 解释由本机模型生成，不构成安全保证；安全结论以规则库安全性说明为准」；Row 3 = 输出文本（纯展示）。设置页开关默认关；`AiSection` 与按钮随 `AiEnabled` 显隐（Loaded 同步）。
- **走查抓到并修复缺陷**：`AiExplainButton` 在 XAML 硬编码 `Visibility="Collapsed"` 且无任何代码置为可见（按钮永远点不到，仅免责文本可见）；修复为 `MainWindow.xaml.cs` Loaded 时随 AiEnabled 同步 `AiExplainButton.Visibility`。
- 注入降权展示落地：`Suspicious=true` 时输出加前缀「⚠ 输出疑似包含提示注入指令，已降权标记（仅展示、永不执行）：」（资源键 `AiSuspiciousPrefix`）。
- 无障碍：本次走查全程经 UIA 无障碍树完成（AXSelect 选行、AXPress 触发按钮、文本取证）——按钮/免责/输出在树中语义可达；样式沿用主窗口已验收主题（DPI 三档/高对比度/Narrator 主体已在 v1-core 03 验收）；Esc 关窗沿用 WindowKeyboard。
- 真机走查证据（pid 17900，无障碍树取证，全程只读扫描）：选中「ZCode 桌面版更新器安装包」→ 点 AI 解释 → 正常路径输出解释文本、无前缀；免责文本持续可见；按钮请求期间禁用、完成后恢复。

