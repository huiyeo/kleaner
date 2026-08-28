using Kleaner.Core;
using Xunit;

namespace Kleaner.Core.Tests;

public sealed class RuleSelectorTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static RuleSet EmptySet() => new(1, null, null, Array.Empty<Rule>());

    [Fact]
    public void 年龄阈值_仅保留超期文件()
    {
        var rule = new Rule("r", "r", RuleCategory.Temp, RiskLevel.Low,
            new[] { "%TEMP%\\x\\**" }, Array.Empty<string>(),
            AgeDays: 7, KeepNewest: null, RequiresElevation: false, Enabled: true, SafetyNotes: "测试规则说明，长度足够。");
        var set = new RuleSet(1, null, 7, new[] { rule });

        var old = new FileCandidate(@"C:\t\old.txt", 10, Now.AddDays(-10));
        var fresh = new FileCandidate(@"C:\t\new.txt", 10, Now.AddDays(-1));

        var selected = RuleSelector.Apply(new[] { old, fresh }, rule, set, Now);

        Assert.Single(selected);
        Assert.Equal(old, selected[0]);
    }

    [Fact]
    public void keepNewest_每组保留最新_豁免年龄()
    {
        var rule = new Rule("r", "r", RuleCategory.Updater, RiskLevel.Low,
            new[] { "%TEMP%\\u\\**\\*.exe" }, Array.Empty<string>(),
            AgeDays: null, KeepNewest: 1, RequiresElevation: false, Enabled: true, SafetyNotes: "测试规则说明，长度足够。");
        var set = EmptySet();

        var candidates = new[]
        {
            new FileCandidate(@"C:\u\app-1.0.exe", 10, Now.AddDays(-100)),
            new FileCandidate(@"C:\u\app-2.0.exe", 10, Now.AddDays(-1)),
            new FileCandidate(@"C:\u\pending\only.exe", 10, Now.AddDays(-50)), // 独立分组，唯一文件被保留
        };

        var selected = RuleSelector.Apply(candidates, rule, set, Now);

        // app-2.0（组内最新）与 only.exe（组内唯一）保留；仅 app-1.0 入选清理
        Assert.Single(selected);
        Assert.Equal(@"C:\u\app-1.0.exe", selected[0].FullPath, ignoreCase: true);
    }

    [Fact]
    public void 无任何阈值_安全默认返回空()
    {
        var rule = new Rule("r", "r", RuleCategory.Application, RiskLevel.Low,
            new[] { "%TEMP%\\x\\**" }, Array.Empty<string>(),
            AgeDays: null, KeepNewest: null, RequiresElevation: false, Enabled: true, SafetyNotes: "测试规则说明，长度足够。");

        var selected = RuleSelector.Apply(
            new[] { new FileCandidate(@"C:\t\a.txt", 1, Now.AddYears(-5)) }, rule, EmptySet(), Now);

        Assert.Empty(selected);
    }
}
