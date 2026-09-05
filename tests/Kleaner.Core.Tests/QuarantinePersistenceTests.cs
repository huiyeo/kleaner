using Kleaner.Core;
using Kleaner.Executor;

namespace Kleaner.Core.Tests;

public sealed class QuarantinePersistenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kleaner-persistence-" + Guid.NewGuid().ToString("N"));

    public QuarantinePersistenceTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("initial")]
    [InlineData("pending")]
    [InlineData("moved")]
    public void 清理清单替换失败立即停止且保留最后有效清单(string phase)
    {
        var history = new HistoryManager(Path.Combine(_root, "history.jsonl"));
        var quarantine = Path.Combine(_root, "q");
        var plan = MakePlan(quarantine);
        var injected = false;
        var manager = new QuarantineManager(quarantine, history, batch =>
        {
            var shouldFail = phase switch
            {
                "initial" => batch.Entries.Count == 0,
                "pending" => batch.Entries.Any(e => e.State == "pending"),
                _ => batch.Entries.Any(e => e.State == "moved")
            };
            if (!injected && shouldFail)
            {
                injected = true;
                throw new IOException("模拟一次清单原子替换失败");
            }
        });

        Assert.Throws<IOException>(() => manager.Execute(plan));
        Assert.True(injected);
        var originals = plan.Items.Count(item => File.Exists(item.File.FullPath));
        Assert.Equal(phase == "moved" ? 1 : 2, originals);
        Assert.Empty(Directory.GetFiles(quarantine, "*.tmp", SearchOption.AllDirectories));
        var reopened = new QuarantineManager(quarantine, history);
        if (phase == "moved")
        {
            var batch = Assert.Single(reopened.ListBatches());
            Assert.Equal("pending", Assert.Single(batch.Entries).State);
            Assert.True(File.Exists(batch.Entries[0].QuarantinedPath));
            Assert.True(reopened.RestoreBatch(batch.BatchId).IsComplete);
            Assert.All(plan.Items, item => Assert.Equal("content", File.ReadAllText(item.File.FullPath)));
        }
        Assert.DoesNotContain(history.Recent(), entry => entry.Action == "clean" && entry.Result == "ok");
    }

    [Fact]
    public void 还原清单替换失败停止后续移动且保留批次()
    {
        var history = new HistoryManager(Path.Combine(_root, "history.jsonl"));
        var quarantine = Path.Combine(_root, "q");
        var plan = MakePlan(quarantine);
        var execution = new QuarantineManager(quarantine, history).Execute(plan);
        var injected = false;
        var manager = new QuarantineManager(quarantine, history, snapshot =>
        {
            if (!injected && snapshot.Entries.Count == 1)
            {
                injected = true;
                throw new IOException("模拟还原清单替换失败");
            }
        });

        var result = manager.RestoreBatch(execution.BatchId);
        Assert.False(result.IsComplete);
        Assert.Equal(1, result.RestoredCount);
        Assert.NotEmpty(result.Failed);
        Assert.Equal(1, plan.Items.Count(item => File.Exists(item.File.FullPath)));
        var reopened = new QuarantineManager(quarantine, history);
        var batch = Assert.Single(reopened.ListBatches());
        Assert.Equal(2, batch.Entries.Count);
        Assert.Single(batch.Entries, entry => File.Exists(entry.QuarantinedPath));
        Assert.DoesNotContain(history.Recent(), entry => entry.Action == "restore" && entry.Result == "ok");
        var recovery = reopened.RestoreBatch(batch.BatchId);
        Assert.Equal(2, recovery.RestoredCount);
        Assert.True(recovery.IsComplete);
        Assert.Empty(reopened.ListBatches());
        Assert.All(plan.Items, item => Assert.Equal("content", File.ReadAllText(item.File.FullPath)));
    }

    private CleanupPlan MakePlan(string quarantine)
    {
        var source = Path.Combine(_root, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "a.txt"), "content");
        File.WriteAllText(Path.Combine(source, "b.txt"), "content");
        var rule = new Rule("temporary", "临时测试规则", RuleCategory.Application, RiskLevel.Low,
            new[] { Path.Combine(source, "**") }, Array.Empty<string>(), 0, null, false, true,
            "仅使用测试临时目录的规则，验证清单故障时的文件保留行为。");
        var set = new RuleSet(1, null, 0, new[] { rule });
        return CleanupPlanBuilder.Create(set, new ScanEngine(quarantine).Scan(set), new[] { rule.Id }, quarantine);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void 重启核对固定目标与内容且不覆盖新文件(bool conflict, bool tamper)
    {
        var history = new HistoryManager(Path.Combine(_root, "history.jsonl"));
        var quarantine = Path.Combine(_root, "q");
        var plan = MakePlan(quarantine);
        var normal = new QuarantineManager(quarantine, history);
        var execution = normal.Execute(plan);
        var first = Assert.Single(normal.ListBatches()).Entries[0];
        if (conflict) File.WriteAllText(first.OriginalPath, "user-new-file");
        var interrupted = new QuarantineManager(quarantine, history, snapshot =>
        {
            if (snapshot.Entries.Count == 1) throw new IOException("移动后的状态提交中断");
        });
        Assert.False(interrupted.RestoreBatch(execution.BatchId).IsComplete);
        var persisted = Assert.Single(normal.ListBatches()).Entries[0];
        Assert.Equal("restoring", persisted.State);
        Assert.NotNull(persisted.RestoreTarget);
        Assert.Equal("content", File.ReadAllText(persisted.RestoreTarget));
        if (tamper) File.WriteAllText(persisted.RestoreTarget, "changed"); // 相同字节数仍需校验摘要。

        var reopened = new QuarantineManager(quarantine, history);
        var result = reopened.RestoreBatch(execution.BatchId);

        Assert.Equal(!tamper, result.IsComplete);
        Assert.Equal(tamper ? "changed" : "content", File.ReadAllText(persisted.RestoreTarget));
        if (conflict) Assert.Equal("user-new-file", File.ReadAllText(first.OriginalPath));
        if (tamper) Assert.Single(Assert.Single(reopened.ListBatches()).Entries);
        else Assert.Empty(reopened.ListBatches());
    }

    [Fact]
    public void 还原意图未落盘时不移动任何文件()
    {
        var history = new HistoryManager(Path.Combine(_root, "history.jsonl"));
        var quarantine = Path.Combine(_root, "q");
        var plan = MakePlan(quarantine);
        var normal = new QuarantineManager(quarantine, history);
        var execution = normal.Execute(plan);
        var faulty = new QuarantineManager(quarantine, history, _ => throw new IOException("意图写入失败"));

        var report = faulty.RestoreBatch(execution.BatchId);

        Assert.Equal(0, report.RestoredCount);
        Assert.False(report.IsComplete);
        Assert.All(plan.Items, item => Assert.False(File.Exists(item.File.FullPath)));
        Assert.All(Assert.Single(normal.ListBatches()).Entries, item => Assert.Equal("moved", item.State));
        Assert.True(normal.RestoreBatch(execution.BatchId).IsComplete);
    }

    [Fact]
    public void 审计在初始化后失去写权限时不移动文件()
    {
        var history = new HistoryManager(Path.Combine(_root, "history.jsonl"));
        var quarantine = Path.Combine(_root, "q");
        var plan = MakePlan(quarantine);
        var manager = new QuarantineManager(quarantine, history);
        using var locked = File.Open(history.FilePath, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Throws<IOException>(() => manager.Execute(plan));
        Assert.All(plan.Items, item => Assert.Equal("content", File.ReadAllText(item.File.FullPath)));
        Assert.Empty(Assert.Single(manager.ListBatches()).Entries);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
