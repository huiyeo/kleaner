# 13: 本机 `dotnet test` 静默不执行测试

**What to investigate:** 上一会话（2026-09-06 交接）发现本机 `dotnet test Kleaner.slnx -c Release` 与 `dotnet test tests/Kleaner.Core.Tests/...csproj -c Release`（含 `--no-build`）只做还原+构建即退出 0，无任何测试运行；Git Bash 与 cmd 均复现；间接证据为 `IsTestProject` 属性求值为空。当时以 `dotnet vstest <测试 dll>` 作为可靠替代，并怀疑 Microsoft.NET.Test.Sdk 17.14.1 与 SDK 10.0.400 的组合不兼容。本工单负责定位根因。

**Blocked by:** None。

**Status:** complete

## 根因（2026-09-06 本会话查实）

**NuGet 全局包缓存整体损坏**：`C:\Users\<用户>\.nuget\packages\` 下所有包目录只剩骨架（版本目录与 nupkg 存在，`*.nuspec` 与 `lib/` 内容被清空，共 18 个包确认缺失）。损坏时间点不明，但早于本会话。由于还原以 obj 下的 assets 文件做增量判断，缓存内容损坏不会触发重新解压，损坏状态长期静默存续。

修复方式：`dotnet nuget locals global-packages --clear` + `dotnet restore Kleaner.slnx`。

修复后验证：

- `dotnet build Kleaner.slnx -c Release`：0 警告 0 错误
- `dotnet test Kleaner.slnx -c Release`：直接运行，152/152 通过

**结论：不存在 SDK 10.0.400 × Test.Sdk 17.14.1 的组合不兼容问题。** 交接文档中"必须用 vstest 替代"的限制解除；上一会话的构建 0/0 结论在增量未重编译场景下成立，全量重编译在当时同样会失败。

## 复现与实验记录

1. **损坏缓存上的全量构建失败**：App 工程报 5 条 MSB3106（包 DLL 路径找不到）+ App.xaml/MainWindow.xaml 各一条 MC3074（`BundledTheme`/`PackIcon` 无法解析，因 MaterialDesignThemes 包内容缺失）。
2. **只清空 `microsoft.net.test.sdk` 的 `lib` 内容**：`dotnet test --no-build` 仍正常运行 152/152——测试运行时用的是 bin 输出里的 testhost 副本，不直接依赖缓存内容。
3. **清空该包全部内容（含 nuspec）**：`dotnet test`（带还原）在还原阶段响亮失败 NU5037，不是静默跳过。

昨日"还原+构建成功但静默退出 0"的**确切**机制未能单独复现；与证据最一致的假设是：缓存部分损坏（props 缺失而 nuspec 尚存）+ 还原因 assets 最新而跳过重验，导致 `IsTestProject` 未被导入，`dotnet test` 将其视作非测试工程静默退出。该组合状态已不可重建，不再深究。

## Comments

- 若"测试静默跳过"复发，排查顺序：① `dotnet build` 是否出现 MSB3106/MC3074 → ② 检查 `~/.nuget/packages/<相关包>/` 是否缺 nuspec 或 lib 内容 → ③ 清缓存重还原。构建产品与缓存的可执行文件不是同一份，"vstest 通过、dotnet test 静默"同时出现时优先怀疑缓存。
