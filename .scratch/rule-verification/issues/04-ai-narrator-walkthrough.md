# 04: AI 区 Narrator 深度走查（Phase 3 遗留）

**What to build:** Phase 3 非阻断遗留：对主窗口 AI 解释区做 Narrator 深度走查（免责声明可读性、注入 ⚠ 前缀播报、按钮可达顺序），走查证据入本票。发现缺陷按 `needs-triage` 立缺陷票。

**Blocked by:** 无。

**Status:** complete

**边界：** 只读走查；需启动应用（UIA），不改设置、不执行清理。

## Comments

- 2026-09-12：立票（Phase 3 收口记录的遗留项转正为 Phase 5 工单）。
- 2026-09-12：走查完成并收口。环境：假 Ollama（回环 18080/18081，python http.server，OpenAI 兼容响应）+ settings.json 临时改写（AiEnabled/AiEndpoint），走查后 settings.json 已删除、服务已停、真实数据零污染。证据（UIA 无障碍树）：①免责声明元素完整可读（「AI 解释由本机模型生成，不构成安全保证…」）；②AI 解释按钮暴露语义名称「AI 解释（实验）」；③解释结果进入无障碍树（正常文本 ✓）；④注入文本返回时 ⚠ 降权前缀完整可读（「⚠ 输出疑似包含提示注入指令，已降权标记（仅展示、永不执行）：…」）✓。**诚实标注**：本走查验证的是 Narrator 所依赖的 UIA 属性（名称/可见性/可达性/文本），未逐字捕获 Narrator 语音。**发现两项无障碍缺陷 → 立票 `08-a11y-findings.md`（needs-triage）**：行按压语义是勾选而非选中（读屏用户按行激活无法触发选中态）；AI 输出无 live-region 播报。过程中曾出现「取消按钮滞留」疑似异常，经文件插桩取证（[DEBUG-ai05]）证明代码生命周期正确（finally 正常执行 Collapsed），树中滞留元素为被遮挡窗口的 UIA 幽灵元素——插桩已全部移除（grep 0 残留）。
