# 01: ADR 0004 前置威胁模型与选型提议

**What to build:** goals.md Phase 3 硬性前置：AI 预览发布前必须另写 ADR/威胁模型。`docs/adr/0004-local-ai-assistant-preview.md`（proposed）已完成——技术选型提议（OpenAI 兼容回环 API/默认 Ollama）、STRIDE 威胁模型摘要（提示注入/误信/隐私/供应链/拒绝服务五类缓解）、故障验收矩阵（实施票的验收清单）、替代方案排除（云端 LLM/内置模型均违反宪法）。

**Blocked by:** None。

**Status:** complete

- [x] ADR/威胁模型文档（goals.md Phase 3 硬性前置）✅ `docs/adr/0004-local-ai-assistant-preview.md`
- [x] 发送数据边界定义（脱敏元数据，无路径/身份——goals.md 边界）
- [x] 故障验收矩阵（实施票的验收清单）
- [x] 替代方案排除记录（云端 LLM、内置模型）

**待所有者确认（转 accepted 的条件）：** ①适配器选型（OpenAI 兼容 + 默认 Ollama 端点）；②解释交互形态（内嵌问答 vs 独立对话窗）。

**实施票（02–04）blocked 原因：** 本地模型服务未安装 + ADR 待 accepted。

## Comments
