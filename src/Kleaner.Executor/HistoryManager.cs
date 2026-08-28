using System.Text.Json;
using System.Text.Json.Serialization;

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
            File.AppendAllText(_path, line + Environment.NewLine);
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
