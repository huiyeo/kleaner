# 03: 解释 UI 与免责标注

**What to build:** 扫描结果页的 AI 解释入口与展示 UI（形态待所有者确认：内嵌问答 vs 独立对话窗），含强制免责标注「AI 解释不构成安全保证，以规则库 safetyNotes 为准」。

**Blocked by:** 02（适配器与校验）。

**Status:** blocked

- [ ] UI 形态实现（按所有者确认）
- [ ] 免责标注（每条 AI 输出可见处）
- [ ] 启用/关闭开关（默认关闭）与设置持久化
- [ ] 无障碍走查（工单 v1-core 03 同标准：DPI/键盘/对比度）

**边界：** AI 输出不修改任何状态；不替代 safetyNotes。

## Comments
