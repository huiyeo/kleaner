using System.Text.Json;
using System.Text.Json.Serialization;
using Kleaner.Core;

namespace Kleaner.Executor;

public sealed record QuarantineEntry(string OriginalPath, string QuarantinedPath, long SizeBytes, string RuleId);

public sealed record QuarantineBatch(string BatchId, DateTime CreatedUtc, IReadOnlyList<QuarantineEntry> Entries)
{
    [JsonIgnore]
    public long TotalBytes => Entries.Sum(e => e.SizeBytes);
}

public sealed record ExecutionReport(
    string BatchId, string QuarantineDir, int MovedCount, long MovedBytes, IReadOnlyList<string> Skipped);

/// <summary>隔离区：删除即移入（默认非系统盘），manifest 记录原路径，支持整批还原与手动清空。不做任何永久删除。</summary>
public sealed class QuarantineManager
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _root;
    private readonly HistoryManager? _history;

    public QuarantineManager(string? root = null, HistoryManager? history = null)
    {
        _root = root ?? DefaultRoot();
        _history = history;
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    /// <summary>默认隔离区根目录：剩余空间最大的非系统固定盘；无其他盘时回退用户目录（C 盘）。</summary>
    public static string DefaultRoot()
    {
        try
        {
            var systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System)) ?? "C:\\";
            var drive = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Where(d => !string.Equals(
                    Path.GetPathRoot(d.RootDirectory.FullName),
                    systemRoot,
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(d => d.TotalFreeSpace)
                .FirstOrDefault();
            if (drive is not null)
                return Path.Combine(drive.RootDirectory.FullName, "KleanerQuarantine");
        }
        catch
        {
        }
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kleaner", "quarantine");
    }

    public ExecutionReport Execute(IEnumerable<(string RuleId, FileCandidate File)> items)
    {
        var batchId = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var batchDir = Path.Combine(_root, batchId);
        Directory.CreateDirectory(batchDir);

        var entries = new List<QuarantineEntry>();
        var skipped = new List<string>();
        long bytes = 0;

        foreach (var (ruleId, file) in items)
        {
            var dest = Path.Combine(batchDir, MapRelative(file.FullPath));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Move(file.FullPath, dest);
                entries.Add(new QuarantineEntry(file.FullPath, dest, file.SizeBytes, ruleId));
                bytes += file.SizeBytes;
            }
            catch
            {
                // 被占用/无权限等：跳过并记录，绝不强制删除
                skipped.Add(file.FullPath);
            }
        }

        File.WriteAllText(
            Path.Combine(batchDir, "manifest.json"),
            JsonSerializer.Serialize(new QuarantineBatch(batchId, DateTime.UtcNow, entries), JsonOpts));

        _history?.Append("clean", $"批次 {batchId}（规则：{string.Join(",", entries.Select(e => e.RuleId).Distinct())}）",
            entries.Count, bytes, skipped.Count == 0 ? "ok" : "partial");

        return new ExecutionReport(batchId, batchDir, entries.Count, bytes, skipped);
    }

    internal static string MapRelative(string originalPath)
    {
        var full = Path.GetFullPath(originalPath);
        var root = Path.GetPathRoot(full) ?? throw new ArgumentException("非绝对路径", nameof(originalPath));
        var drive = root.TrimEnd(':', '\\');
        return Path.Combine(drive, full.Substring(root.Length));
    }

    public IReadOnlyList<QuarantineBatch> ListBatches()
    {
        var list = new List<QuarantineBatch>();
        if (!Directory.Exists(_root))
            return list;
        foreach (var dir in Directory.GetDirectories(_root))
        {
            var manifest = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifest))
                continue;
            try
            {
                list.Add(JsonSerializer.Deserialize<QuarantineBatch>(File.ReadAllText(manifest), JsonOpts)!);
            }
            catch
            {
            }
        }
        return list.OrderByDescending(b => b.CreatedUtc).ToList();
    }

    /// <summary>整批还原。原路径已存在同名文件时，还原文件追加 .restore-{batchId} 后缀，绝不覆盖现有文件。</summary>
    public int RestoreBatch(string batchId)
    {
        var batchDir = Path.Combine(_root, batchId);
        var manifest = Path.Combine(batchDir, "manifest.json");
        var batch = JsonSerializer.Deserialize<QuarantineBatch>(File.ReadAllText(manifest), JsonOpts)!;

        var restored = 0;
        foreach (var entry in batch.Entries.Where(e => File.Exists(e.QuarantinedPath)))
        {
            var target = entry.OriginalPath;
            if (File.Exists(target))
                target = $"{target}.restore-{batchId}";
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            try
            {
                File.Move(entry.QuarantinedPath, target);
                restored++;
            }
            catch
            {
            }
        }

        TryDeleteDir(batchDir);
        _history?.Append("restore", $"批次 {batchId}", restored, 0, restored > 0 ? "ok" : "failed");
        return restored;
    }

    public void DeleteBatch(string batchId)
    {
        TryDeleteDir(Path.Combine(_root, batchId));
        _history?.Append("delete-batch", $"批次 {batchId}", 0, 0, "ok");
    }

    /// <summary>清空早于指定时长的批次。仅由用户显式触发（手动清空策略，不自动删除）。</summary>
    public int PurgeOlderThan(TimeSpan age)
    {
        var cutoff = DateTime.UtcNow - age;
        var purged = 0;
        foreach (var batch in ListBatches().Where(b => b.CreatedUtc < cutoff))
        {
            DeleteBatch(batch.BatchId);
            purged++;
        }
        _history?.Append("purge", "清空过期批次", 0, 0, purged > 0 ? "ok" : "ok");
        return purged;
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (!Directory.Exists(dir))
                return;
            foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { File.Delete(file); }
                catch { }
            }
            foreach (var sub in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length))
            {
                try { Directory.Delete(sub); }
                catch { }
            }
            Directory.Delete(dir);
        }
        catch
        {
        }
    }
}
