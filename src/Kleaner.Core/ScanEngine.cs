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

/// <summary>单条规则扫描完成的上报：规则 id 与该规则的候选计数。</summary>
public sealed record ScanProgress(string RuleId, int FileCount, long TotalBytes);

/// <summary>白名单扫描引擎：枚举候选 → 应用 exclude → 应用年龄/版本规则。只读，不做任何删除。</summary>
public sealed class ScanEngine
{
    private readonly string? _quarantineRoot;

    public ScanEngine(string? quarantineRoot = null) => _quarantineRoot = quarantineRoot;

    public ScanReport Scan(RuleSet set, CancellationToken token = default, IProgress<ScanProgress>? progress = null)
    {
        var now = DateTime.UtcNow;
        var results = new List<RuleScanResult>();
        var errors = new List<string>();

        // 撤回（Deprecated）的规则永不执行——即使被误设 Enabled；这是撤回机制的最深防线。
        foreach (var rule in set.Rules.Where(r => r.Enabled && !r.Deprecated))
        {
            token.ThrowIfCancellationRequested();
            var fileCount = 0;
            var totalBytes = 0L;
            try
            {
                var excludes = rule.Exclude.Select(GlobScanner.ToRegex).ToList();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var candidates = new List<FileCandidate>();

                foreach (var pattern in rule.Paths)
                {
                    token.ThrowIfCancellationRequested();
                    foreach (var file in GlobScanner.EnumerateFileInfos(pattern))
                    {
                        token.ThrowIfCancellationRequested();
                        var path = file.FullName;
                        if (!seen.Add(path))
                            continue;
                        if (excludes.Any(re => re.IsMatch(path)))
                            continue;
                        if (_quarantineRoot is not null && IsUnderRoot(path, _quarantineRoot))
                            continue;
                        // 大小与修改时间取枚举缓存的查找数据，不再补 stat；
                        // 此后文件消失的场景由移动前的复验与 File.Move 跳过机制兜底
                        candidates.Add(new FileCandidate(path, file.Length, file.LastWriteTimeUtc));
                    }
                }

                var selected = RuleSelector.Apply(candidates, rule, set, now);
                fileCount = selected.Count;
                totalBytes = selected.Sum(c => c.SizeBytes);
                results.Add(new RuleScanResult(
                    rule.Id, rule.Name, rule.Category, rule.Risk, rule.RequiresElevation,
                    fileCount, totalBytes, rule.SafetyNotes, selected));
            }
            catch (OperationCanceledException)
            {
                throw;
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
            // 取消路径直接抛出，不上报；其余路径每条规则完成即上报一次
            progress?.Report(new ScanProgress(rule.Id, fileCount, totalBytes));
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
