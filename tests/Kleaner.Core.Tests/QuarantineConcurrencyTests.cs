using Kleaner.Core;
using Kleaner.Executor;

namespace Kleaner.Core.Tests;

public sealed class QuarantineConcurrencyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kleaner-concurrency-" + Guid.NewGuid().ToString("N"));

    public QuarantineConcurrencyTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("clean")]
    [InlineData("restore")]
    [InlineData("delete")]
    [InlineData("purge")]
    public void 还原持有隔离区期间其他写操作在副作用前拒绝(string operation)
    {
        var quarantine = Path.Combine(_root, "q");
        var history = new HistoryManager(Path.Combine(_root, "history.jsonl"));
        var challenger = new QuarantineManager(quarantine, history);
        var original = MakePlan("first", quarantine);
        var batch = challenger.Execute(original);
        var next = MakePlan("next", quarantine);
        var invoked = false;
        Exception? error = null;
        var owner = new QuarantineManager(quarantine, history, snapshot =>
        {
            if (invoked || !snapshot.Entries.Any(entry => entry.State == "restoring")) return;
            invoked = true;
            // 在真实持久化回调内确定性重入，证明不同管理器不能交错修改同一根目录。
            error = Record.Exception(() =>
            {
                switch (operation)
                {
                    case "clean": challenger.Execute(next); break;
                    case "restore": challenger.RestoreBatch(batch.BatchId); break;
                    case "delete": challenger.DeleteBatch(batch.BatchId); break;
                    case "purge": challenger.PurgeOlderThan(TimeSpan.Zero); break;
                }
            });
        });

        var restored = owner.RestoreBatch(batch.BatchId);

        Assert.True(invoked);
        Assert.IsAssignableFrom<IOException>(error);
        Assert.True(restored.IsComplete);
        Assert.All(original.Items, item => Assert.Equal("content", File.ReadAllText(item.File.FullPath)));
        Assert.All(next.Items, item => Assert.True(File.Exists(item.File.FullPath)));
        Assert.DoesNotContain(history.Recent(), entry => entry.Action is "delete-batch-start" or "purge");
        Assert.Single(history.Recent(), entry => entry.Action == "restore-start");
        Assert.Equal(1, challenger.Execute(next).MovedCount); // 正常返回后持有权必须释放。
    }

    private CleanupPlan MakePlan(string name, string quarantine)
    {
        var source = Path.Combine(_root, name);
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "a.txt"), "content");
        var rule = new Rule("temporary", "临时测试规则", RuleCategory.Application, RiskLevel.Low,
            new[] { Path.Combine(source, "**") }, Array.Empty<string>(), 0, null, false, true,
            "仅在临时夹具中验证隔离区并发拒绝。");
        var set = new RuleSet(1, null, 0, new[] { rule });
        return CleanupPlanBuilder.Create(set, new ScanEngine(quarantine).Scan(set), new[] { rule.Id }, quarantine);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
