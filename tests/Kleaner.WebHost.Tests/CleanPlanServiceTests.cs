using Kleaner.Core;
using Kleaner.WebHost;

namespace Kleaner.WebHost.Tests;

/// <summary>CleanPlanService 纯逻辑测试（工单 12）。</summary>
public sealed class CleanPlanServiceTests
{
    private static ScanResultEnvelope SampleScan() => new(
        DateTime.UtcNow,
        new ScanRuleView[]
        {
            new("rule-a", "规则A", "temp", "low", false, MachineVerified: true, 2, 150,
                "安全说明占位，满足二十字以上的校验要求。", null,
                new FileCandidate[]
                {
                    new(@"C:\tmp\a.log", 100, DateTime.UtcNow),
                    new(@"C:\tmp\b.log", 50, DateTime.UtcNow),
                }),
            new("rule-empty", "空规则", "temp", "low", false, true, 0, 0,
                "安全说明占位，满足二十字以上的校验要求。", null, Array.Empty<FileCandidate>()),
            new("rule-elev", "系统级规则", "system", "medium", RequiresElevation: true, MachineVerified: false, 1, 50,
                "安全说明占位，满足二十字以上的校验要求。", null,
                new FileCandidate[] { new(@"C:\sys\old\file.bin", 50, DateTime.UtcNow) }),
        },
        Array.Empty<string>());

    [Fact]
    public void Build_FiltersZeroFileRules_AndSummarizes()
    {
        var plan = CleanPlanService.Build(new[] { "rule-a", "rule-empty" }, SampleScan(), isElevated: true);

        Assert.Equal(new[] { "rule-a" }, plan.Items.Select(i => i.RuleId).ToArray());
        Assert.Equal(2, plan.TotalFiles);
        Assert.Equal(150, plan.TotalBytes);
        Assert.False(plan.NeedsElevation);

        var resolved = Assert.Single(plan.Resolved);
        Assert.Equal("rule-a", resolved.RuleId);
        Assert.Equal(2, resolved.Files.Count);
        Assert.All(resolved.Files, f => Assert.StartsWith(@"C:\tmp\", f.FullPath));
    }

    [Fact]
    public void Build_ItemsFollowScanOrder_NotCheckedOrder()
    {
        var plan = CleanPlanService.Build(new[] { "rule-elev", "rule-a" }, SampleScan(), isElevated: true);

        Assert.Equal(new[] { "rule-a", "rule-elev" }, plan.Items.Select(i => i.RuleId).ToArray());
        Assert.Equal(3, plan.TotalFiles);
        Assert.Equal(200, plan.TotalBytes);
    }

    [Fact]
    public void Build_NeedsElevation_True_WhenElevationRuleCheckedAndNotElevated()
    {
        var plan = CleanPlanService.Build(new[] { "rule-elev" }, SampleScan(), isElevated: false);

        Assert.True(plan.NeedsElevation);
        // 计划照常产出（含文件），只是执行端拒绝——语义同 GUI「提示重启提权」
        Assert.Equal(1, plan.TotalFiles);
    }

    [Fact]
    public void Build_NeedsElevation_False_WhenProcessElevated()
    {
        var plan = CleanPlanService.Build(new[] { "rule-elev" }, SampleScan(), isElevated: true);

        Assert.False(plan.NeedsElevation);
    }

    [Fact]
    public void Build_NeedsElevation_True_EvenWhenZeroFiles()
    {
        // 零文件的提权规则也要拦：语义在勾选集上判定、先于零文件过滤（对齐 GUI）
        var scan = new ScanResultEnvelope(
            DateTime.UtcNow,
            new ScanRuleView[]
            {
                new("rule-empty-elev", "零文件提权规则", "system", "medium", true, false, 0, 0,
                    "安全说明占位，满足二十字以上的校验要求。", null, Array.Empty<FileCandidate>()),
            },
            Array.Empty<string>());

        var plan = CleanPlanService.Build(new[] { "rule-empty-elev" }, scan, isElevated: false);

        Assert.True(plan.NeedsElevation);
        Assert.Empty(plan.Items);
        Assert.Empty(plan.Resolved);
        Assert.Equal(0, plan.TotalFiles);
    }

    [Fact]
    public void Build_RejectsUnknownRuleId()
    {
        Assert.Throws<ArgumentException>(() =>
            CleanPlanService.Build(new[] { "rule-a", "rule-unknown" }, SampleScan(), isElevated: true));
    }

    [Fact]
    public void Build_RejectsEmptySelection()
    {
        // 不复刻 CLI「--rule 缺省静默成功」的坑（deletion-path.md）
        Assert.Throws<ArgumentException>(() =>
            CleanPlanService.Build(Array.Empty<string>(), SampleScan(), isElevated: true));
    }
}
