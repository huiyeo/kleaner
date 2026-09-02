using Kleaner.Core;

namespace Kleaner.WebHost;

/// <summary>单条规则的 Web 视图：Core record 之上的薄 envelope（工单 04/12）。</summary>
public sealed record ScanRuleView(
    string RuleId,
    string RuleName,
    string Category,
    string Risk,
    bool RequiresElevation,
    bool MachineVerified,
    int FileCount,
    long TotalBytes,
    string SafetyNotes,
    string? Note,
    IReadOnlyList<FileCandidate> Files);

/// <summary>
/// 扫描结果的对外形状（工单 12）：直接承载 Core 的 FileCandidate，另补前端展示缺口——
/// MachineVerified 判定（经 <see cref="RuleSelectionPolicy"/>）、
/// 枚举的字符串化（category/risk 直接给 kebab-case，前端无需内置枚举表）。
/// 作为扫描 job 的终态结果存进 JobRegistry，经 GET /api/jobs/{id} 快照直接可取（工单 07 重连语义）。
/// </summary>
public sealed record ScanResultEnvelope(
    DateTime ScanUtc,
    IReadOnlyList<ScanRuleView> Rules,
    IReadOnlyList<string> Errors)
{
    public static ScanResultEnvelope From(RuleSet set, ScanReport report)
    {
        var rulesById = set.Rules.ToDictionary(r => r.Id, StringComparer.Ordinal);
        return new ScanResultEnvelope(
            report.ScanUtc,
            report.Results
                .Select(result => rulesById.TryGetValue(result.RuleId, out var rule)
                    ? new ScanRuleView(
                        result.RuleId, result.RuleName,
                        RuleSetLoader.CategoryKey(result.Category), RiskKey(result.Risk),
                        result.RequiresElevation,
                        RuleSelectionPolicy.IsDefaultSelectable(rule),
                        result.FileCount, result.TotalBytes,
                        result.SafetyNotes, result.Note,
                        result.Files)
                    : throw new InvalidOperationException($"扫描结果含未知规则 id：{result.RuleId}"))
                .ToList(),
            report.Errors);
    }

    private static string RiskKey(RiskLevel risk) => risk switch
    {
        RiskLevel.Low => "low",
        RiskLevel.Medium => "medium",
        _ => throw new ArgumentOutOfRangeException(nameof(risk)),
    };
}
