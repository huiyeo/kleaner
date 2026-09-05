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

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
