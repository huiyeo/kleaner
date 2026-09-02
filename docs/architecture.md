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
- **`StartupWindow` 的表头未完全走本地化**：XAML 里硬编码了列头，再在 `LoadStrings()` 里按列索引覆盖。列顺序一旦调整，文案就会错位。（`MainWindow`/`ToolboxWindow`/`QuarantineWindow` 同样按列索引设表头——列顺序调整时需同步，见各窗口 `LoadStrings()`。）
- **`RuleUpdateService` 的本地覆盖会静默生效**：`%APPDATA%\Kleaner\rules\rules.v1.json` 存在时优先于内置规则库。排查"改了 rules.v1.json 却没生效"时先看这里。
- **WPF 层零控件测试**：`Kleaner.Core.Tests` 不引用 App。可剥离的纯逻辑会下沉到 `Kleaner.Analysis`/`Kleaner.Core` 并补测试（例：`DuplicateSelectionPolicy`、`ScanEngine.Scan` 的取消语义）。

## 前端工程约定（本次优化新增）

- **WPF 官方 Fluent 主题**：`App.xaml` 通过 `MergedDictionaries` 引入 `PresentationFramework.Fluent;component/themes/fluent.xaml`（.NET 6+ 运行时内置，零 NuGet 依赖）。全局控件样式被其接管，窗口内硬编码的尺寸/颜色不受影响；改主题前需回归 7 个窗口的 DataGrid/StatusBar。
- **MVVM 底座（CommunityToolkit.Mvvm 8.4.2）**：`Kleaner.App.csproj` 引入 `CommunityToolkit.Mvvm`。约定：
  - 行模型继承 `ObservableObject`，可写属性用 `[ObservableProperty]` 源生成器（`RuleRow` 已示范：`IsSelected`），派生显示属性靠 `OnPropertyChanged(nameof(...))` 手动通知（如 `Apply` 后刷新 `FileCount`/`SizeDisplay`/`Note`）。
  - 窗口命令用 `[RelayCommand]`/`AsyncRelayCommand`，状态用 `[ObservableProperty]` + `[NotifyCanExecuteChangedFor]` 驱动按钮可用性，替代 `SetBusy`/`SetIdle` 样板（`MainWindowViewModel` 已示范：`ScanCommand`/`CancelScanCommand`/`CleanCommand` + `IsBusy`）。
  - ViewModel 不直接 `new Window`；跨窗口导航通过事件（`OpenWindowRequested`）交由 code-behind 打开，保持 ViewModel 不依赖具体 View。
  - **本地化加载时机的坑**：`S.Get(key)` 查不到时**静默回退为英文键名**（`S.cs` 的 `TryGetValue` 失败分支），不会报错，只在界面上显示成 `BtnScan` 之类。因此：
    - `Program.Main` 在创建任何窗口前调用一次 `S.Load()`（全局保险）；
    - 窗口构造函数里**不要用字段初始化器创建 ViewModel**——字段初始化器早于构造函数体执行，会导致 ViewModel 在 `S.Load()` 之前构造，界面文字整体变英文（`MainWindow` 已按此修正）。
  - **仍有遗留**：`MessageBox` 直接出现在 ViewModel 内（`MainWindowViewModel.LoadRules`/`ScanAsync`/`CleanAsync`），可测性打折，后续可抽 `IDialogService`；`MainWindow` 的 `LoadStrings` 仍按列索引设 DataGrid 表头。
- **扫描支持取消**：`ScanEngine.Scan(RuleSet, CancellationToken)` 在规则与文件循环中检查取消。`MainWindow` 提供「取消扫描」按钮并禁止重入（`_scanCts` 非空时忽略重复触发）；`ToolboxWindow` 每次扫描前取消并释放旧 `_cts`。
- **永久删除的确认强度**：`QuarantineManager.DeleteBatch`/`PurgeOlderThan` 是全仓仅有的永久删除出口（`TryDeleteDir` 递归删除），GUI 已要求二次确认并明示「不可还原」，强度高于可还原的清理流程。
