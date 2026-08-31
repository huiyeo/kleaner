namespace Kleaner.Analysis;

/// <summary>进入重复文件组的候选（路径 + 大小 + 修改时间）。</summary>
public sealed record DuplicateCandidate(string Path, long SizeBytes, DateTime LastWriteTimeUtc);

/// <summary>规划结果：组内按修改时间降序，最新一份标记保留（IsKeep=true），其余为可清理副本。</summary>
public sealed record DuplicatePlanItem(string Path, long SizeBytes, DateTime LastWriteTimeUtc, bool IsKeep);

/// <summary>重复文件的保留策略：每组必须至少保留一份——保留最新，其余标记为副本。
/// 纯函数，无 IO，便于单元测试与跨入口（GUI/CLI）复用。</summary>
public static class DuplicateSelectionPolicy
{
    /// <summary>按修改时间降序（同时间按路径字典序稳定排序）规划组内文件，最新一份保留。</summary>
    public static List<DuplicatePlanItem> Plan(IEnumerable<DuplicateCandidate> files) =>
        files
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select((f, index) => new DuplicatePlanItem(f.Path, f.SizeBytes, f.LastWriteTimeUtc, IsKeep: index == 0))
            .ToList();
}
