# 14 设置、规则更新与工具箱 API

Type: task
Status: open
Blocked by: 10, 11

## Task

1. 设置：读写 `%APPDATA%\Kleaner\settings.json`（现 3 项，含 `QuarantineRoot`——CLI 同读此文件，语义不变）。
2. 规则更新：`POST /api/rules/update` 薄端点直调 `RuleUpdateService`（留 Core 现状，校验链不动——动它要过 `docs/rules.md` 三关，本票不动）。
3. 工具箱：large-files / duplicates / usage 三个动作端点，job 化走 11（v1 只报开始 / 结束）；GUI 把伪规则 id 塞隔离区清单与历史的现状不复制——WebHost 层做规则关联时排除伪 id。

## Acceptance

- 设置读写与 CLI 兼容（同一文件、同一字段）。
- 规则更新校验链（SHA512 + 语义校验）零改动，既有测试背书。
- 三个工具箱操作可取消（cancel → 202），取消后无副作用残留（这三个操作本身只读，无删除路径）。

## Comments
