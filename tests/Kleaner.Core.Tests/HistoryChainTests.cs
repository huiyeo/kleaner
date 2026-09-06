using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kleaner.Executor;

namespace Kleaner.Core.Tests;

public sealed class HistoryChainTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kleaner-chain-" + Guid.NewGuid().ToString("N"));

    private string HistoryPath => Path.Combine(_root, "history.jsonl");

    private string HeadPath => Path.Combine(_root, "history.jsonl.head");

    public HistoryChainTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    internal static string HashLine(string line) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(line)));

    private static string Serialize(string id, string detail, string? prev) =>
        JsonSerializer.Serialize(new HistoryEntry(id, DateTime.UtcNow, "restore", detail, 1, 7, "ok", prev),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    private IReadOnlyList<string> Lines() =>
        File.ReadAllLines(HistoryPath).Where(l => l.Length > 0).ToArray();

    [Fact]
    public void 追加记录互相链接且链检查点随最后一条更新()
    {
        var history = new HistoryManager(HistoryPath);
        history.Append("clean", "一", 1, 1, "ok");
        history.Append("clean", "二", 2, 2, "ok");
        history.Append("clean", "三", 3, 3, "ok");

        var lines = Lines();
        Assert.True(lines.Count == 3, "应有三条记录");
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var first = JsonSerializer.Deserialize<HistoryEntry>(lines[0], options)!;
        var second = JsonSerializer.Deserialize<HistoryEntry>(lines[1], options)!;
        var third = JsonSerializer.Deserialize<HistoryEntry>(lines[2], options)!;
        Assert.Null(first.Prev);
        Assert.Equal(HashLine(lines[0]), second.Prev);
        Assert.Equal(HashLine(lines[1]), third.Prev);
        Assert.Equal(HashLine(lines[2]), File.ReadAllText(HeadPath).Trim());
    }

    [Fact]
    public void 删除中间记录在校验中报告断裂位置()
    {
        var history = new HistoryManager(HistoryPath);
        history.Append("clean", "一", 1, 1, "ok");
        history.Append("clean", "二", 2, 2, "ok");
        history.Append("clean", "三", 3, 3, "ok");
        File.WriteAllLines(HistoryPath, new[] { Lines()[0], Lines()[2] });

        var snapshot = history.RecentVerified();

        Assert.True(snapshot.Integrity.Broken, "删除中间记录必须被链校验发现");
        Assert.Equal(2, snapshot.Integrity.BrokenAt);
        Assert.Equal(2, snapshot.Entries.Count);
    }

    [Fact]
    public void 尾部截断被链检查点检出且检查点滞后不误报()
    {
        var history = new HistoryManager(HistoryPath);
        history.Append("clean", "一", 1, 1, "ok");
        history.Append("clean", "二", 2, 2, "ok");
        history.Append("clean", "三", 3, 3, "ok");
        File.WriteAllLines(HistoryPath, new[] { Lines()[0], Lines()[1] });
        Assert.True(history.RecentVerified().Integrity.HeadMismatch, "截断尾部必须与链检查点不符");

        // 崩溃可能让检查点落后于文件：文件只是正常增长时不得报假警报。
        var staleHead = Lines()[0];
        history.Append("clean", "三", 3, 3, "ok");
        File.WriteAllText(HeadPath, HashLine(staleHead));

        var integrity = history.RecentVerified().Integrity;
        Assert.False(integrity.HeadMismatch, "检查点对应的行仍在文件中时不应报警");
        Assert.False(integrity.Broken);
    }

    [Fact]
    public void 插入伪造记录被链条检出()
    {
        var history = new HistoryManager(HistoryPath);
        history.Append("clean", "一", 1, 1, "ok");
        history.Append("clean", "三", 3, 3, "ok");
        var forged = Serialize("f0rged12", "伪造", new string('0', 64));
        File.WriteAllLines(HistoryPath, new[] { Lines()[0], forged, Lines()[1] });

        var snapshot = history.RecentVerified();

        Assert.True(snapshot.Integrity.Broken, "插入的伪造记录没有合法前置链接");
        Assert.Equal(2, snapshot.Integrity.BrokenAt);
    }

    [Fact]
    public void 旧版无链记录可读且新记录自其原文接链()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllLines(HistoryPath, new[]
        {
            Serialize("legacy000001", "旧记录一", null),
            Serialize("legacy000002", "旧记录二", null),
        });
        var history = new HistoryManager(HistoryPath);
        history.Append("clean", "新记录", 1, 1, "ok");

        var snapshot = history.RecentVerified();
        var lines = Lines();

        Assert.True(snapshot.Integrity.IsValid);
        Assert.Equal(2, snapshot.Integrity.UnchainedCount);
        Assert.Equal(HashLine(lines[1]), JsonSerializer.Deserialize<HistoryEntry>(lines[2],
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!.Prev);
    }

    [Fact]
    public void 链条断裂时补记拒绝继续追加()
    {
        var history = new HistoryManager(HistoryPath);
        history.AppendOnce("first0000001", "restore-file", "一", 1, 1, "ok");
        history.AppendOnce("second0000002", "restore-file", "二", 1, 1, "ok");
        File.WriteAllLines(HistoryPath, new[] { Lines()[1] }); // 第二条的 prev 指向已被删除的第一条。

        var reopened = new HistoryManager(HistoryPath);
        Assert.Throws<InvalidDataException>(
            () => reopened.AppendOnce("third0000003", "restore-file", "三", 1, 1, "ok"));
        Assert.Single(reopened.Recent());
    }
}
