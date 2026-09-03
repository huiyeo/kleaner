# 分析器存量告警清单

> 2026-09-03 启用 `.editorconfig` 警告级基线后的观察清单。处置策略：新代码零新增告警；存量告警盘点标注"修复 / 豁免"后，再评估是否升级为 error 级。

## 2026-09-03 首次盘点（全仓 Release 构建）

共 **3** 个告警，全部位于测试工程，源代码（src/、tools/）零告警：

| 位置 | 规则 | 内容 | 处置 |
|---|---|---|---|
| `tests/Kleaner.Core.Tests/GlobScannerTests.cs:104` | xUnit2008 | `Assert.True()` 做正则匹配，应改 `Assert.Matches` | 待修复（工单化或顺手清理均可，语义等价） |
| `tests/Kleaner.Core.Tests/GlobScannerTests.cs:105` | xUnit2008 | 同上 | 待修复 |
| `tests/Kleaner.Core.Tests/GlobScannerTests.cs:106` | xUnit2008 | `Assert.False()` 做正则匹配，应改 `Assert.DoesNotMatch` | 待修复 |

## 结论

- 基线干净，具备升级为 `error` 级的条件；建议先清掉上述 3 个再收紧。
- `S.Load` 词条文件缺失时的静默回退（界面显示 key 本身）已评估：显示 key 足够显眼、启动即暴露，**不引入日志依赖**，保持现状（2026-09-03 备注）。
