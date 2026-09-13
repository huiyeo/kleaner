# Kleaner 项目指令

白名单制的 Windows C 盘清理工具。**只清理规则库里能说清为什么安全的条目**——无黑名单推断，无启发式猜测。

无人值守推进本项目时，以 `docs/goals.md` 为行动指令（阶段目标、自主推进规则、安全边界）；动删除路径前仍先读 `docs/deletion-path.md`。

## 四道保险（动删除路径前先读 `docs/deletion-path.md`）

1. **严格白名单**：不在 `rules/rules.v1.json` 里的，一律不清理。
2. **年龄阈值**：规则级 → 分类默认 → 全局默认逐级回退；`keepNewest` 豁免。
3. **强制预览**：CLI 不加 `--apply` 只打印计划并返回 0；GUI 走二次确认。
4. **隔离区可还原**：删除即移入。**全仓没有永久删除 API**——唯一出口是 `QuarantineManager.Execute` 里的 `File.Move`。

## 不可协商

- 删除类操作必须同时走 `QuarantineManager`（可还原）和 `HistoryManager`（可审计）。绕开任何一个即视为缺陷，不予合并。
- `Kleaner.Core` 的引擎改动必须附 xunit 用例。
- reparse point 一律排除；被占用文件跳过并在报告中提示，绝不强制删除。
- 规则的新增与修改必须过三关，见 `docs/rules.md`。
- 任何"可能含用户数据"的目录不予合并；拿不准的一律排除。
- AI 解释输出是纯展示文本，**永不进清理链路、永不改规则库、永不调用工具**；AI 面板默认关闭，用户在设置显式启用。边界详见 `docs/adr/0004-local-ai-assistant-preview.md` 与 goals.md「AI 长期边界」。
- 提交信息、注释语言、UI 文案通道遵循 `docs/conventions.md`。

## 按需查阅

| 触发条件 | 读 |
|---|---|
| 增删规则、改 schema、动 `safetyNotes` / `verified` / `maintainer` / `lastEvidenceCheck` / `keepNewest` | `docs/rules.md` |
| 动治理指标口径、目标值、误伤信号、维护统计 | `docs/rule-governance.md` |
| 评估新功能候选（先证明与「安全释放空间」直接相关） | `docs/phase4-tool-assessment.md` + goals.md 宪法第 8 条 |
| 动隔离区、还原、manifest、history、CLI 安全契约、提权 | `docs/deletion-path.md` |
| 发版：定版本号、打包、产物校验、安装矩阵 | `docs/release-checklist.md` |
| 动扫描/分析性能、跑基准、动 SLO 口径 | `docs/performance-baseline.md` + `scripts/bench.ps1` |
| 找某个类在哪、判断新代码该放哪个工程、改 csproj 依赖 | `docs/architecture.md` |
| 术语拿不准、同一个词在不同处含义冲突 | `docs/context.md` |
| 写提交信息、写注释、新增界面文案、改代码风格配置 | `docs/conventions.md` |

## 环境备注

- 构建、测试、运行命令以 `README.md`「开发」节为准，此处不复述。
- .NET 装在非默认位置时，框架依赖启动需设 `DOTNET_ROOT` 指向运行时目录。启动报"找不到运行时"先查这个。
- `Kleaner.App.exe` 运行中会锁构建产物（MSB3027）——重建前先通过 UI 关闭运行中的实例。
- `git push` 常被本机全局代理拦截（代理端口会漂移）：按次用 `git -c http.proxy= -c https.proxy= push` 绕过，不要永久改动用户的代理配置。
- 测试里创建目录联结用 `cmd /c mklink /J`，不要起 powershell.exe——其 5.1 冷启动在 CI runner 上会撞超时（曾致 CI 连续 11 次失败，见 QuarantineManifestTests.CreateJunction 注释）。
- `dotnet test` **静默 exit 0 且无任何输出** = NuGet 全局缓存骨架损坏（已复发两次）：`dotnet nuget locals global-packages --clear` 后 restore 重试；本机该缓存脆弱，遇到先查这里。
- 真机走查时**所有者可能正在使用机器**：弹出的应用窗口可能被手动关闭（良性），被遮挡/后台窗口的 UIA 树会出现幽灵元素（已消失的控件仍在树里）——判定应用行为以插桩日志等进程内证据为准，勿凭树下结论；走查弹窗前先说明，结束完整还原临时环境。
- 编辑工具可能把仓库内 LF 文件写成 CRLF：规则清单签名一律走 `scripts/sign-rules.ps1`（已内置 LF 归一化，2026-09-12 v1.2.0 首发曾因 CRLF 摘要与线上 blob 不符被线上核验拦下重签），勿绕过脚本手改签名流程。
- 真机走查/验收产生的临时环境（settings.json 改写、假服务端口、规则库 override）用完必须完整还原并核验真实数据未受影响；走查取证用 UIA 无障碍树（该应用像素捕获不可靠）。

## 与上层 AGENTS.md 的关系

`D:\Projects\AGENTS.md` 覆盖 `D:\Projects` 下所有项目，面向办公文档场景（周报、方案、PPT）。本文件只管 Kleaner。两者冲突时**本文件优先**（更靠近工作区）。

## Agent skills

### Issue tracker

工单以本地 Markdown 存于 `.scratch/`，一个功能一个目录。见 `docs/agents/issue-tracker.md`。

### Triage labels

五个角色与标签同名：`needs-triage`、`needs-info`、`ready-for-agent`、`ready-for-human`、`wontfix`。见 `docs/agents/triage-labels.md`。

### Domain docs

单上下文：术语表在 `docs/context.md`，决策记录约定在 `docs/adr/`。见 `docs/agents/domain.md`。
