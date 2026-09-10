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
    public void 随库规则_都有非空验证状态与安全说明锚点()
    {
        var set = LoadShipped();

        Assert.All(set.Rules, rule =>
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Verified), $"规则 {rule.Id} 缺少 verified");
            Assert.False(string.IsNullOrWhiteSpace(rule.SafetyDoc), $"规则 {rule.Id} 缺少 safetyDoc");
        });
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

    [Fact]
    public void 撤回字段往返加载且缺省时为未撤回()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "rules": [
            {
              "id": "retired",
              "name": "已撤回规则",
              "category": "temp",
              "risk": "low",
              "paths": ["%TEMP%\\x/**"],
              "ageDays": 7,
              "requiresElevation": false,
              "safetyNotes": "撤回字段往返加载的测试规则，说明长度超过二十个字。",
              "deprecated": true,
              "deprecationReason": "2026-09 发现误伤案例，全网撤回待复核"
            },
            {
              "id": "normal",
              "name": "普通规则",
              "category": "temp",
              "risk": "low",
              "paths": ["%TEMP%\\y/**"],
              "ageDays": 7,
              "requiresElevation": false,
              "safetyNotes": "未撤回的普通规则说明，长度同样超过二十个字的限制要求。"
            }
          ]
        }
        """;
        var set = RuleSetLoader.LoadFromJson(json);

        var retired = Assert.Single(set.Rules, r => r.Id == "retired");
        Assert.True(retired.Deprecated);
        Assert.Equal("2026-09 发现误伤案例，全网撤回待复核", retired.DeprecationReason);
        var normal = Assert.Single(set.Rules, r => r.Id == "normal");
        Assert.False(normal.Deprecated);
        Assert.Null(normal.DeprecationReason);
    }
}