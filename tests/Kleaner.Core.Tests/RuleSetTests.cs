using Kleaner.Core;
using Xunit;

namespace Kleaner.Core.Tests;

public class RuleSetTests
{
    private static RuleSet LoadShipped()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "rules.v1.json");
        return RuleSetLoader.LoadFromFile(path);
    }

    [Fact]
    public void 随库规则_校验通过()
    {
        var set = LoadShipped();
        Assert.True(set.Rules.Count >= 5);
        Assert.Empty(RuleSetLoader.Validate(set));
    }

    [Fact]
    public void 分类默认_年龄解析正确()
    {
        var set = LoadShipped();
        Assert.Equal(14, set.EffectiveAgeDays(set.Rules.Single(r => r.Id == "user-temp")));
        Assert.Equal(7, set.EffectiveAgeDays(set.Rules.Single(r => r.Id == "chrome-http-cache")));
        Assert.Equal(14, set.EffectiveAgeDays(set.Rules.Single(r => r.Id == "npm-cache")));
    }

    [Fact]
    public void 更新器规则_采用版本保留()
    {
        var set = LoadShipped();
        var updater = set.Rules.Single(r => r.Id == "kimi-desktop-updater");
        Assert.Equal(1, updater.KeepNewest);
        Assert.Null(set.EffectiveAgeDays(updater));
    }

    [Fact]
    public void 无阈值规则_被拒绝()
    {
        var rule = new Rule("bad-rule", "坏规则", RuleCategory.Application, RiskLevel.Low,
            new[] { "%LOCALAPPDATA%\\x\\**" }, Array.Empty<string>(),
            AgeDays: null, KeepNewest: null, RequiresElevation: false, Enabled: true,
            SafetyNotes: "一条没有年龄阈值也没有 keepNewest 的规则");
        var set = new RuleSet(1, null, null, new[] { rule });
        Assert.Contains(RuleSetLoader.Validate(set), e => e.Contains("bad-rule"));
    }
}
