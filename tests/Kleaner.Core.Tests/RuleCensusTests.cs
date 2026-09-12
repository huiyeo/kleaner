using Kleaner.Core;
using Xunit;

namespace Kleaner.Core.Tests;

/// <summary>规则真机普查（Phase 5 工单 01）：只读证据档案的判定逻辑。夹具全部使用 GUID 临时目录。</summary>
public sealed class RuleCensusTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kleaner-census-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private Rule Rule(string id, params string[] paths) => new Rule(
        Id: id, Name: id, Category: RuleCategory.System, Risk: RiskLevel.Low,
        Paths: paths, Exclude: Array.Empty<string>(), AgeDays: 14, KeepNewest: null,
        RequiresElevation: false, Enabled: true, SafetyNotes: "普查测试规则，说明长度超过二十个字。");

    private static ScanReport Scan(params RuleScanResult[] results) =>
        new(DateTime.UtcNow, results, Array.Empty<string>());

    private static RuleScanResult ScanOf(string ruleId, int files) => new(
        ruleId, ruleId, RuleCategory.System, RiskLevel.Low, false,
        files, files * 100L, "n", Array.Empty<FileCandidate>());

    [Fact]
    public void 判定_存在且有命中_归入转正候选()
    {
        Directory.CreateDirectory(Path.Combine(_root, "present"));
        var set = new RuleSet(1, null, 14, new[] { Rule("present-hit", _root + "\\present\\**") });
        var scan = Scan(ScanOf("present-hit", 2));

        var report = RuleCensus.Build(set, scan);

        var e = Assert.Single(report.Entries);
        Assert.Equal(CensusVerdict.Present, e.Verdict);
        Assert.Equal(2, e.ScanFileCount);
        Assert.Equal(1, report.Present);
        Assert.Equal(1, report.PresentWithHits);
        Assert.Equal(0, report.PresentNoHits);
        Assert.Equal(0, report.AbsentOrDenied);
    }

    [Fact]
    public void 判定_存在但零命中_不算转正候选()
    {
        Directory.CreateDirectory(Path.Combine(_root, "empty"));
        var set = new RuleSet(1, null, 14, new[] { Rule("present-nohit", _root + "\\empty\\**") });
        var scan = Scan(ScanOf("present-nohit", 0));

        var report = RuleCensus.Build(set, scan);

        Assert.Equal(CensusVerdict.Present, Assert.Single(report.Entries).Verdict);
        Assert.Equal(1, report.PresentNoHits);
        Assert.Equal(0, report.PresentWithHits);
    }

    [Fact]
    public void 判定_路径不存在_归入AbsentOrDenied()
    {
        var set = new RuleSet(1, null, 14, new[] { Rule("absent", _root + "\\nope\\**") });
        var scan = Scan(ScanOf("absent", 0));

        var report = RuleCensus.Build(set, scan);

        Assert.Equal(CensusVerdict.AbsentOrDenied, Assert.Single(report.Entries).Verdict);
        Assert.Equal(1, report.AbsentOrDenied);
    }

    [Fact]
    public void 判定_多模式任一存在即Present且逐模式留档()
    {
        Directory.CreateDirectory(Path.Combine(_root, "real"));
        var set = new RuleSet(1, null, 14, new[] { Rule("mixed", _root + "\\missing\\**", _root + "\\real\\**") });
        var scan = Scan();

        var report = RuleCensus.Build(set, scan);

        var e = Assert.Single(report.Entries);
        Assert.Equal(CensusVerdict.Present, e.Verdict);
        Assert.Equal(2, e.Patterns.Count);
        Assert.Contains(e.Patterns, p => !p.ProbeExists);
        Assert.Contains(e.Patterns, p => p.ProbeExists);
    }

    [Fact]
    public void 判定_精确路径模式_探测目标本身()
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "exact.bin");
        File.WriteAllText(file, "1");
        var set = new RuleSet(1, null, 14, new[] { Rule("exact", file, _root + "\\nope\\gone.bin") });
        var scan = Scan();

        var report = RuleCensus.Build(set, scan);

        var e = Assert.Single(report.Entries);
        Assert.Equal(CensusVerdict.Present, e.Verdict);
        Assert.Equal(2, e.Patterns.Count);
        Assert.Equal(2, e.Patterns.Count(p => p.IsExactPath));
        Assert.Contains(e.Patterns, p => p.ProbeExists);
        Assert.Contains(e.Patterns, p => !p.ProbeExists);
    }
}
