using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;

namespace Kleaner.Executor;

public sealed record HistoryEntry(
    string Id,
    DateTime Utc,
    string Action,      // clean | restore | purge | delete-batch | large-files | duplicates | cli-clean
    string Detail,      // 规则 id / 批次 id / 扫描根目录等
    int FileCount,
    long Bytes,
    string Result)      // ok | partial | failed | cancelled
{
    [JsonIgnore]
    public string TimeDisplay => Utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}

/// <summary>操作历史：JSON Lines 只追加文件（%APPDATA%\Kleaner\history.jsonl），每次删除类操作后记录，可审计可回溯。</summary>
public sealed class HistoryManager
{
    internal const int MaxLineChars = 65_536;
    internal const int MaxRecentEntries = 1_000;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;
    private readonly object _lock = new();

    public HistoryManager(string? path = null) =>
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Kleaner", "history.jsonl");

    public string FilePath => _path;

    /// <summary>在改变文件状态前确认审计日志可创建、可写入并已落盘。</summary>
    public void EnsureWritable()
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using var stream = new FileStream(_path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            stream.Flush(flushToDisk: true);
        }
    }

    public void Append(string action, string detail, int fileCount, long bytes, string result)
    {
        var entry = new HistoryEntry(
            Id: Guid.NewGuid().ToString("N")[..12],
            Utc: DateTime.UtcNow,
            Action: action,
            Detail: detail,
            FileCount: fileCount,
            Bytes: bytes,
            Result: result);
        var line = JsonSerializer.Serialize(entry, JsonOpts);
        ValidateLineLength(line);
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream);
            writer.WriteLine(line);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
    }

    // 在同一排他写句柄内核对并追加，避免不同管理器同时通过“尚未记录”的检查。
    internal void AppendOnce(string id, string action, string detail, int fileCount, long bytes, string result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var entry = new HistoryEntry(id, DateTime.UtcNow, action, detail, fileCount, bytes, result);
        var serialized = JsonSerializer.Serialize(entry, JsonOpts);
        ValidateLineLength(serialized);
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using var stream = new FileStream(_path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            var found = false;
            using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 4096, leaveOpen: true))
            {
                foreach (var line in ReadBoundedLines(reader, skipOversized: false))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    HistoryEntry existing;
                    try
                    {
                        existing = JsonSerializer.Deserialize<HistoryEntry>(line, JsonOpts)
                            ?? throw new InvalidDataException("历史记录为空，无法确认审计是否已写入");
                    }
                    catch (JsonException ex)
                    {
                        throw new InvalidDataException("历史记录损坏，保留恢复意图等待核对", ex);
                    }
                    if (existing.Id != id) continue;
                    if (existing != entry with { Utc = existing.Utc })
                        throw new InvalidDataException("同一审计标识对应不同内容，拒绝移除恢复意图");
                    found = true;
                }
            }
            if (found)
            {
                stream.Flush(flushToDisk: true);
                return;
            }
            // 完整 JSON 行可能尚未写入换行；不要将新记录粘到上一行。
            var needsNewLine = false;
            if (stream.Length > 0)
            {
                stream.Seek(-1, SeekOrigin.End);
                needsNewLine = stream.ReadByte() != '\n';
            }
            stream.Seek(0, SeekOrigin.End);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true);
            if (needsNewLine) writer.WriteLine();
            writer.WriteLine(serialized);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
    }

    private static void ValidateLineLength(string line)
    {
        if (line.Length > MaxLineChars)
            throw new InvalidDataException("历史单行超过长度上限，拒绝继续审计");
    }

    // 展示可跳过坏行；审计必须立即拒绝，不能读完整个异常长行才发现超限。
    internal static IEnumerable<string?> ReadBoundedLines(TextReader reader, bool skipOversized)
    {
        var buffer = new char[4096];
        var line = new StringBuilder(4096);
        var oversized = false;
        var afterCr = false;
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            var offset = 0;
            while (offset < read)
            {
                if (afterCr && buffer[offset] == '\n') { afterCr = false; offset++; continue; }
                afterCr = false;
                var cr = Array.IndexOf(buffer, '\r', offset, read - offset);
                var lf = Array.IndexOf(buffer, '\n', offset, read - offset);
                var end = cr < 0 ? lf : lf < 0 ? cr : Math.Min(cr, lf);
                var count = (end < 0 ? read : end) - offset;
                if (!oversized)
                {
                    if (line.Length + count > MaxLineChars)
                    {
                        if (!skipOversized)
                            throw new InvalidDataException("历史单行超过长度上限，保留恢复意图");
                        oversized = true;
                        line.Clear();
                    }
                    else line.Append(buffer, offset, count);
                }
                if (end >= 0)
                {
                    yield return oversized ? null : line.ToString();
                    line.Clear();
                    oversized = false;
                    afterCr = buffer[end] == '\r';
                    offset = end + 1;
                }
                else offset = read;
            }
        }
        if (oversized) yield return null;
        else if (line.Length > 0) yield return line.ToString();
    }

    /// <summary>最近 limit 条（新的在前），最多 1000 条；展示跳过损坏或超长行。</summary>
    public IReadOnlyList<HistoryEntry> Recent(int limit = 200)
    {
        lock (_lock)
        {
            if (limit <= 0 || !File.Exists(_path))
                return Array.Empty<HistoryEntry>();
            limit = Math.Min(limit, MaxRecentEntries);
            using var reader = new StreamReader(_path);
            var newest = new PriorityQueue<(HistoryEntry Entry, long Order), (long Utc, long Tie)>();
            long order = 0;
            foreach (var line in ReadBoundedLines(reader, skipOversized: true))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<HistoryEntry>(line, JsonOpts);
                    if (entry is null) continue;
                    var priority = (entry.Utc.Ticks, -order);
                    if (newest.Count < limit) newest.Enqueue((entry, order), priority);
                    else if (newest.TryPeek(out _, out var oldest) && priority.CompareTo(oldest) > 0)
                        newest.EnqueueDequeue((entry, order), priority);
                    order++;
                }
                catch
                {
                    // 单行损坏不阻塞整体展示
                }
            }
            return newest.UnorderedItems.Select(item => item.Element)
                .OrderByDescending(item => item.Entry.Utc).ThenBy(item => item.Order)
                .Select(item => item.Entry).ToList();
        }
    }
}
