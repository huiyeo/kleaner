# Phase 3 · 本地 AI 助手预览（工单地图）

> 目标出处：`docs/goals.md` Phase 3。前置 ADR：`docs/adr/0004-local-ai-assistant-preview.md`（proposed）。
> goals.md 硬约束：AI 预览发布前必须另写 ADR/威胁模型 ✅；正式版只连本机回环上的本地模型服务；AI 是可选解释层。

## Notes

- 前置 ADR 0004（proposed）：技术选型提议（OpenAI 兼容回环 API/默认 Ollama 端点）+ STRIDE 威胁模型 + 故障验收矩阵。
- 2026-09-11：按推荐选型实施完成（适配器/UI/故障矩阵），验收用回环假 Ollama（协议一致）代理真实模型；真实本地模型服务安装后在设置开启即用（端到端待用户环境）。

## Decisions-so-far

- 2026-09-10：ADR 0004 起草（proposed）。两项待所有者确认：①适配器选型（OpenAI 兼容+默认 Ollama 端点）；②解释交互形态（结果页内嵌问答 vs 独立对话窗）。
- 2026-09-11：所有者确认项未答复，按推荐选项作为工作假设实施（①OpenAI 兼容回环适配器、②内嵌入口形态）；ADR 0004 保持 proposed 待追认，若追认时改选独立对话窗，仅 UI 层需调整、适配器与校验不变。

## Fog

- 本地模型服务的具体选型与硬件资源预算（显存/内存）需要所有者环境信息。
- 深度 Narrator/无障碍在 AI 界面上的要求尚未定义。

## 工单索引

| # | 工单 | 状态 |
|---|---|---|
| 01 | ADR 0004 前置威胁模型与选型提议 | complete |
| 02 | AI 适配器与输出校验实现 | complete |
| 03 | 解释 UI 与免责标注 | complete |
| 04 | 故障验收矩阵执行 | complete |
