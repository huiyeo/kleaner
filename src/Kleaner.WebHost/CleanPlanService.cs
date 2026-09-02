using Kleaner.Core;

namespace Kleaner.WebHost;

/// <summary>计划内单条规则（对外视图，不含文件清单——预览只到计数与字节，强制预览语义够用）。</summary>
public sealed record PlanItemView(
    string RuleId,
    string RuleName,
    bool RequiresElevation,
    int FileCount,
    long TotalBytes);

/// <summary>执行期才需要的解析结果：规则 id + 该规则待移入隔离区的文件（只来自扫描报告，白名单外路径零触碰）。</summary>
public sealed record PlanResolvedItem(string RuleId, IReadOnlyList<FileCandidate> Files);

/// <summary>
/// 清理计划（dry-run 产物）：items 对外、resolved 对内，均只含「勾选中且文件数 &gt; 0」的规则。
/// <see cref="NeedsElevation"/> 在勾选集上判定（含零文件规则）。
/// </summary>
public sealed record CleanPlan(
    IReadOnlyList<PlanItemView> Items,
    IReadOnlyList<PlanResolvedItem> Resolved,
    bool NeedsElevation,
    int TotalFiles,
    long TotalBytes);

/// <summary>POST /api/plans 请求体（工单 12）。</summary>
public sealed record PlanRequest(string JobId, IReadOnlyList<string> RuleIds);

/// <summary>POST /api/plans/{planId}/confirm 请求体：必须携带此前预览返回的一次性 confirmToken（工单 03 第 5 层）。</summary>
public sealed record ConfirmRequest(string? ConfirmToken);

/// <summary>
/// 清理决策流水线的纯函数服务类（工单 04 决策：决策编排留宿主、可测）：
/// 勾选 id + 扫描 envelope → plan / needsElevation / 摘要。不做任何 I/O。
/// 提权检查在零文件过滤之前、零文件规则不进计划。
/// </summary>
public static class CleanPlanService
{
    /// <summary>勾选集与扫描结果 → 清理计划。未知规则 id 与空勾选集一律拒绝（不复刻 CLI「--rule 缺省静默成功」的坑）。</summary>
    public static CleanPlan Build(IReadOnlyList<string> checkedRuleIds, ScanResultEnvelope scan, bool isElevated)
    {
        if (checkedRuleIds.Count == 0)
        {
            throw new ArgumentException("未勾选任何规则", nameof(checkedRuleIds));
        }

        var rulesById = scan.Rules.ToDictionary(r => r.RuleId, StringComparer.Ordinal);
        foreach (var id in checkedRuleIds)
        {
            if (!rulesById.ContainsKey(id))
            {
                throw new ArgumentException($"勾选了扫描结果中不存在的规则 id：{id}", nameof(checkedRuleIds));
            }
        }

        var checkedSet = checkedRuleIds.ToHashSet(StringComparer.Ordinal);
        var needsElevation = scan.Rules.Any(r => checkedSet.Contains(r.RuleId) && r.RequiresElevation) && !isElevated;

        var items = scan.Rules
            .Where(r => checkedSet.Contains(r.RuleId) && r.FileCount > 0)
            .Select(r => new PlanItemView(r.RuleId, r.RuleName, r.RequiresElevation, r.FileCount, r.TotalBytes))
            .ToList();

        // resolved 与 items 逐条对应，文件清单只来自扫描报告——白名单外路径零触碰（deletion-path 第 1 道）
        var resolved = items
            .Select(item => new PlanResolvedItem(
                item.RuleId,
                rulesById[item.RuleId].Files.ToList()))
            .ToList();

        return new CleanPlan(
            items,
            resolved,
            needsElevation,
            items.Sum(i => i.FileCount),
            items.Sum(i => i.TotalBytes));
    }
}
