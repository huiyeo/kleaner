# 分析器存量告警清单

> 2026-09-03 启用 `.editorconfig` 警告级基线后的观察清单。处置策略：新代码零新增告警；存量告警盘点标注"修复 / 豁免"后，再评估是否升级为 error 级。

## 2026-09-04 更新

- 首盘的 3 个 xUnit2008 告警已清零（`GlobScannerTests` 改用 `Assert.Matches/DoesNotMatch` 的 Regex 实例重载，60/60 通过）。全仓当前 **0 告警**。
- 升级评估：具备直接升级 `error` 级的条件；按 2026-09-03 决策仍保留警告级观察至约定观察期结束，期满无新增即可收紧。

## 2026-09-03 首次盘点（全仓 Release 构建）

共 **3** 个告警，全部位于测试工程，源代码（src/、tools/）零告警：

| 位置 | 规则 | 内容 | 处置 |
|---|---|---|---|
| `tests/Kleaner.Core.Tests/GlobScannerTests.cs:104` | xUnit2008 | `Assert.True()` 做正则匹配，应改 `Assert.Matches` | 已修复（2026-09-04） |
| `tests/Kleaner.Core.Tests/GlobScannerTests.cs:105` | xUnit2008 | 同上 | 已修复（2026-09-04） |
| `tests/Kleaner.Core.Tests/GlobScannerTests.cs:106` | xUnit2008 | `Assert.False()` 做正则匹配，应改 `Assert.DoesNotMatch` | 已修复（2026-09-04） |

## 备注

- `S.Load` 词条文件缺失时的静默回退（界面显示 key 本身）已评估：显示 key 足够显眼、启动即暴露，**不引入日志依赖**，保持现状（2026-09-03 备注）。
