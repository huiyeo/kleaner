# 14 设置、规则更新与工具箱 API

Type: task
Status: resolved
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

- 2026-08-31 完成。设置端点 `GET/PUT /api/settings` 与 GUI/CLI 共用 `%APPDATA%\Kleaner\settings.json`，严格只读写 `QuarantineRoot`、`RuleUpdateUrl`、`RuleUpdateSha512` 三字段；WebHost 运行期隔离区根也改为同一设置存储解析。`POST /api/rules/update` 只读取已保存的更新地址与 SHA512，直接调用既有 `RuleUpdateService.CheckAndUpdateAsync`，下载、SHA512 与语义校验链零改动。工具箱 `POST /api/tools/large-files`、`/duplicates`、`/usage` 统一落入 11 的 job 注册表，返回 202 + jobId，可由既有 cancel 端点取消；只调用 Analysis 的只读扫描，不写隔离区或历史，伪规则 id 不再复刻 GUI 现状。新增 WebHost 集成测试覆盖 settings 文件兼容性、更新端点、三个动作的 job 结果和取消后零副作用；全量测试 Core 60/60 + WebHost 64/64 通过。
