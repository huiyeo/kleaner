using Kleaner.Core;
using Xunit;

namespace Kleaner.Core.Tests;

public sealed class RuleSelectionPolicyTests
{
    private static Rule MakeRule(string? verified) => new(
        Id: "r",
        Name: "测试规则",
        Category: RuleCategory.Application,
        Risk: RiskLevel.Low,
        Paths: Array.Empty<string>(),
        Exclude: Array.Empty<string>(),
        AgeDays: 7,
        KeepNewest: null,
        RequiresElevation: false,
        Enabled: true,
        SafetyNotes: "仅用于单元测试构造规则对象，不影响真实环境。",
        Verified: verified);

    [Theory]
    [InlineData(null, true)]                 // 未声明 verified 的旧规则视同已验证
    [InlineData("本机实测", true)]            // 精确前缀
    [InlineData("本机实测：2026-08-01 于本机清理验证", true)] // 前缀 + 补充说明
    [InlineData("本机实测验", true)]          // 前缀出现在更长的词首，按 Ordinal 前缀语义仍成立
    [InlineData("文档依据：官方文档说明", false)]  // 非「本机实测」开头 → 不默认勾选
    [InlineData("文档+本机实测", false)]       // 「本机实测」不在开头 → 不默认勾选
    [InlineData("", false)]                   // 空串不是「本机实测」开头
    public void IsDefaultSelectable_各verified形态分支(string? verified, bool expected)
    {
        Assert.Equal(expected, RuleSelectionPolicy.IsDefaultSelectable(MakeRule(verified)));
    }
}
