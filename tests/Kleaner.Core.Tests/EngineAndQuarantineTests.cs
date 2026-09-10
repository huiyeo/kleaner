using System.Text;
using Kleaner.Core;
using Kleaner.Executor;
using Kleaner.SpecialOps;
using Xunit;

namespace Kleaner.Core.Tests;

public sealed class EngineAndQuarantineTests : IDisposable
{
    private readonly string _root;

    public EngineAndQuarantineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kleaner-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch { }
    }

    private static string JsonEscape(string s) => s.Replace("\\", "\\\\");

    private static CleanupPlan CreatePlan(string quarantineRoot, string ruleId, params string[] paths)
    {
        var sourceDir = Path.GetDirectoryName(paths[0])!;
        var rule = new Rule(
            ruleId, "测试授权规则", RuleCategory.Application, RiskLevel.Low,
            new[] { Path.Combine(sourceDir, "**") }, Array.Empty<string>(),
            AgeDays: 0, KeepNewest: null, RequiresElevation: false, Enabled: true,
            SafetyNotes: "仅用于隔离区测试的临时目录授权规则，绝不触及真实用户文件。");
        var set = new RuleSet(1, null, 0, new[] { rule });
        var scan = new ScanEngine(quarantineRoot).Scan(set);
        return CleanupPlanBuilder.Create(set, scan, new[] { ruleId }, quarantineRoot);
    }

    /// <summary>同步收集 ScanProgress 上报的 IProgress 实现，避免 Progress&lt;T&gt; 的线程语义带来的竞态。</summary>
    private sealed class CollectingProgress : IProgress<ScanProgress>
    {
        public List<ScanProgress> Reports { get; } = new();
        public void Report(ScanProgress value) => Reports.Add(value);
    }

    [Fact]
    public void 引擎端到端_扫描与排除()
    {
        var dir = Path.Combine(_root, "cache");
        Directory.CreateDirectory(dir);
        var old = Path.Combine(dir, "old.bin");
        var fresh = Path.Combine(dir, "fresh.bin");
        var excluded = Path.Combine(dir, "skip.log");
        File.WriteAllText(old, new string('a', 100));
        File.WriteAllText(fresh, "b");
        File.WriteAllText(excluded, "c");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-30));
        File.SetLastWriteTimeUtc(fresh, DateTime.UtcNow);
        File.SetLastWriteTimeUtc(excluded, DateTime.UtcNow.AddDays(-30));

        var json = $$"""
        {
          "schemaVersion": 1,
          "rules": [{
            "id": "test-cache",
            "name": "测试缓存",
            "category": "application",
            "risk": "low",
            "paths": ["{{JsonEscape(dir)}}\\**"],
            "exclude": ["{{JsonEscape(dir)}}\\*.log"],
            "ageDays": 7,
            "requiresElevation": false,
            "safetyNotes": "测试目录的临时缓存文件，仅用于单元测试验证，不影响真实环境。"
          }]
        }
        """;
        var set = RuleSetLoader.LoadFromJson(json);
        Assert.Empty(RuleSetLoader.Validate(set));

        var report = new ScanEngine().Scan(set);

        Assert.Empty(report.Errors);
        var result = Assert.Single(report.Results);
        Assert.Equal(1, result.FileCount);
        Assert.Equal(100, result.TotalBytes);
        Assert.Equal(old, result.Files[0].FullPath, ignoreCase: true);
    }

    [Fact]
    public void 引擎_已取消的令牌立即抛取消异常()
    {
        var dir = Path.Combine(_root, "cancel");
        Directory.CreateDirectory(dir);
        var f = Path.Combine(dir, "c.bin");
        File.WriteAllText(f, "x");
        File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddDays(-30));

        var json = $$"""
        {
          "schemaVersion": 1,
          "rules": [{
            "id": "test-cancel",
            "name": "测试取消",
            "category": "application",
            "risk": "low",
            "paths": ["{{JsonEscape(dir)}}\\**"],
            "ageDays": 7,
            "requiresElevation": false,
            "safetyNotes": "仅用于单元测试验证取消语义，不影响真实环境与规则库文件。"
          }]
        }
        """;
        var set = RuleSetLoader.LoadFromJson(json);
        Assert.Empty(RuleSetLoader.Validate(set));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => new ScanEngine().Scan(set, cts.Token));
    }

    [Fact]
    public void 引擎_默认令牌下扫描结果与之前一致()
    {
        var dir = Path.Combine(_root, "token-ok");
        Directory.CreateDirectory(dir);
        var f = Path.Combine(dir, "t.bin");
        File.WriteAllText(f, "content");
        File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddDays(-30));

        var json = $$"""
        {
          "schemaVersion": 1,
          "rules": [{
            "id": "test-token",
            "name": "测试默认令牌",
            "category": "application",
            "risk": "low",
            "paths": ["{{JsonEscape(dir)}}\\**"],
            "ageDays": 7,
            "requiresElevation": false,
            "safetyNotes": "仅用于单元测试验证默认令牌路径不受影响。"
          }]
        }
        """;
        var set = RuleSetLoader.LoadFromJson(json);
        var report = new ScanEngine().Scan(set, CancellationToken.None);

        Assert.Empty(report.Errors);
        Assert.Equal(1, Assert.Single(report.Results).FileCount);
    }

    [Fact]
    public void 引擎_隔离区内文件不计入候选且按路径段边界排除()
    {
        var dir = Path.Combine(_root, "app-cache");
        Directory.CreateDirectory(dir);
        var inside = Path.Combine(dir, "inside.bin");
        File.WriteAllText(inside, "x");
        File.SetLastWriteTimeUtc(inside, DateTime.UtcNow.AddDays(-30));

        var quarantineRoot = Path.Combine(dir, "KleanerQuarantine");
        Directory.CreateDirectory(quarantineRoot);
        var quarantined = Path.Combine(quarantineRoot, "old.bin");
        File.WriteAllText(quarantined, "y");
        File.SetLastWriteTimeUtc(quarantined, DateTime.UtcNow.AddDays(-30));

        // 前缀相同但不属于隔离区本体的目录不应被误伤（D:\Q 不排除 D:\Q2）
        var sibling = Path.Combine(dir, "KleanerQuarantine2");
        Directory.CreateDirectory(sibling);
        var kept = Path.Combine(sibling, "keep.bin");
        File.WriteAllText(kept, "z");
        File.SetLastWriteTimeUtc(kept, DateTime.UtcNow.AddDays(-30));

        var json = $$"""
        {
          "schemaVersion": 1,
          "rules": [{
            "id": "test-q",
            "name": "测试隔离区排除",
            "category": "application",
            "risk": "low",
            "paths": ["{{JsonEscape(dir)}}\\**"],
            "ageDays": 7,
            "requiresElevation": false,
            "safetyNotes": "仅用于单元测试验证隔离区排除逻辑与路径段边界匹配。"
          }]
        }
        """;
        var set = RuleSetLoader.LoadFromJson(json);
        Assert.Empty(RuleSetLoader.Validate(set));

        var report = new ScanEngine(quarantineRoot).Scan(set);

        Assert.True(report.Errors.Count == 0, "规则扫描错误：" + string.Join("；", report.Errors));
        var files = Assert.Single(report.Results).Files;
        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => string.Equals(f.FullPath, inside, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, f => string.Equals(f.FullPath, kept, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(files, f => string.Equals(f.FullPath, quarantined, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void 隔离区_执行后可整批还原()
    {
        var sourceDir = Path.Combine(_root, "src");
        Directory.CreateDirectory(sourceDir);
        var f1 = Path.Combine(sourceDir, "a.txt");
        var f2 = Path.Combine(sourceDir, "b.txt");
        File.WriteAllText(f1, "hello");
        File.WriteAllText(f2, "world");

        var quarantineRoot = Path.Combine(_root, "quarantine");
        var manager = CreateManager(quarantineRoot, "restore");

        var report = manager.Execute(CreatePlan(quarantineRoot, "test-rule", f1, f2));

        Assert.Equal(2, report.MovedCount);
        Assert.Equal(10, report.MovedBytes);
        Assert.Empty(report.Skipped);
        Assert.False(File.Exists(f1));
        Assert.False(File.Exists(f2));

        var batches = manager.ListBatches();
        var batch = Assert.Single(batches);
        Assert.Equal(2, batch.Entries.Count);

        var restored = manager.RestoreBatch(batch.BatchId);
        Assert.Equal(2, restored.RestoredCount);
        Assert.True(File.Exists(f1));
        Assert.True(File.Exists(f2));
        Assert.Equal("hello", File.ReadAllText(f1));
        Assert.Empty(manager.ListBatches());
    }

    [Fact]
    public void 隔离区_被占用文件跳过不报错()
    {
        var sourceDir = Path.Combine(_root, "locked");
        Directory.CreateDirectory(sourceDir);
        var locked = Path.Combine(sourceDir, "busy.txt");
        var normal = Path.Combine(sourceDir, "free.txt");
        File.WriteAllText(locked, "x");
        File.WriteAllText(normal, "y");

        var manager = CreateManager(Path.Combine(_root, "quarantine2"), "locked");

        using var handle = File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.None);
        var report = manager.Execute(CreatePlan(manager.Root, "rule", locked, normal));

        Assert.Equal(1, report.MovedCount);
        Assert.Single(report.Skipped);
        Assert.True(File.Exists(locked));
        Assert.False(File.Exists(normal));
    }

    [Fact]
    public void 隔离区_还原遇冲突不覆盖()
    {
        var sourceDir = Path.Combine(_root, "conflict");
        Directory.CreateDirectory(sourceDir);
        var f = Path.Combine(sourceDir, "c.txt");
        File.WriteAllText(f, "original");
        var manager = CreateManager(Path.Combine(_root, "quarantine3"), "conflict");
        var report = manager.Execute(CreatePlan(manager.Root, "r", f));
        File.WriteAllText(f, "new-content"); // 原位置出现新文件

        manager.RestoreBatch(report.BatchId);

        Assert.Equal("new-content", File.ReadAllText(f));
        Assert.True(File.Exists(f + ".restore-" + report.BatchId));
    }

    [Fact]
    public void 隔离区_手动清空仅清过期批次()
    {
        var manager = CreateManager(Path.Combine(_root, "quarantine4"), "purge");
        var dir = Path.Combine(_root, "purge-src");
        Directory.CreateDirectory(dir);
        var f = Path.Combine(dir, "p.txt");
        File.WriteAllText(f, "x");
        var report = manager.Execute(CreatePlan(manager.Root, "r", f));

        // 手工构造一个 8 天前的过期批次
        var oldBatchDir = Path.Combine(manager.Root, "20260101-000000");
        Directory.CreateDirectory(oldBatchDir);
        File.WriteAllText(Path.Combine(oldBatchDir, "manifest.json"),
            """{"batchId":"20260101-000000","createdUtc":"2026-08-01T00:00:00Z","entries":[]}""");

        var purged = manager.PurgeOlderThan(TimeSpan.FromDays(7));

        Assert.Equal(1, purged);
        Assert.False(Directory.Exists(oldBatchDir));
        Assert.Single(manager.ListBatches()); // 今天的新批次保留
        _ = report;
    }

    [Fact]
    public void 隔离区_还原部分失败时保留未恢复文件与批次()
    {
        var source = Path.Combine(_root, "partial-restore");
        Directory.CreateDirectory(source);
        var blocked = Path.Combine(source, "blocked.txt");
        var normal = Path.Combine(source, "normal.txt");
        File.WriteAllText(blocked, "blocked");
        File.WriteAllText(normal, "normal");
        var manager = CreateManager(Path.Combine(_root, "partial-quarantine"), "partial-restore");
        var execution = manager.Execute(CreatePlan(manager.Root, "partial", blocked, normal));

        Directory.CreateDirectory(blocked); // 同名目录使恢复目标不可写入文件。
        var restore = manager.RestoreBatch(execution.BatchId);

        Assert.Equal(1, restore.RestoredCount);
        Assert.NotEmpty(restore.Failed);
        Assert.True(File.Exists(normal));
        var batch = Assert.Single(manager.ListBatches());
        Assert.Contains(batch.Entries, entry => entry.OriginalPath == blocked && File.Exists(entry.QuarantinedPath));
    }

    [Fact]
    public void 隔离区_批次编号唯一且manifest只保留最终状态()
    {
        var source = Path.Combine(_root, "unique-batches");
        Directory.CreateDirectory(source);
        var first = Path.Combine(source, "first.txt");
        var second = Path.Combine(source, "second.txt");
        File.WriteAllText(first, "one");
        File.WriteAllText(second, "two");
        var manager = CreateManager(Path.Combine(_root, "unique-quarantine"), "unique");

        var firstExecution = manager.Execute(CreatePlan(manager.Root, "unique", first));
        var secondExecution = manager.Execute(CreatePlan(manager.Root, "unique", second));

        Assert.NotEqual(firstExecution.BatchId, secondExecution.BatchId);
        Assert.All(manager.ListBatches().SelectMany(batch => batch.Entries), entry => Assert.Equal("moved", entry.State));
        Assert.Empty(Directory.GetFiles(manager.Root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void 隔离区_永久清空失败不会伪报成功()
    {
        var source = Path.Combine(_root, "delete-failure");
        Directory.CreateDirectory(source);
        var file = Path.Combine(source, "locked.txt");
        File.WriteAllText(file, "locked");
        var removable = Path.Combine(source, "removable.txt");
        File.WriteAllText(removable, "remove");
        var history = new HistoryManager(Path.Combine(_root, "delete-failure.history.jsonl"));
        var manager = new QuarantineManager(Path.Combine(_root, "delete-failure-quarantine"), history);
        var execution = manager.Execute(CreatePlan(manager.Root, "delete", file, removable));
        var entries = Assert.Single(manager.ListBatches()).Entries;
        var entry = Assert.Single(entries, item => item.OriginalPath == file);
        var removedEntry = Assert.Single(entries, item => item.OriginalPath == removable);

        using var handle = File.Open(entry.QuarantinedPath, FileMode.Open, FileAccess.Read, FileShare.None);
        var deletion = manager.DeleteBatch(execution.BatchId);

        Assert.False(deletion.Deleted);
        Assert.NotEmpty(deletion.Failed);
        Assert.False(File.Exists(removedEntry.QuarantinedPath));
        Assert.True(Directory.Exists(execution.QuarantineDir));
        Assert.Contains(history.Recent(), item => item.Action == "delete-batch" && item.Result == "partial");
        Assert.True(File.Exists(Path.Combine(execution.QuarantineDir, "manifest.json")));
        var reopened = new QuarantineManager(manager.Root, history);
        Assert.Equal(execution.BatchId, Assert.Single(reopened.ListBatches()).BatchId);
        handle.Dispose();
        var restore = reopened.RestoreBatch(execution.BatchId);
        Assert.Equal(1, restore.RestoredCount);
        Assert.Single(restore.Skipped);
        Assert.Equal("locked", File.ReadAllText(file));
    }

    [Fact]
    public void 审计文件不可被清理规则扫入隔离区()
    {
        // 规则模式覆盖整个测试目录，历史文件与其检查点就躺在其中——审计自身必须免于清理。
        var source = Path.Combine(_root, "sweep-target.txt");
        File.WriteAllText(source, "sweep");
        var historyPath = Path.Combine(_root, "swept.history.jsonl");
        var history = new HistoryManager(historyPath);
        var manager = new QuarantineManager(Path.Combine(_root, "sweep-quarantine"), history);
        history.Append("clean-start", "前置记录", 0, 0, "started");

        var report = manager.Execute(CreatePlan(manager.Root, "sweep", source));

        Assert.True(File.Exists(historyPath), "历史文件不得被移入隔离区");
        Assert.True(File.Exists(historyPath + ".head"), "链检查点不得被移入隔离区");
        Assert.Contains(report.Skipped, item => item.Contains("审计文件"));
        Assert.False(File.Exists(source), "非审计的目标文件应正常移入隔离区");
        Assert.Single(Assert.Single(manager.ListBatches()).Entries, entry => entry.OriginalPath == source);
        history.Append("clean", "历史仍可写", 1, 1, "ok");
        Assert.Contains(history.Recent(), entry => entry.Detail == "历史仍可写");
    }

    [Fact]
    public void 撤回的规则即使Enabled为真也不被扫描()
    {
        // 撤回（deprecated）是最深防线：发布者误设 enabled=true 也永不执行。
        var dir = Path.Combine(_root, "deprecated-scan");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "hit.txt"), new string('a', 100));
        File.SetLastWriteTimeUtc(Path.Combine(dir, "hit.txt"), DateTime.UtcNow.AddDays(-30));

        var json = $$"""
        {
          "schemaVersion": 1,
          "rules": [{
            "id": "retired-rule",
            "name": "已撤回规则",
            "category": "application",
            "risk": "low",
            "paths": ["{{JsonEscape(dir)}}\\\\**"],
            "ageDays": 0,
            "enabled": true,
            "deprecated": true,
            "deprecationReason": "2026-09 发现误伤，全网撤回",
            "requiresElevation": false,
            "safetyNotes": "已撤回的规则用于验证撤回防线的测试，仅命中临时夹具目录。"
          }]
        }
        """;
        var set = RuleSetLoader.LoadFromJson(json);
        Assert.True(set.Rules[0].Deprecated, "撤回标记必须加载");
        Assert.Equal("2026-09 发现误伤，全网撤回", set.Rules[0].DeprecationReason);

        var report = new ScanEngine().Scan(set);

        Assert.Empty(report.Results); // 撤回规则被扫描防线跳过，不产生任何结果
    }

    [Fact]
    public void 隔离区_正常清空移除批次且审计成功()
    {
        var source = Path.Combine(_root, "delete-success.txt");
        File.WriteAllText(source, "remove");
        var history = new HistoryManager(Path.Combine(_root, "delete-success.history.jsonl"));
        var manager = new QuarantineManager(Path.Combine(_root, "delete-success-quarantine"), history);
        var execution = manager.Execute(CreatePlan(manager.Root, "delete-success", source));

        var report = manager.DeleteBatch(execution.BatchId);

        Assert.True(report.Deleted);
        Assert.Empty(report.Failed);
        Assert.False(Directory.Exists(execution.QuarantineDir));
        Assert.Empty(manager.ListBatches());
        Assert.Contains(history.Recent(), item => item.Action == "delete-batch" && item.Result == "ok");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 隔离区_还原收尾保留未登记文件与清单并审计部分失败(bool nested)
    {
        var source = Path.Combine(_root, "untracked-source.txt");
        File.WriteAllText(source, "original");
        var history = new HistoryManager(Path.Combine(_root, "untracked.history.jsonl"));
        var manager = new QuarantineManager(Path.Combine(_root, "untracked-quarantine"), history);
        var execution = manager.Execute(CreatePlan(manager.Root, "untracked", source));
        var unexpectedDirectory = nested ? Path.Combine(execution.QuarantineDir, "extra") : execution.QuarantineDir;
        Directory.CreateDirectory(unexpectedDirectory);
        var unexpected = Path.Combine(unexpectedDirectory, "do-not-delete.txt");
        File.WriteAllText(unexpected, "untracked-data");

        var restored = manager.RestoreBatch(execution.BatchId);

        Assert.Equal("original", File.ReadAllText(source));
        Assert.True(File.Exists(unexpected));
        Assert.Equal("untracked-data", File.ReadAllText(unexpected));
        Assert.False(restored.IsComplete);
        Assert.NotEmpty(restored.Failed);
        Assert.Equal(execution.BatchId, Assert.Single(manager.ListBatches()).BatchId);
        Assert.Contains(history.Recent(), item => item.Action == "restore" && item.Result == "partial");
    }

    [Fact]
    public void 隔离区_审计不可写时拒绝初始化且不移动文件()
    {
        var source = Path.Combine(_root, "audit-failure");
        Directory.CreateDirectory(source);
        var file = Path.Combine(source, "original.txt");
        File.WriteAllText(file, "keep");
        var invalidHistoryPath = Path.Combine(_root, "history-directory");
        Directory.CreateDirectory(invalidHistoryPath);

        Assert.ThrowsAny<Exception>(() => new QuarantineManager(Path.Combine(_root, "audit-failure-quarantine"), new HistoryManager(invalidHistoryPath)));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void 更新校验_SHA512通过与否均正确判定()
    {
        var payload = "hello kleaner"u8.ToArray();
        var good = Convert.ToHexString(System.Security.Cryptography.SHA512.HashData(payload));
        Assert.True(RuleUpdateService.VerifySha512(payload, good));
        Assert.True(RuleUpdateService.VerifySha512(payload, good.ToLowerInvariant()));
        Assert.False(RuleUpdateService.VerifySha512(payload, "ABCD"));
    }

    private QuarantineManager CreateManager(string quarantineRoot, string name) =>
        new(quarantineRoot, new HistoryManager(Path.Combine(_root, $"{name}.history.jsonl")));

    [Fact]
    public void SpecialOps_检测不抛异常()
    {
        _ = WslInspector.DetectVhdx();
        _ = RegistryInspector.ScanBrokenUninstallEntries();
        Assert.True(SystemToolGuide.Items.Count >= 3);
    }

    [Fact]
    public void SpecialOps_卸载串提取exe路径()
    {
        Assert.Equal(@"C:\Program Files\X\unins.exe",
            RegistryInspector.ExtractExePath("\"C:\\Program Files\\X\\unins.exe\" /quiet"));
        Assert.Null(RegistryInspector.ExtractExePath("MsiExec.exe /X{GUID}"));
        Assert.Null(RegistryInspector.ExtractExePath("something without exe"));
    }

    [Fact]
    public void 引擎_进度按规则顺序与计数上报()
    {
        var dirA = Path.Combine(_root, "prog-a");
        var dirB = Path.Combine(_root, "prog-b");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);
        foreach (var name in new[] { "1.bin", "2.bin" })
            File.WriteAllText(Path.Combine(dirA, name), new string('a', 10));
        File.WriteAllText(Path.Combine(dirB, "1.bin"), new string('b', 30));
        File.SetLastWriteTimeUtc(Path.Combine(dirA, "1.bin"), DateTime.UtcNow.AddDays(-30));
        File.SetLastWriteTimeUtc(Path.Combine(dirA, "2.bin"), DateTime.UtcNow.AddDays(-30));
        File.SetLastWriteTimeUtc(Path.Combine(dirB, "1.bin"), DateTime.UtcNow.AddDays(-30));

        var json = $$"""
        {
          "schemaVersion": 1,
          "rules": [
            {
              "id": "rule-a",
              "name": "进度规则A",
              "category": "application",
              "risk": "low",
              "paths": ["{{JsonEscape(dirA)}}\\**"],
              "ageDays": 7,
              "requiresElevation": false,
              "safetyNotes": "仅用于单元测试验证进度上报顺序，不影响真实环境。"
            },
            {
              "id": "rule-b",
              "name": "进度规则B",
              "category": "application",
              "risk": "low",
              "paths": ["{{JsonEscape(dirB)}}\\**"],
              "ageDays": 7,
              "requiresElevation": false,
              "safetyNotes": "仅用于单元测试验证进度上报顺序，不影响真实环境。"
            }
          ]
        }
        """;
        var set = RuleSetLoader.LoadFromJson(json);

        var reports = new CollectingProgress();
        var report = new ScanEngine().Scan(set, progress: reports);

        Assert.Equal(new[] { "rule-a", "rule-b" }, reports.Reports.Select(p => p.RuleId).ToArray());
        Assert.Equal(2, reports.Reports[0].FileCount);
        Assert.Equal(20, reports.Reports[0].TotalBytes);
        Assert.Equal(1, reports.Reports[1].FileCount);
        Assert.Equal(30, reports.Reports[1].TotalBytes);
        Assert.Equal(2, Assert.Single(report.Results, r => r.RuleId == "rule-a").FileCount);
    }

    [Fact]
    public void 引擎_null进度时行为与现状一致()
    {
        var dir = Path.Combine(_root, "prog-null");
        Directory.CreateDirectory(dir);
        var f = Path.Combine(dir, "n.bin");
        File.WriteAllText(f, new string('x', 5));
        File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddDays(-30));

        var json = $$"""
        {
          "schemaVersion": 1,
          "rules": [{
            "id": "rule-null",
            "name": "空进度规则",
            "category": "application",
            "risk": "low",
            "paths": ["{{JsonEscape(dir)}}\\**"],
            "ageDays": 7,
            "requiresElevation": false,
            "safetyNotes": "仅用于单元测试验证 null 进度参数不改变行为。"
          }]
        }
        """;
        var set = RuleSetLoader.LoadFromJson(json);

        // 不传 progress（默认 null）：结果与既有契约一致
        var report = new ScanEngine().Scan(set);

        Assert.Empty(report.Errors);
        var result = Assert.Single(report.Results);
        Assert.Equal(1, result.FileCount);
        Assert.Equal(5, result.TotalBytes);
    }

    [Fact]
    public void 引擎_规则扫描失败时进度上报零计数()
    {
        var dir = Path.Combine(_root, "prog-fail");
        Directory.CreateDirectory(dir);

        // paths 指向不存在的盘符目录，GlobScanner 抛非权限类异常 → errors 记录且进度为 0
        var json = $$"""
        {
          "schemaVersion": 1,
          "rules": [{
            "id": "rule-fail",
            "name": "失败规则",
            "category": "application",
            "risk": "low",
            "paths": ["{{JsonEscape(_root)}}\\nonexistent-驱动器\\**"],
            "ageDays": 7,
            "requiresElevation": false,
            "safetyNotes": "仅用于单元测试验证失败规则也会上报进度，不影响真实环境。"
          }]
        }
        """;
        var set = RuleSetLoader.LoadFromJson(json);

        var reports = new CollectingProgress();
        var report = new ScanEngine().Scan(set, progress: reports);

        var entry = Assert.Single(reports.Reports);
        Assert.Equal("rule-fail", entry.RuleId);
        Assert.Equal(0, entry.FileCount);
        Assert.Equal(0, entry.TotalBytes);
    }
}
