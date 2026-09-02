---
status: accepted
---

> **2026-09-02 起重新生效**：`Kleaner.App`（WPF）已恢复为唯一 GUI 入口，过渡期的 `Kleaner.WebHost` Web 前端已整体移除。
> 期间的状态变更（superseded → accepted）只反映 GUI 层技术栈的往返，本文件的技术结论（选型理由、MDIX 5.x 配置陷阱）始终有效。

# 采用 MaterialDesignThemes 5.3.2 作为 UI 主题库

Kleaner 此前使用 WPF 内置的官方 Fluent 主题（`PresentationFramework.Fluent`），但它只接管控件皮肤、不动布局骨架——9 个等宽 96px 按钮挤成一排、DataGrid 保持经典样式、窗口标题栏仍是 Win32 风格，观感只做到"不难看"。因此改用 `MaterialDesignThemes` 5.3.2，并**同步改造布局**（只换库不改布局会重蹈"老格局贴新壁纸"的覆辙），采用样板先行（先改 MainWindow 一个窗口验证风格再铺开）、严格使用边界（只用公开控件与主题资源，不改 ControlTemplate，保留将来换回的能力）。

配套决策：主题 Light、Primary=Indigo、Secondary=Amber、字体 Roboto、窗口标题栏用 `WindowChrome` 自绘、MainWindow 的 DataGrid 保留（信息密度优先）而底部说明区改 Card。

**Considered Options**

- **WPF-UI 4.3.0**：同样原生支持 net10.0-windows，自带 `FluentWindow` 标题栏、Navigation、Snackbar、Segoe Fluent 图标，且与现有 Fluent 同源、迁移断裂最小、标题栏开箱即用——技术上是更省事的方案。未被采用是因为用户明确选择 Material Design 观感。
- **MahApps.Metro 2.4.11**：最高只到 .NETCoreApp3.1，对本项目需 NU1701 回退，能编译但不保证行为正确。
- **HandyControl 3.5.1**：最高 net8.0，同样需回退。
- **AdonisUI 1.17.1**：最高 net5.0，过于陈旧。
- **保留官方 Fluent + 自写样式**：零第三方依赖，但 7 个窗口的布局与样式全部手写，工作量最大且缺乏设计系统支撑。

**Consequences**

- **窗口标题栏必须自绘**：MDIX 主包不含自定义窗口标题栏；提供 `MaterialWindow` 的 `MaterialDesignExtensions` 停在 3.3.0（依赖旧版主包，与 5.3.2 差两个大版本、不可用）。因此改用 WPF 原生 `WindowChrome` 自绘，约 100–150 行 XAML，需自行处理拖拽与最小化/最大化/关闭按钮。
- **Roboto 字体全量引入**：`IncludeMaterialDesignFont=True` 会把 18 个 ttf（约 2.9 MB）复制进输出目录，其中实际用到的 Regular/Medium/Bold 仅约 480 KB。先用官方全量方式保持零自定义，待 7 个窗口铺完、字体用量明确后再按需精简。
- **约 144 处硬编码外观属性待清理**：分布在 7 个窗口（ToolboxWindow 39 处最重，MainWindow 与 AdvancedWindow 各 25 处），需建立统一的间距/尺寸/颜色资源字典。注意其中**尺寸类**（Width/Height/Margin）才是拥挤感的来源，只清颜色和字体不会改善观感。
- **配置与 4.x 教程不通用**：5.0 有 breaking change，`App.xaml` 必须用 `BundledTheme` + `MaterialDesign2.Defaults.xaml`；网上大量基于 4.x 的中文教程在此版本上会报错，排查时勿照抄。
- **现有 Fluent 主题字典需移除**：两者同时合并会导致控件样式相互覆盖，迁移时 `App.xaml` 中 `PresentationFramework.Fluent` 的 `MergedDictionaries` 条目要删掉。
