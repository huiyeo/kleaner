using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using Kleaner.Core;

namespace Kleaner.Executor;

/// <summary>pending/moved 记录隔离状态；restoring 在移动前固定还原目标及内容摘要。</summary>
public sealed record QuarantineEntry(string OriginalPath, string QuarantinedPath, long SizeBytes, string RuleId,
    string State = "pending", string? RestoreTarget = null, string? RestoreSha256 = null);

public sealed record QuarantineBatch(string BatchId, DateTime CreatedUtc, IReadOnlyList<QuarantineEntry> Entries)
{
    [JsonIgnore]
    public long TotalBytes => Entries.Sum(e => e.SizeBytes);
}

public sealed record ExecutionReport(string BatchId, string QuarantineDir, int MovedCount, long MovedBytes, IReadOnlyList<string> Skipped);

public sealed record RestoreReport(int RestoredCount, IReadOnlyList<string> Skipped, IReadOnlyList<string> Failed)
{
    public bool IsComplete => Skipped.Count == 0 && Failed.Count == 0;
}

public sealed record BatchDeletionReport(string BatchId, bool Deleted, IReadOnlyList<string> Failed);

/// <summary>隔离区副作用入口：清理只能移入，审计与 manifest 初始化失败时拒绝改变文件状态。</summary>
public sealed class QuarantineManager
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _root;
    private readonly HistoryManager _history;
    private readonly Action<QuarantineBatch>? _beforeManifestReplace;

    public QuarantineManager(string? root, HistoryManager history)
        : this(root, history, null)
    {
    }

    // 仅测试程序集注入故障；生产调用仍固定走真实文件写入、刷新和原子替换。
    internal QuarantineManager(string? root, HistoryManager history, Action<QuarantineBatch>? beforeManifestReplace)
    {
        _beforeManifestReplace = beforeManifestReplace;
        ArgumentNullException.ThrowIfNull(history);
        _root = Path.GetFullPath(root ?? DefaultRoot());
        _history = history;
        EnsureNoReparsePoints(_root);
        Directory.CreateDirectory(_root);
        _history.EnsureWritable();
    }

    public string Root => _root;

    public static string DefaultRoot()
    {
        try
        {
            var systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System)) ?? "C:\\";
            var drive = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Where(d => !string.Equals(Path.GetPathRoot(d.RootDirectory.FullName), systemRoot, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(d => d.TotalFreeSpace)
                .FirstOrDefault();
            if (drive is not null)
                return Path.Combine(drive.RootDirectory.FullName, "KleanerQuarantine");
        }
        catch
        {
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kleaner", "quarantine");
    }

    /// <summary>只执行由 Core 签发的计划；先落可恢复 manifest 与审计起始记录，再移动每个文件。</summary>
    public ExecutionReport Execute(CleanupPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var revalidation = plan.Revalidate();
        var skipped = revalidation.Skipped.ToList();
        if (revalidation.AuthorizedItems.Count == 0)
            return new ExecutionReport(string.Empty, string.Empty, 0, 0, skipped);

        var batchId = CreateBatchId();
        var batchDir = GetBatchDirectory(batchId);
        Directory.CreateDirectory(batchDir);
        var entries = new List<QuarantineEntry>();
        var createdUtc = DateTime.UtcNow;
        WriteManifestAtomic(batchDir, new QuarantineBatch(batchId, createdUtc, entries));
        _history.Append("clean-start", $"批次 {batchId}", revalidation.AuthorizedItems.Count, 0, "started");

        long bytes = 0;
        foreach (var item in revalidation.AuthorizedItems)
        {
            var file = item.File;
            var destination = Path.Combine(batchDir, MapRelative(file.FullPath));
            if (File.Exists(destination))
            {
                skipped.Add($"{file.FullPath}（隔离区目标已存在）");
                continue;
            }

            var entry = new QuarantineEntry(file.FullPath, destination, file.SizeBytes, item.RuleId);
            entries.Add(entry);
            // 恢复记录写入失败属于事务故障，不能由逐文件跳过逻辑吞掉。
            WriteManifestAtomic(batchDir, new QuarantineBatch(batchId, createdUtc, entries));
            try
            {
                EnsureNoReparsePoints(file.FullPath);
                EnsureNoReparsePoints(destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                EnsureNoReparsePoints(file.FullPath);
                EnsureNoReparsePoints(destination);
                File.Move(file.FullPath, destination);
                entries[^1] = entry with { State = "moved" };
                bytes += file.SizeBytes;
            }
            catch (Exception ex)
            {
                if (!File.Exists(destination))
                    entries.Remove(entry);
                skipped.Add($"{file.FullPath}（{ex.GetType().Name}）");
            }
            WriteManifestAtomic(batchDir, new QuarantineBatch(batchId, createdUtc, entries));
        }

        WriteManifestAtomic(batchDir, new QuarantineBatch(batchId, createdUtc, entries));
        var movedEntries = entries.Count(e => File.Exists(e.QuarantinedPath));
        _history.Append("clean", $"批次 {batchId}（规则：{string.Join(",", entries.Select(e => e.RuleId).Distinct())}）",
            movedEntries, bytes, skipped.Count == 0 ? "ok" : "partial");
        return new ExecutionReport(batchId, batchDir, movedEntries, bytes, skipped);
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
        EnsureNoReparsePoints(_root);
        if (!Directory.Exists(_root))
            return list;
        foreach (var dir in Directory.GetDirectories(_root))
        {
            if (!File.Exists(Path.Combine(dir, "manifest.json")))
                continue;
            try { list.Add(ReadBatch(dir)); }
            catch { }
        }
        return list.OrderByDescending(b => b.CreatedUtc).ToList();
    }

    /// <summary>只要存在未恢复、缺失或失败的条目，就保留整个批次及 manifest。</summary>
    public RestoreReport RestoreBatch(string batchId)
    {
        var batchDir = GetBatchDirectory(batchId);
        var batch = ReadBatch(batchDir);
        _history.Append("restore-start", $"批次 {batchId}", batch.Entries.Count, 0, "started");

        var remaining = batch.Entries.ToList();
        var skipped = new List<string>();
        var failed = new List<string>();
        var restored = 0;
        foreach (var entry in batch.Entries)
        {
            var currentEntry = entry;
            var sourceExists = File.Exists(entry.QuarantinedPath);
            if (!sourceExists && entry.State != "restoring")
            {
                skipped.Add($"{entry.QuarantinedPath}（隔离文件不存在）");
                continue;
            }

            if (entry.State != "restoring")
            {
                try
                {
                    var chosenTarget = File.Exists(entry.OriginalPath) ? $"{entry.OriginalPath}.restore-{batchId}" : entry.OriginalPath;
                    currentEntry = entry with
                    {
                        State = "restoring",
                        RestoreTarget = chosenTarget,
                        RestoreSha256 = ContentHash(entry.QuarantinedPath)
                    };
                }
                catch (Exception ex)
                {
                    failed.Add($"{entry.QuarantinedPath}（{ex.GetType().Name}）");
                    continue;
                }
                remaining[remaining.IndexOf(entry)] = currentEntry;
                try
                {
                    WriteManifestAtomic(batchDir, batch with { Entries = remaining.ToArray() });
                }
                catch (Exception ex)
                {
                    failed.Add($"还原意图写入失败，已停止还原（{ex.GetType().Name}）");
                    break;
                }
            }
            var target = currentEntry.RestoreTarget!;
            try
            {
                EnsureNoReparsePoints(entry.QuarantinedPath);
                EnsureNoReparsePoints(target);
                if (sourceExists)
                {
                    if (!MatchesRestoreContent(entry.QuarantinedPath, currentEntry))
                        throw new InvalidDataException("隔离文件内容与还原意图不一致");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    EnsureNoReparsePoints(entry.QuarantinedPath);
                    EnsureNoReparsePoints(target);
                    File.Move(entry.QuarantinedPath, target);
                }
                else if (!MatchesRestoreContent(target, currentEntry))
                {
                    throw new InvalidDataException("还原目标缺失或内容不匹配，保留清单待核对");
                }
                restored++;
            }
            catch (Exception ex)
            {
                failed.Add($"{entry.QuarantinedPath}（{ex.GetType().Name}）");
                continue;
            }
            try
            {
                // 先保存逐项证据，再移除恢复意图；审计失败不能继续移动下一项。
                var auditId = "restore-file:" + Convert.ToHexString(SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(batchId + "\0" + currentEntry.QuarantinedPath.ToUpperInvariant())));
                _history.AppendOnce(auditId, "restore-file", JsonSerializer.Serialize(new
                {
                    BatchId = batchId,
                    currentEntry.OriginalPath,
                    currentEntry.QuarantinedPath,
                    currentEntry.RestoreTarget,
                    currentEntry.RestoreSha256
                }, JsonOpts), 1, currentEntry.SizeBytes, "ok");
            }
            catch (Exception ex)
            {
                failed.Add($"逐项还原审计失败，已停止后续还原并保留意图（{ex.GetType().Name}）");
                break;
            }
            remaining.Remove(currentEntry);
            try
            {
                WriteManifestAtomic(batchDir, batch with { Entries = remaining.ToArray() });
            }
            catch (Exception ex)
            {
                failed.Add($"批次清单更新失败，已停止后续还原（{ex.GetType().Name}）");
                break;
            }
        }

        var report = new RestoreReport(restored, skipped, failed);
        if (report.IsComplete && remaining.Count == 0)
        {
            TryDeleteEmptyBatchDirectory(batchDir, failed);
            if (failed.Count > 0)
                report = report with { Failed = failed };
        }
        _history.Append("restore", $"批次 {batchId}", restored, 0, report.IsComplete && remaining.Count == 0 ? "ok" : "partial");
        return report;
    }

    public BatchDeletionReport DeleteBatch(string batchId)
    {
        var batchDir = GetBatchDirectory(batchId);
        _history.Append("delete-batch-start", $"批次 {batchId}", 0, 0, "started");
        var failed = DeleteDirectoryContents(batchDir);
        var deleted = failed.Count == 0 && !Directory.Exists(batchDir);
        _history.Append("delete-batch", $"批次 {batchId}", 0, 0, deleted ? "ok" : "partial");
        return new BatchDeletionReport(batchId, deleted, failed);
    }

    public int PurgeOlderThan(TimeSpan age)
    {
        var cutoff = DateTime.UtcNow - age;
        var successful = 0;
        var partial = false;
        foreach (var batch in ListBatches().Where(b => b.CreatedUtc < cutoff))
        {
            var report = DeleteBatch(batch.BatchId);
            if (report.Deleted) successful++;
            else partial = true;
        }
        _history.Append("purge", "清空过期批次", successful, 0, partial ? "partial" : "ok");
        return successful;
    }

    private string GetBatchDirectory(string batchId)
    {
        if (string.IsNullOrWhiteSpace(batchId) || !string.Equals(Path.GetFileName(batchId), batchId, StringComparison.Ordinal))
            throw new ArgumentException("非法批次 id", nameof(batchId));
        var root = Path.GetFullPath(_root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(_root, batchId));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("批次不在隔离区根目录中", nameof(batchId));
        EnsureNoReparsePoints(path);
        return path;
    }

    private static string CreateBatchId() => $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}";

    private QuarantineBatch ReadBatch(string batchDir)
    {
        EnsureNoReparsePoints(Path.Combine(batchDir, "manifest.json"));
        var batch = JsonSerializer.Deserialize<QuarantineBatch>(File.ReadAllText(Path.Combine(batchDir, "manifest.json")), JsonOpts)
            ?? throw new InvalidDataException("隔离区清单为空或损坏");
        if (batch.BatchId != Path.GetFileName(batchDir) || batch.Entries is null)
            throw new InvalidDataException("隔离区清单与批次不匹配");

        var prefix = Path.GetFullPath(batchDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var quarantinePrefix = Path.GetFullPath(_root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // 先校验整个批次，避免处理完前几项后才发现后面的清单损坏。
        foreach (var entry in batch.Entries)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.OriginalPath) ||
                string.IsNullOrWhiteSpace(entry.QuarantinedPath) ||
                !Path.IsPathFullyQualified(entry.OriginalPath) || !Path.IsPathFullyQualified(entry.QuarantinedPath))
                throw new InvalidDataException("隔离区清单必须使用绝对文件路径");

            var original = Path.GetFullPath(entry.OriginalPath);
            var source = Path.GetFullPath(entry.QuarantinedPath);
            var expected = Path.GetFullPath(Path.Combine(batchDir, MapRelative(original)));
            if (!source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(source, expected, StringComparison.OrdinalIgnoreCase) ||
                original.StartsWith(quarantinePrefix, StringComparison.OrdinalIgnoreCase) ||
                !seen.Add(source) || entry.SizeBytes < 0 ||
                entry.State is not ("pending" or "moved" or "restoring"))
                throw new InvalidDataException("隔离区清单路径、映射或条目状态非法");
            if (entry.State == "restoring")
            {
                if (entry.RestoreTarget is null || !Path.IsPathFullyQualified(entry.RestoreTarget) ||
                    entry.RestoreSha256 is not { Length: 64 } || !entry.RestoreSha256.All(Uri.IsHexDigit))
                    throw new InvalidDataException("还原意图缺少目标或内容摘要");
                var restoreTarget = Path.GetFullPath(entry.RestoreTarget);
                if (!string.Equals(restoreTarget, original, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(restoreTarget, original + ".restore-" + batch.BatchId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("还原意图目标不在允许范围");
                EnsureNoReparsePoints(restoreTarget);
            }
            EnsureNoReparsePoints(source);
            EnsureNoReparsePoints(original);
        }
        return batch;
    }

    private static string ContentHash(string path)
    {
        EnsureNoReparsePoints(path);
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool MatchesRestoreContent(string path, QuarantineEntry entry) =>
        File.Exists(path) && new FileInfo(path).Length == entry.SizeBytes &&
        string.Equals(ContentHash(path), entry.RestoreSha256, StringComparison.OrdinalIgnoreCase);

    /// <summary>按根到叶检查现有路径段。仅允许尚不存在的路径，其他读取错误向上抛出。</summary>
    private static void EnsureNoReparsePoints(string path)
    {
        var ancestors = new Stack<string>();
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            ancestors.Push(current);
        foreach (var current in ancestors)
        {
            try
            {
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                    throw new InvalidDataException($"路径包含 reparse point，已拒绝操作：{current}");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private void WriteManifestAtomic(string batchDir, QuarantineBatch batch)
    {
        var manifest = Path.Combine(batchDir, "manifest.json");
        EnsureNoReparsePoints(manifest);
        var temporary = Path.Combine(batchDir, $"manifest.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, batch, JsonOpts);
                stream.Flush(flushToDisk: true);
            }
            EnsureNoReparsePoints(manifest);
            _beforeManifestReplace?.Invoke(batch);
            File.Move(temporary, manifest, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try { File.Delete(temporary); }
                catch { }
            }
        }
    }

    private static void TryDeleteEmptyBatchDirectory(string batchDir, List<string> failed)
    {
        try
        {
            EnsureNoReparsePoints(batchDir);
            var manifest = Path.Combine(batchDir, "manifest.json");
            var directories = new List<string> { batchDir };
            // 先证明只剩清单与空目录；未知文件和 reparse point 必须留给人工检查。
            for (var index = 0; index < directories.Count; index++)
            {
                var current = directories[index];
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                    throw new IOException($"批次包含 reparse point：{current}");
                foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                {
                    var attributes = File.GetAttributes(entry);
                    if (attributes.HasFlag(FileAttributes.ReparsePoint))
                        throw new IOException($"批次包含 reparse point：{entry}");
                    if (attributes.HasFlag(FileAttributes.Directory))
                        directories.Add(entry);
                    else if (!string.Equals(entry, manifest, StringComparison.OrdinalIgnoreCase))
                        throw new IOException($"批次仍有未登记文件：{entry}");
                }
            }

            for (var index = directories.Count - 1; index > 0; index--)
            {
                EnsureNoReparsePoints(directories[index]);
                Directory.Delete(directories[index], recursive: false);
            }
            EnsureNoReparsePoints(manifest);
            File.Delete(manifest);
            Directory.Delete(batchDir, recursive: false);
        }
        catch (Exception ex) { failed.Add($"{batchDir}（{ex.GetType().Name}）"); }
    }

    private static List<string> DeleteDirectoryContents(string directory)
    {
        var failed = new List<string>();
        if (!Directory.Exists(directory)) return failed;
        var manifest = Path.Combine(directory, "manifest.json");
        var directories = new List<string> { directory };
        for (var index = 0; index < directories.Count; index++)
        {
            var current = directories[index];
            IEnumerable<string> entries;
            try
            {
                EnsureNoReparsePoints(current);
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                    throw new IOException($"批次包含 reparse point：{current}");
                entries = Directory.EnumerateFileSystemEntries(current, "*", SearchOption.TopDirectoryOnly).ToArray();
            }
            catch (Exception ex)
            {
                failed.Add($"{current}（{ex.GetType().Name}）");
                continue;
            }
            foreach (var entry in entries)
            {
                // 清单必须存活到所有内容处理完成，否则失败批次将从列表消失。
                if (string.Equals(entry, manifest, StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    EnsureNoReparsePoints(entry);
                    var attributes = File.GetAttributes(entry);
                    if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        failed.Add($"{entry}（reparse point 已跳过）");
                    }
                    else if (Directory.Exists(entry)) directories.Add(entry);
                    else File.Delete(entry);
                }
                catch (Exception ex) { failed.Add($"{entry}（{ex.GetType().Name}）"); }
            }
        }
        if (failed.Count == 0)
            TryDeleteEmptyBatchDirectory(directory, failed);
        return failed;
    }
}
