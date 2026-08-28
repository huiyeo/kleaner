namespace Kleaner.Core;

public sealed record FileCandidate(string FullPath, long SizeBytes, DateTime LastWriteTimeUtc);

/// <summary>对扫描候选集应用规则选择逻辑：年龄阈值或 keepNewest 版本保留，二者互斥（keepNewest 豁免年龄）。</summary>
public static class RuleSelector
{
    public static IReadOnlyList<FileCandidate> Apply(
        IReadOnlyList<FileCandidate> candidates, Rule rule, RuleSet set, DateTime nowUtc)
    {
        if (rule.KeepNewest is int keep && keep >= 1)
        {
            return candidates
                .GroupBy(c => Path.GetDirectoryName(c.FullPath) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .SelectMany(g => g
                    .OrderByDescending(c => Path.GetFileName(c.FullPath), StringComparer.OrdinalIgnoreCase)
                    .Skip(keep))
                .ToList();
        }

        var ageDays = set.EffectiveAgeDays(rule);
        if (ageDays is null)
            return Array.Empty<FileCandidate>();

        var cutoff = nowUtc.AddDays(-ageDays.Value);
        return candidates.Where(c => c.LastWriteTimeUtc < cutoff).ToList();
    }
}
