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
        Assert.Equal(2, history.Recent().Count(entry => entry.Action == "restore-file"));
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

    [Fact]
    public void 还原逐项审计失败保留意图并停止后续移动()
    {
        var history = new HistoryManager(Path.Combine(_root, "history.jsonl"));
        var quarantine = Path.Combine(_root, "q");
        var plan = MakePlan(quarantine);
        var normal = new QuarantineManager(quarantine, history);
        var execution = normal.Execute(plan);
        FileStream? locked = null;
        var faulty = new QuarantineManager(quarantine, history, snapshot =>
        {
            if (locked is null && snapshot.Entries.Any(entry => entry.State == "restoring"))
                locked = File.Open(history.FilePath, FileMode.Open, FileAccess.Read, FileShare.None);
        });
        try
        {
            Assert.Throws<IOException>(() => faulty.RestoreBatch(execution.BatchId));
            var persisted = Assert.Single(normal.ListBatches());
            Assert.Equal(2, persisted.Entries.Count);
            Assert.Equal("restoring", persisted.Entries[0].State);
            Assert.Single(plan.Items, item => File.Exists(item.File.FullPath));
        }
        finally { locked?.Dispose(); }

        Assert.True(normal.RestoreBatch(execution.BatchId).IsComplete);
        Assert.Equal(2, history.Recent().Count(entry => entry.Action == "restore-file"));
        Assert.All(plan.Items, item => Assert.Equal("content", File.ReadAllText(item.File.FullPath)));
    }

    [Fact]
    public void 还原最终汇总审计失败仍保留全部逐项证据()
    {
        var history = new HistoryManager(Path.Combine(_root, "history.jsonl"));
        var quarantine = Path.Combine(_root, "q");
        var plan = MakePlan(quarantine);
        var execution = new QuarantineManager(quarantine, history).Execute(plan);
        FileStream? locked = null;
        var faulty = new QuarantineManager(quarantine, history, snapshot =>
        {
            if (snapshot.Entries.Count == 0)
                locked = File.Open(history.FilePath, FileMode.Open, FileAccess.Read, FileShare.None);
        });
        try { Assert.Throws<IOException>(() => faulty.RestoreBatch(execution.BatchId)); }
        finally { locked?.Dispose(); }

        var records = history.Recent().Where(entry => entry.Action == "restore-file").ToArray();
        Assert.Equal(2, records.Length);
        Assert.All(records, record =>
        {
            using var detail = System.Text.Json.JsonDocument.Parse(record.Detail);
            Assert.Equal(execution.BatchId, detail.RootElement.GetProperty("batchId").GetString());
            var target = detail.RootElement.GetProperty("restoreTarget").GetString()!;
            Assert.Equal("content", File.ReadAllText(target));
            Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(target))),
                detail.RootElement.GetProperty("restoreSha256").GetString());
        });
        Assert.DoesNotContain(history.Recent(), entry => entry.Action == "restore");
        Assert.True(Directory.Exists(Path.Combine(quarantine, ".pending-audit")), "汇总失败必须保留独立于批次的待补记凭据");
        var receiptPath = Assert.Single(Directory.GetFiles(Path.Combine(quarantine, ".pending-audit"), "*.json"));
        var receiptBytes = File.ReadAllBytes(receiptPath);
        var reopened = new QuarantineManager(quarantine, history);
        reopened.RecoverPendingAudit();
        var summary = Assert.Single(history.Recent(), entry => entry.Action == "restore");
        Assert.Equal(2, summary.FileCount);
        Assert.Equal("ok", summary.Result);
        Assert.False(File.Exists(receiptPath));
        File.WriteAllBytes(receiptPath, receiptBytes); // 模拟审计成功后、凭据移除前中断。
        reopened.RecoverPendingAudit();
        Assert.Single(history.Recent(), entry => entry.Action == "restore");
        Assert.All(plan.Items, item => Assert.Equal("content", File.ReadAllText(item.File.FullPath)));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    public void 损坏待补记凭据阻止新清理并保留证据(string corrupt)
    {
        var history = new HistoryManager(Path.Combine(_root, "history.jsonl"));
        var quarantine = Path.Combine(_root, "q");
        var plan = MakePlan(quarantine);
        var manager = new QuarantineManager(quarantine, history);
        var directory = Path.Combine(quarantine, ".pending-audit");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, corrupt);

        Assert.NotNull(Record.Exception(() => manager.Execute(plan)));
        Assert.Equal(corrupt, File.ReadAllText(path));
        Assert.All(plan.Items, item => Assert.Equal("content", File.ReadAllText(item.File.FullPath)));
        Assert.Empty(history.Recent());
        Assert.Empty(manager.ListBatches());
    }

    [Fact]
    public void 待补记目录冲突时首次还原不移动文件()
    {
        var history = new HistoryManager(Path.Combine(_root, "history.jsonl"));
        var quarantine = Path.Combine(_root, "q");
        var plan = MakePlan(quarantine);
        var manager = new QuarantineManager(quarantine, history);
        var execution = manager.Execute(plan);
        Directory.Delete(Path.Combine(quarantine, ".pending-audit"), recursive: false);
        File.WriteAllText(Path.Combine(quarantine, ".pending-audit"), "保留的冲突文件");
        Assert.Throws<InvalidDataException>(() => manager.RestoreBatch(execution.BatchId));
        Assert.True(File.Exists(Path.Combine(execution.QuarantineDir, "manifest.json")));
        Assert.Equal("保留的冲突文件", File.ReadAllText(Path.Combine(quarantine, ".pending-audit")));
        Assert.Throws<InvalidDataException>(() => manager.Execute(plan));
        Assert.DoesNotContain(history.Recent(), entry => entry.Action == "restore");
        Assert.All(plan.Items, item => Assert.False(File.Exists(item.File.FullPath)));
        Assert.All(Assert.Single(manager.ListBatches()).Entries, entry => Assert.Equal("content", File.ReadAllText(entry.QuarantinedPath)));
    }

    [Fact]
    public void 待补记结果替换失败不删除批次清单()
    {
        var history = new HistoryManager(Path.Combine(_root, "history.jsonl"));
        var quarantine = Path.Combine(_root, "q");
        var plan = MakePlan(quarantine);
        var manager = new QuarantineManager(quarantine, history);
        var execution = manager.Execute(plan);
        FileStream? locked = null;
        var interrupted = new QuarantineManager(quarantine, history, snapshot =>
        {
            if (snapshot.Entries.Count == 0)
            {
                var path = Assert.Single(Directory.GetFiles(Path.Combine(quarantine, ".pending-audit"), "*.json"));
                locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
            }
        });
        try
        {
            var error = Record.Exception(() => interrupted.RestoreBatch(execution.BatchId));
            Assert.True(error is IOException or UnauthorizedAccessException, $"应拒绝替换被锁定的凭据，实际为 {error?.GetType().Name}");
        }
        finally { locked?.Dispose(); }
        Assert.True(File.Exists(Path.Combine(execution.QuarantineDir, "manifest.json")));
        Assert.DoesNotContain(history.Recent(), entry => entry.Action == "restore");
        manager.RecoverPendingAudit();
        var summary = Assert.Single(history.Recent(), entry => entry.Action == "restore");
        Assert.Equal("partial", summary.Result);
        Assert.Contains("下限", summary.Detail);
        Assert.All(plan.Items, item => Assert.Equal("content", File.ReadAllText(item.File.FullPath)));
    }

    [Fact]
    public void 首次还原移动前已持久化待补记凭据()
    {
        var history = new HistoryManager(Path.Combine(_root, "history.jsonl"));
        var quarantine = Path.Combine(_root, "q");
        var plan = MakePlan(quarantine);
        var execution = new QuarantineManager(quarantine, history).Execute(plan);
        var observed = false;
        var manager = new QuarantineManager(quarantine, history, snapshot =>
        {
            if (!snapshot.Entries.Any(entry => entry.State == "restoring")) return;
            observed = Directory.Exists(Path.Combine(quarantine, ".pending-audit")) &&
                Directory.GetFiles(Path.Combine(quarantine, ".pending-audit"), "*.json").Length == 1;
        });
        Assert.True(manager.RestoreBatch(execution.BatchId).IsComplete);
        Assert.True(observed, "逐项还原不能先于补记凭据初始化");
    }

    [Fact]
    public void 旧版还原待补记凭据仍可安全回放()
    {
        var history = new HistoryManager(Path.Combine(_root, "history.jsonl"));
        var quarantine = Path.Combine(_root, "q");
        var manager = new QuarantineManager(quarantine, history);
        var batchId = "legacy-batch";
        Directory.CreateDirectory(Path.Combine(quarantine, batchId));
        var id = Guid.NewGuid().ToString("N");
        var receipts = Path.Combine(quarantine, ".pending-audit");
        Directory.CreateDirectory(receipts);
        File.WriteAllText(Path.Combine(receipts, id + ".json"),
            $$"""{"version":1,"id":"{{id}}","batchId":"{{batchId}}","restoredCount":1,"result":"partial"}""");

        manager.RecoverPendingAudit();

        var summary = Assert.Single(history.Recent());
        Assert.Equal("restore", summary.Action);
        Assert.Equal(1, summary.FileCount);
        Assert.Equal("partial", summary.Result);
        Assert.False(File.Exists(Path.Combine(receipts, id + ".json")));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
