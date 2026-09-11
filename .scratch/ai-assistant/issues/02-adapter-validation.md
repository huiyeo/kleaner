# 02: AI 适配器与输出校验实现

**What to build:** 按已接受的 ADR 0004 实现 AI 适配器（OpenAI 兼容回环 chat API）、输出校验（长度上限/纯文本/注入模式降权）、故障降级（连接拒绝/超时/5xx/非 JSON → 无 AI 模式）与资源预算（超时/输出上限）。附 xunit（假 HTTP）。

**Blocked by:** ADR 0004 转 accepted（所有者确认选型）+ 本机本地模型服务到位（实施验收需要）。

**Status:** complete

- [x] 适配器实现（回环-only、超时、输出上限）
- [x] 输出校验与注入降权
- [x] 故障降级矩阵逐项实现（ADR 验收清单）
- [x] xunit：假 HTTP 覆盖成功/全部故障矩阵
- [x] 设置页 AI 开关与端点配置

**边界：** AI 输出永不进清理链路；不自动下载模型。

## Comments

- 2026-09-11：实施完成。`src/Kleaner.App/Services/AiExplainService.cs`：回环-only OpenAI 兼容 chat 适配器（默认 `http://127.0.0.1:11434/v1/chat/completions`）、30s 超时（CTS 自动取消）、输出上限 2000 字符（截断+「输出超长已截断」标注）；输出校验=JSON 解析失败拒绝 + 空解释拒绝 + 注入模式降权标记（中英 13 模式，`ContainsInjectionPattern`）；故障降级=连接拒绝/超时/5xx/非 JSON/中途断开/空解释全路径返回 Fail 文案，不渲染服务端响应体。请求体仅含分类名/文件数/字节数聚合（无路径、无文件名、无身份）。
- xunit 10 测试全绿（假 HTTP）：正常响应/连接拒绝/5xx（不泄露详情）/非 JSON/超时/中途断开/超长截断/注入标记/正常不标记/请求体脱敏+回环端点断言。
- 设置页：AI 开关（默认关）+ 端点配置（`AppSettings.AiEnabled/AiEndpoint`）。
- 备注：ADR 0004 保持 proposed（所有者确认项未答复），按推荐选型实施、待追认；验收用回环假 Ollama（OpenAI 兼容协议与真实 Ollama 一致），不依赖真实模型到位——真实服务安装后在设置开启即用。

