using System.Text;

namespace Kleaner.Core;

/// <summary>规则库治理指标的只读快照。全部口径为纯规则数据可计算项，不依赖遥测；
/// 「推荐准确率」与「误伤事件数」的精确口径待定（工单 rule-governance 02/03），本快照仅含可测部分。</summary>
public sealed record RuleGovernanceReport(
    int TotalRules,
    int EvidenceCoveredRules,
    double EvidenceCoverage,
    int VerifiedRules,
    double VerifiedCoverage,
    int DefaultSelectableRules,
    int CategoriesCovered,
    int TotalCategories,
    int UniqueTargetRoots);

/// <summary>
/// Phase 2 治理指标计算（goals.md：覆盖率/证据覆盖率/验证覆盖率/推荐准确率/误伤事件数）。
/// 可测口径：证据覆盖率 = safetyNotes≥20 字且 safetyDoc 非空的规则占比；验证覆盖率 =
/// verified 以「本机实测」开头的规则占比（与 RuleSelectionPolicy 默认勾选同源）；软件覆盖率 =
/// 已覆盖分类数（共 6 类）与唯一目标根数（paths 的环境变量名或盘符，去重，不展开实际路径——保证指标确定性）。
/// </summary>
public static partial class RuleGovernance
{
    public const int TotalCategoryCount = 6;

    public static RuleGovernanceReport Report(RuleSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        var total = set.Rules.Count;
        if (total == 0)
            return new RuleGovernanceReport(0, 0, 0, 0, 0, 0, 0, TotalCategoryCount, 0);

        var evidenceCovered = 0;
        var verified = 0;
        var defaultSelectable = 0;
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var categories = new HashSet<RuleCategory>();

        foreach (var rule in set.Rules)
        {
            if (rule.SafetyNotes.Length >= 20 && !string.IsNullOrWhiteSpace(rule.SafetyDoc))
                evidenceCovered++;
            if (RuleSelectionPolicy.IsDefaultSelectable(rule))
            {
                verified++;
                defaultSelectable++;
            }
            categories.Add(rule.Category);
            foreach (var path in rule.Paths)
                roots.Add(TargetRoot(path));
        }

        var totalRules = (double)total;
        return new RuleGovernanceReport(
            TotalRules: total,
            EvidenceCoveredRules: evidenceCovered,
            EvidenceCoverage: evidenceCovered / totalRules,
            VerifiedRules: verified,
            VerifiedCoverage: verified / totalRules,
            DefaultSelectableRules: defaultSelectable,
            CategoriesCovered: categories.Count,
            TotalCategories: TotalCategoryCount,
            UniqueTargetRoots: roots.Count);
    }

    // 目标根：路径开头的 %环境变量%（含百分号）或盘符（如 D:）。不展开实际值——指标跨机器稳定可复现。
    private static string TargetRoot(string path)
    {
        var trimmed = path.TrimStart();
        if (trimmed.StartsWith('%'))
        {
            var end = trimmed.IndexOf('%', 1);
            if (end > 1) return trimmed[..(end + 1)];
        }
        var colon = trimmed.IndexOf(':');
        if (colon is 1) return trimmed[..2]; // 盘符
        return trimmed;
    }
}

/// <summary>误伤事件信号（口径经用户确认 2026-09-10）：还原批次即计一次「潜在误伤事件」
/// （信号级非结论——还原也可能是「改主意」）；部分还原按还原文件数计明细。供人工复核与趋势观察。</summary>
public sealed record RestoreSignalReport(int RestoreEvents, int RestoredFiles);

/// <summary>误伤信号的输入行（调用方从 HistoryEntry 投影；Core 零依赖不做类型引用）。</summary>
public sealed record RestoreSignalEntry(string Action, int FileCount);

/// <summary>治理指标目标值（用户确认 2026-09-10：基线锚定分阶段收紧）。阈值仅警告不阻塞构建——治理指标非安全不变量。</summary>
public sealed record GovernanceTarget(
    double EvidenceCoverage,
    double VerifiedCoverage,
    int CategoriesCovered)
{
    /// <summary>用户确认的首期目标（基线锚定）：证据 100% 保持、验证覆盖率阶段一 ≥25%。</summary>
    public static GovernanceTarget Phase2Initial { get; } = new(1.0, 0.25, 6);
}

public static partial class RuleGovernance
{
    /// <summary>从操作历史提取误伤信号：restore 汇总条目计事件，FileCount 累计还原文件明细。</summary>
    public static RestoreSignalReport RestoreSignal(IReadOnlyList<RestoreSignalEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var events = 0;
        var files = 0;
        foreach (var e in entries)
        {
            if (e.Action != "restore") continue;
            events++;
            files += Math.Max(0, e.FileCount);
        }
        return new RestoreSignalReport(events, files);
    }

    /// <summary>对照目标值核销指标；未达标项返回可读警告（警告不阻塞构建——治理指标非安全不变量）。</summary>
    public static (bool Met, IReadOnlyList<string> Warnings) CheckTargets(
        RuleGovernanceReport report, GovernanceTarget target)
    {
        var warnings = new List<string>();
        if (report.EvidenceCoverage < target.EvidenceCoverage)
            warnings.Add($"证据覆盖率 {report.EvidenceCoverage:P1} 低于目标 {target.EvidenceCoverage:P1}（要求保持 100%）");
        if (report.VerifiedCoverage < target.VerifiedCoverage)
            warnings.Add($"验证覆盖率 {report.VerifiedCoverage:P1} 低于阶段目标 {target.VerifiedCoverage:P1}（提升途径：真机实测转正）");
        if (report.CategoriesCovered < target.CategoriesCovered)
            warnings.Add($"分类覆盖 {report.CategoriesCovered}/{target.CategoriesCovered} 低于目标");
        return (warnings.Count == 0, warnings);
    }
}
