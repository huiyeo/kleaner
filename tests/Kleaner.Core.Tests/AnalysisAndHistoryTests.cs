using Kleaner.Analysis;
using Kleaner.Executor;
using Xunit;

namespace Kleaner.Core.Tests;

public sealed class AnalysisAndHistoryTests : IDisposable
{
    private readonly string _root;

    public AnalysisAndHistoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kleaner-ana-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch { }
    }

    private static byte[] Content(long size, byte seed) => Enumerable.Repeat(seed, (int)size).ToArray();

    [Fact]
    public void 大文件扫描_按阈值过滤并降序()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllBytes(Path.Combine(_root, "big.bin"), Content(300, 1));
        File.WriteAllBytes(Path.Combine(_root, "small.bin"), Content(10, 2));
        File.WriteAllBytes(Path.Combine(_root, "sub", "mid.bin"), Content(100, 3));

        var items = LargeFileScanner.Scan(_root, minBytes: 50, top: 10);

        Assert.Equal(2, items.Count);
        Assert.True(items[0].SizeBytes >= items[1].SizeBytes);
        Assert.Contains(items, i => i.Path.EndsWith("big.bin", StringComparison.Ordinal));
        Assert.DoesNotContain(items, i => i.Path.EndsWith("small.bin", StringComparison.Ordinal));
    }

    [Fact]
    public void 重复文件_内容相同才成组_不同内容不误报()
    {
        var same1 = Path.Combine(_root, "a-copy1.bin");
        var same2 = Path.Combine(_root, "a-copy2.bin");
        var unique = Path.Combine(_root, "unique.bin");
        var payload = Content(4096, 7);
        File.WriteAllBytes(same1, payload);
        File.WriteAllBytes(same2, payload);
        File.WriteAllBytes(unique, Content(4096, 8));

        var groups = DuplicateFinder.Find(_root, minBytesPerFile: 1024);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Files.Count);
        Assert.Contains(group.Files, f => f.EndsWith("a-copy1.bin", StringComparison.Ordinal));
        Assert.Contains(group.Files, f => f.EndsWith("a-copy2.bin", StringComparison.Ordinal));
    }

    [Fact]
    public void 重复文件_相同大小不同内容_全量哈希排除()
    {
        // 两个同大小、首 64KB 也相同（内容超过 64KB 后才分叉）的文件——首块预筛无法区分，全量哈希必须兜底
        var dir = Path.Combine(_root, "deep");
        Directory.CreateDirectory(dir);
        var f1 = Path.Combine(dir, "x1.bin");
        var f2 = Path.Combine(dir, "x2.bin");
        var shared = Content(DuplicateFinder.PartialHashBytes + 1024, 9);
        var b1 = (byte[])shared.Clone();
        var b2 = (byte[])shared.Clone();
        b2[^1] ^= 0xFF;
        File.WriteAllBytes(f1, b1);
        File.WriteAllBytes(f2, b2);

        var groups = DuplicateFinder.Find(_root, minBytesPerFile: 1024);

        Assert.Empty(groups);
        _ = f1;
        _ = f2;
    }

    [Fact]
    public void 重复文件_首块即相同且全文件相同_首块哈希路径成组()
    {
        Directory.CreateDirectory(_root);
        var payload = Content(2048, 5); // 小于 64KB：单文件哈希一次算完
        var a = Path.Combine(_root, "dup-a.dat");
        var b = Path.Combine(_root, "dup-b.dat");
        File.WriteAllBytes(a, payload);
        File.WriteAllBytes(b, payload);

        var groups = DuplicateFinder.Find(_root, minBytesPerFile: 1024);

        Assert.Single(groups);
        Assert.Equal(2, groups[0].Files.Count);
    }

    [Fact]
    public void 空间分析_一级子项递归求和排序()
    {
        var big = Path.Combine(_root, "bigdir");
        var small = Path.Combine(_root, "smalldir");
        Directory.CreateDirectory(Path.Combine(big, "nested"));
        Directory.CreateDirectory(small);
        File.WriteAllBytes(Path.Combine(big, "f1.bin"), Content(500, 1));
        File.WriteAllBytes(Path.Combine(big, "nested", "f2.bin"), Content(300, 1));
        File.WriteAllBytes(Path.Combine(small, "f3.bin"), Content(100, 1));
        File.WriteAllBytes(Path.Combine(_root, "loose.bin"), Content(50, 1));

        var items = DiskUsageAnalyzer.TopLevel(_root);

        Assert.Equal(3, items.Count);
        Assert.Equal("bigdir", Path.GetFileName(items[0].Path));
        Assert.True(items[0].IsDirectory);
        Assert.Equal(800, items[0].SizeBytes);
        Assert.Equal(50, items.Single(i => !i.IsDirectory).SizeBytes);
    }

    [Fact]
    public void 操作历史_追加可读_损坏行不阻塞()
    {
        var path = Path.Combine(_root, "history.jsonl");
        var history = new HistoryManager(path);
        history.Append("clean", "批次 1", 3, 123, "ok");
        history.Append("restore", "批次 1", 3, 0, "ok");

        File.AppendAllText(path, "{corrupted line}\n");

        var entries = new HistoryManager(path).Recent();
        Assert.Equal(2, entries.Count);
        Assert.Equal("restore", entries[0].Action); // 新的在前
        Assert.Equal("批次 1", entries[0].Detail);
    }

    [Fact]
    public void 隔离区操作_自动写入历史()
    {
        var src = Path.Combine(_root, "src");
        Directory.CreateDirectory(src);
        var f = Path.Combine(src, "x.txt");
        File.WriteAllText(f, "data");
        var history = new HistoryManager(Path.Combine(_root, "h2.jsonl"));
        var manager = new QuarantineManager(Path.Combine(_root, "q"), history);

        var report = manager.Execute(new[] { ("rule-a", new Kleaner.Core.FileCandidate(f, 4, DateTime.UtcNow)) });
        manager.RestoreBatch(report.BatchId);

        var entries = history.Recent();
        Assert.Contains(entries, e => e.Action == "clean");
        Assert.Contains(entries, e => e.Action == "restore");
        var clean = entries.Single(e => e.Action == "clean");
        Assert.Equal(1, clean.FileCount);
        Assert.Equal(4, clean.Bytes);
        Assert.Contains("rule-a", clean.Detail);
    }
}
