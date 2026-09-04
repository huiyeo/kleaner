# Kleaner 工程规范

所有提交者（人类与 Agent）共同遵守。AGENTS.md 引用本文件；与个人习惯冲突时以本文件为准。规范变更直接修改本文件，并在文末变更记录中注明。

## 1. 提交信息

- 格式：`<type>(<scope>): <中文 subject>`。type 与 scope 用英文小写，subject 用中文。
- type 取值：`feat` / `fix` / `docs` / `style` / `refactor` / `test` / `build` / `chore` / `perf` / `revert`。
- scope 按工程取值：`app` / `core` / `executor` / `analysis` / `specialops` / `cli` / `rules` / `release`；跨工程的变更省略括号。
- subject：中文祈使句，不加句号，≤50 字，说"做了什么"。
- body（可选）：中文，说"为什么"；关联工单写 `.scratch/` 内的编号。
- 示例：`fix(app): 启动时先加载 App.xaml 资源再实例化 MainWindow`

## 2. 注释

- 统一中文。
- 只写代码本身说不出来的约束：意图、安全边界、外部契约、坑。不复述代码。
- 公共类型与公共 API 写 XML 文档注释（`///`）。

## 3. UI 文案

- 面向用户的文案一律经 `Kleaner.App` 的 `S.Get` / `S.Format`，词条存于 `src/Kleaner.App/Resources/Strings.zh-CN.json`。
- XAML 与 C# 中不得硬编码面向用户的文案（含标点与数字单位）。
- `S.Get` 在词条缺失时静默回退返回 key 本身，构建不报错——新增 key 后必须肉眼核对界面或在走查中确认。

## 4. 代码风格与分析器

- 缩进、换行、命名以仓库根 `.editorconfig` 为准；不因个人编辑器习惯改动全局配置。
- 已完成观察的代码风格与可空性规则为 error 级；不设 `TreatWarningsAsErrors`，避免将 SDK、第三方或未来未评估的告警一并升级。新增或调整规则先以警告级观察，并在 `docs/analyzer-baseline.md` 清零后再收紧。

## 变更记录

- 2026-09-03 初版：提交信息 / 注释 / UI 文案 / 代码风格与分析器四节。
- 2026-09-05：既有分析器基线清零后，将七条已观察规则收紧为 error；继续不启用全局 `TreatWarningsAsErrors`。
