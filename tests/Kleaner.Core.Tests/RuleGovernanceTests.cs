using Kleaner.Core;
using Kleaner.Executor;

namespace Kleaner.Core.Tests;

/// <summary>规则治理指标（Phase 2）：纯规则数据可计算，不依赖遥测或扫描。</summary>
public sealed class RuleGovernanceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kleaner-gov-" + Guid.NewGuid().ToString("N"));

    public RuleGovernanceTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static Rule MakeRule(
        string id,
        string notes = "治理指标测试规则的安全说明，长度超过二十个字以满足校验。",
        string? doc = "docs/safety-notes.md#x",
        string? verified = "本机实测",
        IReadOnlyList<string>? paths = null,
        RuleCategory category = RuleCategory.Temp) =>
        new(id, id, category, RiskLevel.Low,
            paths ?? ["%TEMP%\\kleaner-gov/**"],
            Array.Empty<string>(), 0, null, false, true, notes, doc, verified);

    [Fact]
    public void 全达标规则库各覆盖率为百分之百()
    {
        var set = new RuleSet(1, null, 0, new[]
        {
            MakeRule("a", category: RuleCategory.Temp),
            MakeRule("b", category: RuleCategory.BrowserCache),
        });

        var report = RuleGovernance.Report(set);

        Assert.Equal(2, report.TotalRules);
        Assert.Equal(2, report.EvidenceCoveredRules);
        Assert.Equal(1.0, report.EvidenceCoverage);
        Assert.Equal(2, report.VerifiedRules);
        Assert.Equal(1.0, report.VerifiedCoverage);
        Assert.Equal(2, report.DefaultSelectableRules);
    }

    [Fact]
    public void 缺失证据或验证的规则拉低覆盖率()
    {
        var set = new RuleSet(1, null, 0, new[]
        {
            MakeRule("full"),                                        // 全达标
            MakeRule("no-doc", doc: null, verified: null),           // 缺 safetyDoc 且未验证
            MakeRule("no-verified", verified: null),                 // 有 doc 但未验证
        });

        var report = RuleGovernance.Report(set);

        Assert.Equal(3, report.TotalRules);
        Assert.Equal(2, report.EvidenceCoveredRules);   // full 与 no-verified 都有 safetyDoc
        Assert.Equal(2.0 / 3, report.EvidenceCoverage, precision: 6);
        Assert.Equal(1, report.VerifiedRules);
        Assert.Equal(1.0 / 3, report.VerifiedCoverage, precision: 6);
        Assert.Equal(1, report.DefaultSelectableRules);
    }

    [Fact]
    public void 目标根按环境变量与盘符去重()
    {
        var set = new RuleSet(1, null, 0, new[]
        {
            MakeRule("t1", paths: ["%TEMP%\\a\\**"]),
            MakeRule("t2", paths: ["%TEMP%\\b\\**", "%LOCALAPPDATA%\\c\\**"]),
            MakeRule("t3", paths: ["D:\\games\\**"]),
        });

        var report = RuleGovernance.Report(set);

        Assert.Equal(3, report.UniqueTargetRoots); // %TEMP% %LOCALAPPDATA% D:
        Assert.Equal(3, report.TotalRules);
    }

    [Fact]
    public void 分类覆盖按六分类枚举计数()
    {
        var set = new RuleSet(1, null, 0, new[]
        {
            MakeRule("c1", category: RuleCategory.Temp),
            MakeRule("c2", category: RuleCategory.Temp),      // 同分类只计一次
            MakeRule("c3", category: RuleCategory.System),
        });

        var report = RuleGovernance.Report(set);

        Assert.Equal(2, report.CategoriesCovered);
        Assert.Equal(6, report.TotalCategories);
    }

    [Fact]
    public void 空规则库返回零值不抛异常()
    {
        var set = new RuleSet(1, null, 0, Array.Empty<Rule>());

        var report = RuleGovernance.Report(set);

        Assert.Equal(0, report.TotalRules);
        Assert.Equal(0, report.EvidenceCoverage);
        Assert.Equal(0, report.VerifiedCoverage);
        Assert.Equal(0, report.UniqueTargetRoots);
    }

    [Fact]
    public void 短安全说明不计入证据覆盖()
    {
        var set = new RuleSet(1, null, 0, new[]
        {
            MakeRule("short", notes: "太短"), // <20 字
        });

        var report = RuleGovernance.Report(set);

        Assert.Equal(0, report.EvidenceCoveredRules);
    }

    [Fact]
    public void 还原批次计为误伤信号且部分还原按文件数计明细()
    {
        var now = DateTime.UtcNow;
        var history = new HistoryManager(Path.Combine(_root, "signal-history.jsonl"));
        history.Append("restore", "批次 A（全部还原）", 2, 10, "ok");
        history.Append("clean", "非还原动作不计入", 1, 1, "ok");
        history.Append("restore", "批次 B（部分还原）", 1, 5, "ok");

        var entries = history.Recent(100)
            .Select(e => new RestoreSignalEntry(e.Action, e.FileCount))
            .ToList();
        var signal = RuleGovernance.RestoreSignal(entries);

        Assert.Equal(2, signal.RestoreEvents);
        Assert.Equal(3, signal.RestoredFiles);
    }

    [Fact]
    public void 空历史误伤信号为零()
    {
        var history = new HistoryManager(Path.Combine(_root, "empty-history.jsonl"));

        var signal = RuleGovernance.RestoreSignal(history.Recent(100)
            .Select(e => new RestoreSignalEntry(e.Action, e.FileCount)).ToList());

        Assert.Equal(0, signal.RestoreEvents);
        Assert.Equal(0, signal.RestoredFiles);
    }

    [Fact]
    public void 阈值检查_达标与不达标()
    {
        var target = new GovernanceTarget(EvidenceCoverage: 1.0, VerifiedCoverage: 0.25, CategoriesCovered: 6);
        var met = new RuleGovernanceReport(10, 10, 1.0, 3, 0.3, 3, 6, 6, 5);
        var (ok, warnings) = RuleGovernance.CheckTargets(met, target);
        Assert.True(ok);
        Assert.Empty(warnings);

        var below = new RuleGovernanceReport(10, 10, 1.0, 2, 0.2, 3, 5, 6, 5);
        var (ok2, warnings2) = RuleGovernance.CheckTargets(below, target);
        Assert.False(ok2);
        Assert.Contains(warnings2, w => w.Contains("验证覆盖率"));
        Assert.Contains(warnings2, w => w.Contains("分类覆盖"));
    }

    [Fact]
    public void 证据覆盖率跌出100立即警告()
    {
        var target = new GovernanceTarget(1.0, 0.25, 6);
        var report = new RuleGovernanceReport(10, 9, 0.9, 2, 0.2, 3, 5, 6, 5);
        var (ok, warnings) = RuleGovernance.CheckTargets(report, target);
        Assert.False(ok);
        Assert.Contains(warnings, w => w.Contains("证据覆盖率"));
    }
}