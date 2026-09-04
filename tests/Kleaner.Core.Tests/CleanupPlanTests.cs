using Kleaner.Core;
using Kleaner.Executor;

namespace Kleaner.Core.Tests;

public sealed class CleanupPlanTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kleaner-plan-" + Guid.NewGuid().ToString("N"));

    public CleanupPlanTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch { }
    }

    [Fact]
    public void 伪造的扫描结果不能把规则外路径签入计划()
    {
        var safeDir = Path.Combine(_root, "safe");
        var outsideDir = Path.Combine(_root, "outside");
        Directory.CreateDirectory(safeDir);
        Directory.CreateDirectory(outsideDir);
        var outside = Path.Combine(outsideDir, "outside.txt");
        File.WriteAllText(outside, "不要移动");
        File.SetLastWriteTimeUtc(outside, DateTime.UtcNow.AddDays(-10));

        var rule = RuleFor("safe-rule", safeDir);
        var set = new RuleSet(1, null, 7, new[] { rule });
        var forged = new ScanReport(
            DateTime.UtcNow,
            new[] { new RuleScanResult(rule.Id, rule.Name, rule.Category, rule.Risk, false, 1, 4,
                rule.SafetyNotes, new[] { new FileCandidate(outside, 4, File.GetLastWriteTimeUtc(outside)) }) },
            Array.Empty<string>());

        var plan = CleanupPlanBuilder.Create(set, forged, new[] { rule.Id });

        Assert.Empty(plan.Items);
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void 执行前文件变化会被复验跳过且保留原文件()
    {
        var source = Path.Combine(_root, "source");
        Directory.CreateDirectory(source);
        var file = Path.Combine(source, "cache.bin");
        File.WriteAllText(file, "old");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-30));
        var quarantine = Path.Combine(_root, "quarantine");
        var rule = RuleFor("cache-rule", source);
        var set = new RuleSet(1, null, 7, new[] { rule });
        var scan = new ScanEngine(quarantine).Scan(set);
        var plan = CleanupPlanBuilder.Create(set, scan, new[] { rule.Id }, quarantine);
        Assert.Single(plan.Items);

        File.WriteAllText(file, "changed");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow);
        var report = new QuarantineManager(quarantine, new HistoryManager(Path.Combine(_root, "history.jsonl"))).Execute(plan);

        Assert.Equal(0, report.MovedCount);
        Assert.Contains(report.Skipped, skipped => skipped.Contains(file, StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void 计划只包含用户选中的规则且带有规则集版本()
    {
        var firstDir = Path.Combine(_root, "first");
        var secondDir = Path.Combine(_root, "second");
        Directory.CreateDirectory(firstDir);
        Directory.CreateDirectory(secondDir);
        var first = Path.Combine(firstDir, "first.bin");
        var second = Path.Combine(secondDir, "second.bin");
        File.WriteAllText(first, "1");
        File.WriteAllText(second, "2");
        File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddDays(-30));
        File.SetLastWriteTimeUtc(second, DateTime.UtcNow.AddDays(-30));
        var firstRule = RuleFor("first-rule", firstDir);
        var secondRule = RuleFor("second-rule", secondDir);
        var set = new RuleSet(7, null, 7, new[] { firstRule, secondRule });
        var scan = new ScanEngine().Scan(set);

        var plan = CleanupPlanBuilder.Create(set, scan, new[] { firstRule.Id });

        Assert.Equal(7, plan.RuleSetSchemaVersion);
        var item = Assert.Single(plan.Items);
        Assert.Equal(firstRule.Id, item.RuleId);
        Assert.Equal(first, item.File.FullPath, ignoreCase: true);
    }

    private static Rule RuleFor(string id, string directory) => new(
        id, "测试规则", RuleCategory.Application, RiskLevel.Low,
        new[] { Path.Combine(directory, "**") }, Array.Empty<string>(),
        AgeDays: 7, KeepNewest: null, RequiresElevation: false, Enabled: true,
        SafetyNotes: "仅用于授权计划回归测试的临时缓存目录，不包含真实用户文件。");
}
