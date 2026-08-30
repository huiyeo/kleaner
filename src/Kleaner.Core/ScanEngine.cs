namespace Kleaner.Core;

public sealed record RuleScanResult(
    string RuleId,
    string RuleName,
    RuleCategory Category,
    RiskLevel Risk,
    bool RequiresElevation,
    int FileCount,
    long TotalBytes,
    string SafetyNotes,
    IReadOnlyList<FileCandidate> Files,
    string? Note = null);

public sealed record ScanReport(DateTime ScanUtc, IReadOnlyList<RuleScanResult> Results, IReadOnlyList<string> Errors);

/// <summary>白名单扫描引擎：枚举候选 → 应用 exclude → 应用年龄/版本规则。只读，不做任何删除。</summary>
public sealed class ScanEngine
{
    private readonly string? _quarantineRoot;

    public ScanEngine(string? quarantineRoot = null) => _quarantineRoot = quarantineRoot;

    public ScanReport Scan(RuleSet set)
    {
        var now = DateTime.UtcNow;
        var results = new List<RuleScanResult>();
        var errors = new List<string>();

        foreach (var rule in set.Rules.Where(r => r.Enabled))
        {
            try
            {
                var excludes = rule.Exclude.Select(GlobScanner.ToRegex).ToList();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var candidates = new List<FileCandidate>();

                foreach (var pattern in rule.Paths)
                {
                    foreach (var path in GlobScanner.EnumerateFiles(pattern))
                    {
                        if (!seen.Add(path))
                            continue;
                        if (excludes.Any(re => re.IsMatch(path)))
                            continue;
                        if (_quarantineRoot is not null && IsUnderRoot(path, _quarantineRoot))
                            continue;

                        long size;
                        DateTime mtime;
                        try
                        {
                            var fi = new FileInfo(path);
                            size = fi.Length;
                            mtime = fi.LastWriteTimeUtc;
                        }
                        catch
                        {
                            continue;
                        }
                        candidates.Add(new FileCandidate(path, size, mtime));
                    }
                }

                var selected = RuleSelector.Apply(candidates, rule, set, now);
                results.Add(new RuleScanResult(
                    rule.Id, rule.Name, rule.Category, rule.Risk, rule.RequiresElevation,
                    selected.Count, selected.Sum(c => c.SizeBytes), rule.SafetyNotes, selected));
            }
            catch (UnauthorizedAccessException)
            {
                results.Add(new RuleScanResult(
                    rule.Id, rule.Name, rule.Category, rule.Risk, rule.RequiresElevation,
                    0, 0, rule.SafetyNotes, Array.Empty<FileCandidate>(),
                    Note: "需要管理员权限，未扫描"));
            }
            catch (Exception ex)
            {
                errors.Add($"规则 {rule.Id} 扫描失败：{ex.Message}");
            }
        }

        return new ScanReport(now, results, errors);
    }

    /// <summary>判断 path 是否位于 root 目录之下（按路径段边界，避免 "D:\Q" 误伤 "D:\Q2"）。</summary>
    private static bool IsUnderRoot(string path, string root)
    {
        var r = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!path.StartsWith(r, StringComparison.OrdinalIgnoreCase))
            return false;
        return path.Length == r.Length ||
               path[r.Length] == Path.DirectorySeparatorChar ||
               path[r.Length] == Path.AltDirectorySeparatorChar;
    }
}
