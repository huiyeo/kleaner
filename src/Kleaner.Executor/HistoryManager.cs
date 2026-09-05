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
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using var stream = new FileStream(_path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            var found = false;
            using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 4096, leaveOpen: true))
            {
                while (reader.ReadLine() is { } line)
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
            writer.WriteLine(JsonSerializer.Serialize(entry, JsonOpts));
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
    }

    /// <summary>最近 limit 条（新的在前）。</summary>
    public IReadOnlyList<HistoryEntry> Recent(int limit = 200)
    {
        lock (_lock)
        {
            if (!File.Exists(_path))
                return Array.Empty<HistoryEntry>();
            var lines = File.ReadAllLines(_path);
            var list = new List<HistoryEntry>(lines.Length);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                try
                {
                    list.Add(JsonSerializer.Deserialize<HistoryEntry>(line, JsonOpts)!);
                }
                catch
                {
                    // 单行损坏不阻塞整体展示
                }
            }
            return list.OrderByDescending(e => e.Utc).Take(limit).ToList();
        }
    }
}
