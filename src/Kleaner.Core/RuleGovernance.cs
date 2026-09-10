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
public static class RuleGovernance
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
