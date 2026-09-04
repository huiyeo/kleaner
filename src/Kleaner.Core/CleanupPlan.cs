namespace Kleaner.Core;

/// <summary>由 Core 签发的不可变清理计划。构造器仅对 Core 可见，调用方不能把裸路径伪装成授权项。</summary>
public sealed class CleanupPlan
{
    private readonly RuleSet _rules;
    private readonly string? _quarantineRoot;

    internal CleanupPlan(RuleSet rules, string? quarantineRoot, IReadOnlyList<CleanupPlanItem> items)
    {
        _rules = rules;
        _quarantineRoot = quarantineRoot;
        Items = Array.AsReadOnly(items.ToArray());
    }

    public int RuleSetSchemaVersion => _rules.SchemaVersion;

    public IReadOnlyList<CleanupPlanItem> Items { get; }

    /// <summary>执行前以计划内的规则快照重新扫描，只有仍符合全部规则条件且元数据未变化的项才可移入隔离区。</summary>
    public CleanupPlanRevalidation Revalidate() => CleanupPlanBuilder.Revalidate(_rules, _quarantineRoot, Items);
}

/// <summary>单个授权项的扫描快照。</summary>
public sealed class CleanupPlanItem
{
    internal CleanupPlanItem(string ruleId, FileCandidate file)
    {
        RuleId = ruleId;
        File = file;
    }

    public string RuleId { get; }

    public FileCandidate File { get; }
}

/// <summary>执行前复验结果。Skipped 仅描述未通过复验的项，不包含移动阶段的 IO 跳过。</summary>
public sealed record CleanupPlanRevalidation(
    IReadOnlyList<CleanupPlanItem> AuthorizedItems,
    IReadOnlyList<string> Skipped);

/// <summary>清理计划的唯一签发模块：把规则、扫描结果和用户选择收敛为可复验的计划。</summary>
public static class CleanupPlanBuilder
{
    /// <summary>仅接受当前规则集中的规则选择；扫描结果会立即重新核验，防止伪造或已过期的候选进入计划。</summary>
    public static CleanupPlan Create(
        RuleSet ruleSet,
        ScanReport scanReport,
        IEnumerable<string> selectedRuleIds,
        string? quarantineRoot = null)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        ArgumentNullException.ThrowIfNull(scanReport);
        ArgumentNullException.ThrowIfNull(selectedRuleIds);

        var selectedIds = selectedRuleIds.ToHashSet(StringComparer.Ordinal);
        var rulesById = ruleSet.Rules.ToDictionary(rule => rule.Id, StringComparer.Ordinal);
        var unknown = selectedIds.Where(id => !rulesById.ContainsKey(id)).ToArray();
        if (unknown.Length > 0)
            throw new ArgumentException($"未知规则 id：{string.Join(",", unknown)}", nameof(selectedRuleIds));

        var selectedRules = ruleSet.Rules.Where(rule => selectedIds.Contains(rule.Id)).ToArray();
        var snapshot = Snapshot(ruleSet, selectedRules);
        var fresh = new ScanEngine(quarantineRoot).Scan(snapshot);
        var freshByRule = fresh.Results.ToDictionary(result => result.RuleId, StringComparer.Ordinal);
        var reportedByRule = scanReport.Results.ToDictionary(result => result.RuleId, StringComparer.Ordinal);
        var items = new List<CleanupPlanItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in selectedRules)
        {
            if (!reportedByRule.TryGetValue(rule.Id, out var reported) ||
                !freshByRule.TryGetValue(rule.Id, out var current))
                continue;

            foreach (var candidate in reported.Files)
            {
                if (!seen.Add(candidate.FullPath))
                    continue;
                var verified = current.Files.FirstOrDefault(file => SamePath(file.FullPath, candidate.FullPath));
                if (verified is not null && SameSnapshot(verified, candidate))
                    items.Add(new CleanupPlanItem(rule.Id, candidate));
            }
        }

        return new CleanupPlan(snapshot, quarantineRoot, items);
    }

    internal static CleanupPlanRevalidation Revalidate(
        RuleSet rules,
        string? quarantineRoot,
        IReadOnlyList<CleanupPlanItem> items)
    {
        var fresh = new ScanEngine(quarantineRoot).Scan(rules);
        var freshByRule = fresh.Results.ToDictionary(result => result.RuleId, StringComparer.Ordinal);
        var authorized = new List<CleanupPlanItem>();
        var skipped = new List<string>();

        foreach (var item in items)
        {
            if (!freshByRule.TryGetValue(item.RuleId, out var result))
            {
                skipped.Add($"{item.File.FullPath}（执行前复验未通过）");
                continue;
            }

            var current = result.Files.FirstOrDefault(file => SamePath(file.FullPath, item.File.FullPath));
            if (current is null || !SameSnapshot(current, item.File))
            {
                skipped.Add($"{item.File.FullPath}（执行前复验未通过）");
                continue;
            }

            authorized.Add(item);
        }

        return new CleanupPlanRevalidation(authorized, skipped);
    }

    private static RuleSet Snapshot(RuleSet source, IReadOnlyList<Rule> selectedRules)
    {
        var categories = source.AgeDaysByCategory is null
            ? null
            : new Dictionary<string, int>(source.AgeDaysByCategory, StringComparer.Ordinal);
        var rules = selectedRules.Select(rule => new Rule(
            rule.Id,
            rule.Name,
            rule.Category,
            rule.Risk,
            rule.Paths.ToArray(),
            rule.Exclude.ToArray(),
            rule.AgeDays,
            rule.KeepNewest,
            rule.RequiresElevation,
            rule.Enabled,
            rule.SafetyNotes,
            rule.SafetyDoc,
            rule.Verified)).ToArray();
        return new RuleSet(source.SchemaVersion, categories, source.DefaultAgeDays, rules);
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static bool SameSnapshot(FileCandidate left, FileCandidate right) =>
        left.SizeBytes == right.SizeBytes && left.LastWriteTimeUtc == right.LastWriteTimeUtc;
}
