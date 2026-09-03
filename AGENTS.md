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
- 提交信息、注释语言、UI 文案通道遵循 `docs/conventions.md`。

## 按需查阅

| 触发条件 | 读 |
|---|---|
| 增删规则、改 schema、动 `safetyNotes` / `verified` / `keepNewest` | `docs/rules.md` |
| 动隔离区、还原、manifest、history、CLI 安全契约、提权 | `docs/deletion-path.md` |
| 找某个类在哪、判断新代码该放哪个工程、改 csproj 依赖 | `docs/architecture.md` |
| 术语拿不准、同一个词在不同处含义冲突 | `docs/context.md` |
| 写提交信息、写注释、新增界面文案、改代码风格配置 | `docs/conventions.md` |

## 环境备注

- 构建、测试、运行命令以 `README.md`「开发」节为准，此处不复述。
- .NET 装在非默认位置时，框架依赖启动需设 `DOTNET_ROOT` 指向运行时目录。启动报"找不到运行时"先查这个。

## 与上层 AGENTS.md 的关系

`D:\Projects\AGENTS.md` 覆盖 `D:\Projects` 下所有项目，面向办公文档场景（周报、方案、PPT）。本文件只管 Kleaner。两者冲突时**本文件优先**（更靠近工作区）。

## Agent skills

### Issue tracker

工单以本地 Markdown 存于 `.scratch/`，一个功能一个目录。见 `docs/agents/issue-tracker.md`。

### Triage labels

五个角色与标签同名：`needs-triage`、`needs-info`、`ready-for-agent`、`ready-for-human`、`wontfix`。见 `docs/agents/triage-labels.md`。

### Domain docs

单上下文：术语表在 `docs/context.md`，决策记录约定在 `docs/adr/`。见 `docs/agents/domain.md`。
