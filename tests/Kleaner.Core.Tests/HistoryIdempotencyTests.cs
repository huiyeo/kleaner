using Kleaner.Executor;

namespace Kleaner.Core.Tests;

public sealed class HistoryIdempotencyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kleaner-history-" + Guid.NewGuid().ToString("N"));
    private string HistoryPath => Path.Combine(_root, "history.jsonl");

    public HistoryIdempotencyTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void 重建管理器后相同审计只追加一次()
    {
        new HistoryManager(HistoryPath).AppendOnce("stable", "restore-file", "detail", 1, 7, "ok");
        var before = File.ReadAllBytes(HistoryPath);
        new HistoryManager(HistoryPath).AppendOnce("stable", "restore-file", "detail", 1, 7, "ok");
        Assert.Equal(before, File.ReadAllBytes(HistoryPath));
        Assert.Single(new HistoryManager(HistoryPath).Recent());
    }

    [Fact]
    public void 同标识不同内容拒绝且保留原日志()
    {
        var history = new HistoryManager(HistoryPath);
        history.AppendOnce("stable", "restore-file", "original", 1, 7, "ok");
        var before = File.ReadAllBytes(HistoryPath);
        Assert.Throws<InvalidDataException>(() => history.AppendOnce("stable", "restore-file", "changed", 1, 7, "ok"));
        Assert.Equal(before, File.ReadAllBytes(HistoryPath));
    }

    [Theory]
    [InlineData("{\"id\":")]
    [InlineData("null")]
    public void 损坏日志不追加不覆盖(string corrupt)
    {
        File.WriteAllText(HistoryPath, corrupt);
        Assert.Throws<InvalidDataException>(() => new HistoryManager(HistoryPath)
            .AppendOnce("stable", "restore-file", "detail", 1, 7, "ok"));
        Assert.Equal(corrupt, File.ReadAllText(HistoryPath));
    }

    [Fact]
    public void 完整尾行缺少换行时仍分开追加()
    {
        var history = new HistoryManager(HistoryPath);
        history.AppendOnce("first", "restore-file", "detail", 1, 7, "ok");
        File.WriteAllText(HistoryPath, File.ReadAllText(HistoryPath).TrimEnd('\r', '\n'));
        history.AppendOnce("second", "restore-file", "detail", 1, 7, "ok");
        Assert.Equal(2, history.Recent().Count);
    }

    [Fact]
    public void 已有写句柄时拒绝竞争追加且释放后可重试()
    {
        var history = new HistoryManager(HistoryPath);
        history.EnsureWritable();
        using (var owner = new FileStream(HistoryPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            Assert.Throws<IOException>(() => new HistoryManager(HistoryPath)
                .AppendOnce("stable", "restore-file", "detail", 1, 7, "ok"));
            Assert.Equal(0, owner.Length);
        }
        history.AppendOnce("stable", "restore-file", "detail", 1, 7, "ok");
        Assert.Single(history.Recent());
    }

    [Fact]
    public void 超长历史行拒绝审计追加且不覆盖()
    {
        var line = System.Text.Json.JsonSerializer.Serialize(new HistoryEntry("old", DateTime.UtcNow,
            "restore-file", new string('x', 70_000), 1, 7, "ok"),
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        File.WriteAllText(HistoryPath, line);
        Assert.Throws<InvalidDataException>(() => new HistoryManager(HistoryPath)
            .AppendOnce("stable", "restore-file", "detail", 1, 7, "ok"));
        Assert.Equal(line, File.ReadAllText(HistoryPath));
    }

    [Fact]
    public void 历史展示跳过超长及空记录并保留后续正常行()
    {
        var history = new HistoryManager(HistoryPath);
        File.WriteAllText(HistoryPath, "null\n" + new string('x', 70_000) + "\n");
        history.Append("clean", "normal", 1, 7, "ok");
        Assert.Equal("normal", Assert.Single(history.Recent()).Detail);
    }

    [Fact]
    public void 审计读取超长无换行输入在有限读取量内拒绝()
    {
        using var reader = new RepeatingReader();
        Assert.Throws<InvalidDataException>(() => HistoryManager.ReadBoundedLines(reader, false).ToList());
        Assert.InRange(reader.CharactersRead, HistoryManager.MaxLineChars + 1, HistoryManager.MaxLineChars + 4096);
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("\r")]
    public void 分块读取保持行边界与末尾无换行(string separator)
    {
        var first = new string('a', 4095);
        using var reader = new StringReader(first + separator + new string('b', HistoryManager.MaxLineChars) + separator + "tail");
        var lines = HistoryManager.ReadBoundedLines(reader, false).ToArray();
        Assert.Equal(new[] { first, new string('b', HistoryManager.MaxLineChars), "tail" }, lines);
    }

    [Fact]
    public void 超限新记录在打开日志前拒绝()
    {
        var history = new HistoryManager(HistoryPath);
        Assert.Throws<InvalidDataException>(() => history.Append("test", new string('x', 70_000), 0, 0, "ok"));
        Assert.Throws<InvalidDataException>(() => history.AppendOnce("id", "test", new string('x', 70_000), 0, 0, "ok"));
        Assert.False(File.Exists(HistoryPath));
    }

    private sealed class RepeatingReader : TextReader
    {
        public int CharactersRead { get; private set; }
        public override int Read(char[] buffer, int index, int count)
        {
            Array.Fill(buffer, 'x', index, count);
            CharactersRead += count;
            return count;
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
