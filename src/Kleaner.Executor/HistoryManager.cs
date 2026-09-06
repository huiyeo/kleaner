using System.Security.Cryptography;
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
    string Result,      // ok | partial | failed | cancelled
    string? Prev = null) // 前一行原文的 SHA-256；null 仅允许出现在启用链保护之前的首条记录
{
    [JsonIgnore]
    public string TimeDisplay => Utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}

/// <summary>历史哈希链的只读校验结果；断裂只意味着删改可被发现，不构成防篡改承诺（ADR-0002）。</summary>
public sealed record HistoryIntegrity(bool Broken, int? BrokenAt, bool HeadMismatch, int UnchainedCount)
{
    public bool IsValid => !Broken && !HeadMismatch;
}

public sealed record HistorySnapshot(IReadOnlyList<HistoryEntry> Entries, HistoryIntegrity Integrity);

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

    // 检查点与历史文件一一对应：同目录可能并存多个历史文件，共享检查点会互相误报。
    private string HeadPath => _path + ".head";

    internal static string HashLine(string line) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(line)));

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
        lock (_lock)
        {
            var seed = SeedFromLastLine();
            var entry = new HistoryEntry(
                Id: Guid.NewGuid().ToString("N")[..12],
                Utc: DateTime.UtcNow,
                Action: action,
                Detail: detail,
                FileCount: fileCount,
                Bytes: bytes,
                Result: result,
                Prev: seed);
            var line = JsonSerializer.Serialize(entry, JsonOpts);
            ValidateLineLength(line);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using (var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                using var writer = new StreamWriter(stream);
                writer.WriteLine(line);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            UpdateHead(HashLine(line));
        }
    }

    // 新记录链接到当前最后一行的原文哈希；空文件返回 null，旧版记录同样作为链起点。
    // 最后一行是超长行时无法取到原文，返回 null 让链在敌意行之后重新起段。
    private string? SeedFromLastLine()
    {
        if (!File.Exists(_path)) return null;
        string? last = null;
        using (var reader = new StreamReader(_path))
        {
            foreach (var line in ReadBoundedLines(reader, skipOversized: true))
            {
                if (line is null) { last = null; continue; }
                if (!string.IsNullOrWhiteSpace(line)) last = line;
            }
        }
        return last is null ? null : HashLine(last);
    }

    // 检查点滞后只是缩小尾部截断的检测范围；写入失败必须中止审计，避免静默失去检测能力。
    private void UpdateHead(string hash)
    {
        var temporary = HeadPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, hash);
            File.Move(temporary, HeadPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try { File.Delete(temporary); } catch { }
            }
        }
    }

    // 在同一排他写句柄内核对并追加，避免不同管理器同时通过“尚未记录”的检查。
    internal void AppendOnce(string id, string action, string detail, int fileCount, long bytes, string result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        // 保持“超限记录在打开日志前拒绝”的契约：prev 最多再加约 74 个字符，预留 80 余量先做保守上限检查。
        var bound = JsonSerializer.Serialize(new HistoryEntry(id, DateTime.UtcNow, action, detail, fileCount, bytes, result), JsonOpts);
        if (bound.Length + 80 > MaxLineChars)
            throw new InvalidDataException("历史单行超过长度上限，拒绝继续审计");
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using var stream = new FileStream(_path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            var found = false;
            string? lastLine = null;
            var rawLines = new List<string>();
            using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 4096, leaveOpen: true))
            {
                foreach (var line in ReadBoundedLines(reader, skipOversized: false))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    rawLines.Add(line);
                    lastLine = line;
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
                    if (existing.Action != action || existing.Detail != detail ||
                        existing.FileCount != fileCount || existing.Bytes != bytes || existing.Result != result)
                        throw new InvalidDataException("同一审计标识对应不同内容，拒绝移除恢复意图");
                    found = true;
                }
            }
            // 断裂的历史不能成为补记依据：先核对链条，再决定是否追加。
            var headHash = File.Exists(HeadPath) ? File.ReadAllText(HeadPath).Trim() : null;
            if (!VerifyChain(rawLines, headHash).IsValid)
                throw new InvalidDataException("历史完整性校验失败，拒绝在可疑历史之上补记，请先人工核对历史文件");
            if (found)
            {
                stream.Flush(flushToDisk: true);
                return;
            }
            var entry = new HistoryEntry(id, DateTime.UtcNow, action, detail, fileCount, bytes, result,
                lastLine is null ? null : HashLine(lastLine));
            var serialized = JsonSerializer.Serialize(entry, JsonOpts);
            ValidateLineLength(serialized);
            // 完整 JSON 行可能尚未写入换行；不要将新记录粘到上一行。
            var needsNewLine = false;
            if (stream.Length > 0)
            {
                stream.Seek(-1, SeekOrigin.End);
                needsNewLine = stream.ReadByte() != '\n';
            }
            stream.Seek(0, SeekOrigin.End);
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true))
            {
                if (needsNewLine) writer.WriteLine();
                writer.WriteLine(serialized);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            UpdateHead(HashLine(serialized));
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

    /// <summary>带哈希链校验的最近记录；校验失败时记录照常列出，但完整性结论随快照返回，不得当作完整历史展示。</summary>
    public HistorySnapshot RecentVerified(int limit = 200)
    {
        lock (_lock)
        {
            var entries = Recent(limit);
            if (!File.Exists(_path))
                return new HistorySnapshot(entries, new HistoryIntegrity(false, null, false, 0));
            string? headHash = null;
            if (File.Exists(HeadPath))
                headHash = File.ReadAllText(HeadPath).Trim();
            var rawLines = new List<string>();
            using (var reader = new StreamReader(_path))
            {
                foreach (var line in ReadBoundedLines(reader, skipOversized: true))
                {
                    if (string.IsNullOrWhiteSpace(line)) { rawLines.Add(string.Empty); continue; }
                    rawLines.Add(line);
                }
            }
            return new HistorySnapshot(entries, VerifyChain(rawLines, headHash));
        }
    }

    /// <summary>按行校验哈希链。无法解析的行仍参与链接（按原文哈希），首条带前置链接的记录必须能接上文件内的前驱。</summary>
    internal static HistoryIntegrity VerifyChain(IReadOnlyList<string> rawLines, string? headHash)
    {
        var chainingStarted = false;
        var unchained = 0;
        string? previousHash = null;
        for (var index = 0; index < rawLines.Count; index++)
        {
            var raw = rawLines[index];
            if (raw.Length == 0)
            {
                // 超长行被读取边界截断，原文不可得，链条自此无法延续。
                if (chainingStarted || headHash is not null)
                    return new HistoryIntegrity(true, index + 1, false, unchained);
                continue;
            }
            var currentHash = HashLine(raw);
            HistoryEntry? entry = null;
            try { entry = JsonSerializer.Deserialize<HistoryEntry>(raw, JsonOpts); }
            catch (JsonException) { }
            if (entry is null)
            {
                // 无法解析的行不判断裂，但其哈希仍作为下一行的链接目标。
                previousHash = currentHash;
                continue;
            }
            if (entry.Prev is null)
            {
                if (chainingStarted)
                    return new HistoryIntegrity(true, index + 1, false, unchained);
                unchained++;
            }
            else
            {
                // previousHash 为空只发生在首行：带前置链接的首行意味着其前驱已被删除。
                if (previousHash is null || entry.Prev != previousHash)
                    return new HistoryIntegrity(true, index + 1, false, unchained);
                chainingStarted = true;
            }
            previousHash = currentHash;
        }
        var headMismatch = false;
        if (headHash is not null)
        {
            // 检查点对应文件中任意一行即视为完好：文件可以在检查点之后正常增长；找不到则尾部被截断或改写。
            headMismatch = !rawLines.Any(line => line.Length > 0 && HashLine(line) == headHash);
        }
        return new HistoryIntegrity(false, null, headMismatch, unchained);
    }
}
