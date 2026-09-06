using System.Diagnostics;
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

    [Fact]
    public void 移动窗口内来源祖先对改名免疫()
    {
        var quarantine = Path.Combine(_root, "q");
        var history = new HistoryManager(Path.Combine(_root, "history.jsonl"));
        var plan = MakePlan("first", quarantine);
        var sourceDir = Path.Combine(_root, "first");
        Exception? renameError = null;
        var invoked = false;
        // beforeLeafMove 在祖先锚定之后、移动之前触发，此处代表并行攻击者在检查与移动之间改名来源祖先。
        var owner = new QuarantineManager(quarantine, history, null, null, () =>
        {
            if (invoked) return;
            invoked = true;
            renameError = Record.Exception(() => Directory.Move(sourceDir, sourceDir + "-stolen"));
        });

        var report = owner.Execute(plan);

        Assert.True(invoked);
        Assert.IsAssignableFrom<IOException>(renameError);
        Assert.Equal(1, report.MovedCount);
        Assert.True(Directory.Exists(sourceDir), "锚定期间来源目录必须保持原名");
        Assert.False(File.Exists(Path.Combine(sourceDir, "a.txt")));
        Assert.True(File.Exists(Assert.Single(Assert.Single(owner.ListBatches()).Entries).QuarantinedPath));
        Assert.Single(history.Recent(), entry => entry.Action == "clean" && entry.Result == "ok");
    }

    [Fact]
    public void 还原窗口内还原目标祖先对改名免疫()
    {
        var quarantine = Path.Combine(_root, "q");
        var history = new HistoryManager(Path.Combine(_root, "history.jsonl"));
        var plan = MakePlan("first", quarantine);
        var batchId = new QuarantineManager(quarantine, history).Execute(plan).BatchId;
        var targetDir = Path.Combine(_root, "first");
        Exception? renameError = null;
        var invoked = false;
        var owner = new QuarantineManager(quarantine, history, null, null, () =>
        {
            if (invoked) return;
            invoked = true;
            renameError = Record.Exception(() => Directory.Move(targetDir, targetDir + "-stolen"));
        });

        var report = owner.RestoreBatch(batchId);

        Assert.True(invoked);
        Assert.IsAssignableFrom<IOException>(renameError);
        Assert.True(report.IsComplete);
        Assert.True(Directory.Exists(targetDir), "锚定期间还原目标目录必须保持原名");
        Assert.Equal("content", File.ReadAllText(Path.Combine(targetDir, "a.txt")));
        Assert.Empty(owner.ListBatches());
    }

    [Fact]
    public void 并行进程在移动窗口内改写来源祖先被拒绝()
    {
        var quarantine = Path.Combine(_root, "q");
        var history = new HistoryManager(Path.Combine(_root, "history.jsonl"));
        var plan = MakePlan("first", quarantine);
        var sourceDir = Path.Combine(_root, "first");
        var goMarker = Path.Combine(_root, "go.txt");
        var resultFile = Path.Combine(_root, "result.txt");
        var script = Path.Combine(_root, "adversary.cmd");
        File.WriteAllLines(script, new[]
        {
            "@echo off",
            ":wait",
            $"if exist \"{goMarker}\" goto go",
            "ping -n 1 127.0.0.1 >nul",
            "goto wait",
            ":go",
            $"ren \"{sourceDir}\" first-moved 2>nul",
            $"echo %errorlevel%>\"{resultFile}\"",
        });
        using var adversary = Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });
        var moved = false;
        var owner = new QuarantineManager(quarantine, history, null, null, () =>
        {
            if (moved) return;
            moved = true;
            // 钩子阻塞操作直至并行进程的改名尝试结束，保证尝试发生在锚定窗口之内。
            File.WriteAllText(goMarker, "go");
            for (var attempt = 0; attempt < 600 && !File.Exists(resultFile); attempt++)
                Thread.Sleep(50);
        });

        try
        {
            var report = owner.Execute(plan);

            Assert.True(moved);
            Assert.True(File.Exists(resultFile), "并行进程必须在移动窗口内完成改名尝试");
            Assert.NotEqual("0", File.ReadAllText(resultFile).Trim());
            Assert.Equal(1, report.MovedCount);
            Assert.True(Directory.Exists(sourceDir), "锚定期间来源目录必须保持原名");
            Assert.Single(history.Recent(), entry => entry.Action == "clean" && entry.Result == "ok");
        }
        finally
        {
            if (adversary is not null && !adversary.HasExited) adversary.Kill();
        }
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
