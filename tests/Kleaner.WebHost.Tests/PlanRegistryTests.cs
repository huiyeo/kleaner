using Kleaner.Core;
using Kleaner.WebHost;

namespace Kleaner.WebHost.Tests;

/// <summary>PlanRegistry 纯逻辑测试（工单 12）：一次性 confirmToken 的校验、烧毁与并发先到先得。</summary>
public sealed class PlanRegistryTests
{
    private static CleanPlan SamplePlan() => new(
        new[] { new PlanItemView("rule-a", "规则A", false, 2, 150) },
        new[] { new PlanResolvedItem("rule-a", new FileCandidate[] { new(@"C:\tmp\a.log", 100, DateTime.UtcNow) }) },
        NeedsElevation: false,
        TotalFiles: 2,
        TotalBytes: 150);

    [Fact]
    public void Create_TokenOnlyVisibleInCreationView()
    {
        var registry = new PlanRegistry();
        var record = registry.Create(SamplePlan());

        Assert.False(record.Confirmed);
        Assert.False(string.IsNullOrEmpty(record.ToView(includeToken: true).ConfirmToken));
        Assert.Null(record.ToView(includeToken: false).ConfirmToken);
    }

    [Fact]
    public void TryConsume_CorrectToken_ConsumedOnce()
    {
        var registry = new PlanRegistry();
        var record = registry.Create(SamplePlan());
        var token = record.ToView(includeToken: true).ConfirmToken!;

        Assert.Equal(ConfirmOutcome.Consumed, record.TryConsume(token));
        Assert.True(record.Confirmed);
        Assert.Equal(ConfirmOutcome.AlreadyConfirmed, record.TryConsume(token));
        Assert.Null(record.ToView(includeToken: true).ConfirmToken);
    }

    [Fact]
    public void TryConsume_WrongToken_RejectedAndNotBurned()
    {
        var registry = new PlanRegistry();
        var record = registry.Create(SamplePlan());
        var token = record.ToView(includeToken: true).ConfirmToken!;

        Assert.Equal(ConfirmOutcome.BadToken, record.TryConsume("not-the-token"));
        Assert.Equal(ConfirmOutcome.BadToken, record.TryConsume(""));
        Assert.False(record.Confirmed);

        // 凭据未被错误请求烧毁，原持有人仍可确认
        Assert.Equal(ConfirmOutcome.Consumed, record.TryConsume(token));
    }

    [Fact]
    public void Get_UnknownPlan_ReturnsNull()
    {
        var registry = new PlanRegistry();
        registry.Create(SamplePlan());

        Assert.Null(registry.Get("nonexistent"));
    }

    [Fact]
    public void View_CarriesPlanSummary()
    {
        var registry = new PlanRegistry();
        var view = registry.Create(SamplePlan()).ToView(includeToken: true);

        Assert.False(view.NeedsElevation);
        Assert.Equal(2, view.TotalFiles);
        Assert.Equal(150, view.TotalBytes);
        var item = Assert.Single(view.Items);
        Assert.Equal("rule-a", item.RuleId);
    }
}
