# 架构与模块边界

## 工程清单

7 个工程，分三个虚拟文件夹（`Kleaner.slnx`）。全部面向 .NET 10，Windows 专属（`net10.0-windows`），仅 `Kleaner.Core` 例外。

| 工程 | TFM | 类型 | 引用 |
|---|---|---|---|
| `Kleaner.Core` | `net10.0` | 库 | 无（零依赖） |
| `Kleaner.Analysis` | `net10.0-windows` | 库 | 无（零依赖） |
| `Kleaner.Executor` | `net10.0-windows` | 库 | Core |
| `Kleaner.SpecialOps` | `net10.0-windows` | 库 | Core |
| `Kleaner.App` | `net10.0-windows` | `WinExe`，`UseWPF` | Core, Executor, SpecialOps, Analysis |
| `Kleaner.ScanCli` | `net10.0-windows` | `Exe` | Core, Executor, Analysis |
| `Kleaner.Core.Tests` | `net10.0-windows` | 库（xunit） | Core, Executor, SpecialOps, Analysis |

依赖**单向无环**。没有 `Directory.Build.props`、`global.json`、`.config/dotnet-tools.json`——无 SDK 版本锁定，无集中包管理。

两个叶子值得记住：

- **Core 不引用任何东西**（连 Windows 专属 TFM 都不用），可移植性最强。
- **Analysis 也不引用任何东西**，包括 Core。它靠 `FileCandidate` 这类记录与 Core 在 App/CLI 层组合，而非编译期耦合。

`Kleaner.ScanCli` 不引用 SpecialOps——高级模式（WSL、注册表、系统工具引导）只有 GUI 有。
`Kleaner.Core.Tests` 不引用 App——WPF 层无测试覆盖。

## 各工程源文件与职责

### Kleaner.Core — 规则与扫描（纯逻辑层）

| 文件 | 职责 |
|---|---|
| `RuleModels.cs` | `Rule` / `RuleSet` 记录、`RuleCategory` / `RiskLevel` 枚举 |
| `RuleSetLoader.cs` | JSON → 模型；`Validate` 语义校验；`EffectiveAgeDays` 阈值回退 |
| `RuleSelector.cs` | 对候选集应用 `keepNewest` 或年龄阈值（二者互斥） |
| `GlobScanner.cs` | `%ENV%` 展开、`*` / `**` 通配枚举、reparse point 排除 |
| `ScanEngine.cs` | 按规则枚举候选 → exclude → 选择，产出 `ScanReport`。**只读** |
| `RuleUpdateService.cs` | 规则在线更新：下载 → SHA512 校验 → 语义校验 → 落用户目录 |

*注意*：README 称 Core 是"纯逻辑、无 UI 依赖"，方向正确但不完全——`RuleUpdateService` 做了 HTTP 下载与文件写入。它无 UI 依赖，但不是无 IO。

### Kleaner.Analysis — 空间分析（零依赖）

| 文件 | 职责 |
|---|---|
| `FileWalker.cs` | 目录遍历 |
| `LargeFileScanner.cs` | 大文件扫描 |
| `DuplicateFinder.cs` | 重复文件查找（内容指纹） |
| `DiskUsageAnalyzer.cs` | 空间占用排行 |
| `TreemapLayout.cs` | Squarified 矩形图布局（`TreemapLayout.Squarify`） |

不依赖 Core，也不依赖 Windows API 之外的任何东西，可独立复用与测试。

### Kleaner.Executor — 副作用层

| 文件 | 职责 |
|---|---|
| `QuarantineManager.cs` | 隔离区：移入、还原、清空、批次清单 |
| `HistoryManager.cs` | 操作历史（只追加 JSONL） |
| `StartupManager.cs` | 启动项枚举 / 禁用 / 还原（含注册表与启动文件夹） |

**这一层是唯一的写入出口。** 详见 `deletion-path.md`。

### Kleaner.SpecialOps — 高级模式（只扫描与引导）

| 文件 | 职责 |
|---|---|
| `WslInspector.cs` | WSL vhdx 检测与压缩指引 |
| `SystemToolGuide.cs` | 大件跳转到系统工具 |
| `RegistryInspector.cs` | 注册表卸载残留**只读**扫描 |

按设计不直接改动系统项。

### Kleaner.App — WPF 界面

窗口：`MainWindow`（主界面）、`ToolboxWindow`（工具箱）、`AdvancedWindow`（高级模式）、`QuarantineWindow`（隔离区）、`HistoryWindow`（操作历史）、`SettingsWindow`、`StartupWindow`（启动项）。

支撑：`App.xaml.cs`、`Program.cs`（`StartupObject`）、`AppSettings.cs`、`RuleRow.cs`（规则行的展示与默认勾选策略）、`Helpers.cs`（`IsElevated` / `RestartElevated`）、`S.cs`（本地化）。

本地化文案集中在 `Resources/Strings.zh-CN.json`，**不硬编码在 XAML 里**（`StartupWindow` 有局部例外，见文末已知问题）。

### Kleaner.ScanCli — 命令行

单文件顶层语句 `Program.cs`。子命令：`scan`、`clean`、`large-files`、`duplicates`、`usage`、`startup`、`startup-test`。

## 入口点

- **GUI**：`src/Kleaner.App/Program.cs` 的 `Main`（csproj 里显式 `StartupObject=Kleaner.App.Program`），转到 `App.xaml.cs` → `MainWindow`。
- **CLI**：`tools/Kleaner.ScanCli/Program.cs` 顶层语句直接分派。

两者是**平级入口**，共享 Core / Executor / Analysis，各自组装。改动扫描或删除逻辑时，两个入口都要验证。

## 分层约束

新代码归属的判断顺序：

1. 纯计算、无 IO、无 Windows API → `Kleaner.Analysis`（若与空间分析相关）或 `Kleaner.Core`（若与规则相关）
2. 规则语义、路径匹配、候选选择 → `Kleaner.Core`
3. 移动文件、写注册表、写审计 → `Kleaner.Executor`
4. 只扫描不改动的系统项 → `Kleaner.SpecialOps`
5. 界面与交互 → `Kleaner.App`
6. 无界面的批量入口 → `Kleaner.ScanCli`

**不要**让 Core 反过来引用 Executor 或 App——依赖是单向的。

## 构建产物里的资源

`Kleaner.App` 会把 `rules/rules.v1.json` 与 `Resources/Strings.zh-CN.json` 复制到输出目录；`Kleaner.Core.Tests` 会复制 `rules/rules.v1.json`（校验用例需要真实规则库）。改规则文件名或位置时，这两个 csproj 都要同步。

## 已知问题

- **CLI 定位内置规则的.path 很脆**：`BundledRulesPath()` 从 `AppContext.BaseDirectory` 向上跳 5 级再拼 `rules/rules.v1.json`。输出目录层级一变就失效。
- **`StartupWindow` 的表头未完全走本地化**：XAML 里硬编码了列头，再在 `LoadStrings()` 里按列索引覆盖。列顺序一旦调整，文案就会错位。
- **`RuleUpdateService` 的本地覆盖会静默生效**：`%APPDATA%\Kleaner\rules\rules.v1.json` 存在时优先于内置规则库。排查"改了 rules.v1.json 却没生效"时先看这里。
- **WPF 层零测试**：`Kleaner.Core.Tests` 不引用 App。
