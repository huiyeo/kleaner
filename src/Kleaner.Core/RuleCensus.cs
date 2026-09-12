namespace Kleaner.Core;

public enum CensusVerdict
{
    /// <summary>至少一个模式的探测路径存在。注意：requiresElevation 规则未提权时可能被 ACL 误判——AbsentOrDenied ≠ 一定不存在。</summary>
    Present,

    /// <summary>全部模式的探测路径都不存在，或因权限不可达（Directory.Exists 对拒绝访问静默返回 false）。</summary>
    AbsentOrDenied,
}

/// <summary>单模式探测结果：通配符模式探测其前缀起始目录；精确路径模式探测目标本身。</summary>
public sealed record PatternCensus(string Pattern, bool IsExactPath, string ProbePath, bool ProbeExists);

public sealed record RuleCensusEntry(
    string RuleId,
    string Name,
    RuleCategory Category,
    bool RequiresElevation,
    string Verified,
    CensusVerdict Verdict,
    int ScanFileCount,
    long ScanTotalBytes,
    IReadOnlyList<PatternCensus> Patterns);

public sealed record RuleCensusReport(
    int TotalRules,
    int Present,
    int PresentWithHits,
    int PresentNoHits,
    int AbsentOrDenied,
    IReadOnlyList<RuleCensusEntry> Entries);

/// <summary>
/// 规则真机普查（Phase 5，工单 rule-verification 01）：把规则的路径模式与一次完整扫描的命中
/// 合成为只读证据档案。本工具只产出事实（存在性 / 命中量），不产出任何「升级 verified」或
/// 「撤回」结论——那些判断属于所有者决策包。requiresElevation 规则在未提权进程中的
/// AbsentOrDenied 可能是 ACL 拒绝，消费方必须结合该字段解读。
/// </summary>
public static class RuleCensus
{
    public static RuleCensusReport Build(RuleSet set, ScanReport scan)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(scan);
        var scanIndex = scan.Results.ToDictionary(r => r.RuleId, r => r);

        var entries = new List<RuleCensusEntry>();
        foreach (var rule in set.Rules)
        {
            var patterns = rule.Paths.Select(Probe).ToList();
            var verdict = patterns.Any(p => p.ProbeExists) ? CensusVerdict.Present : CensusVerdict.AbsentOrDenied;
            scanIndex.TryGetValue(rule.Id, out var result);
            entries.Add(new RuleCensusEntry(
                rule.Id, rule.Name, rule.Category, rule.RequiresElevation, rule.Verified ?? string.Empty,
                verdict, result?.FileCount ?? 0, result?.TotalBytes ?? 0, patterns));
        }

        var present = entries.Where(e => e.Verdict == CensusVerdict.Present).ToList();
        return new RuleCensusReport(
            entries.Count, present.Count,
            present.Count(e => e.ScanFileCount > 0),
            present.Count(e => e.ScanFileCount == 0),
            entries.Count - present.Count,
            entries);
    }

    private static PatternCensus Probe(string pattern)
    {
        var normalized = GlobScanner.Normalize(pattern);
        var startDir = GlobScanner.TryGetStartDir(pattern);
        if (startDir is not null)
            return new PatternCensus(pattern, IsExactPath: false, startDir, Directory.Exists(startDir));
        return new PatternCensus(pattern, IsExactPath: true, normalized,
            File.Exists(normalized) || Directory.Exists(normalized));
    }
}
