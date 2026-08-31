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
        var manager = new QuarantineManager(quarantineRoot);

        var candidates = new[]
        {
            new FileCandidate(f1, 5, DateTime.UtcNow.AddDays(-30)),
            new FileCandidate(f2, 5, DateTime.UtcNow.AddDays(-30)),
        };
        var report = manager.Execute(candidates.Select(c => ("test-rule", c)));

        Assert.Equal(2, report.MovedCount);
        Assert.Equal(10, report.MovedBytes);
        Assert.Empty(report.Skipped);
        Assert.False(File.Exists(f1));
        Assert.False(File.Exists(f2));

        var batches = manager.ListBatches();
        var batch = Assert.Single(batches);
        Assert.Equal(2, batch.Entries.Count);

        var restored = manager.RestoreBatch(batch.BatchId);
        Assert.Equal(2, restored);
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

        var manager = new QuarantineManager(Path.Combine(_root, "quarantine2"));

        using var handle = File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.None);
        var report = manager.Execute(new[]
        {
            ("rule", new FileCandidate(locked, 1, DateTime.UtcNow)),
            ("rule", new FileCandidate(normal, 1, DateTime.UtcNow)),
        });

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
        var manager = new QuarantineManager(Path.Combine(_root, "quarantine3"));
        var report = manager.Execute(new[] { ("r", new FileCandidate(f, 8, DateTime.UtcNow)) });
        File.WriteAllText(f, "new-content"); // 原位置出现新文件

        manager.RestoreBatch(report.BatchId);

        Assert.Equal("new-content", File.ReadAllText(f));
        Assert.True(File.Exists(f + ".restore-" + report.BatchId));
    }

    [Fact]
    public void 隔离区_手动清空仅清过期批次()
    {
        var manager = new QuarantineManager(Path.Combine(_root, "quarantine4"));
        var dir = Path.Combine(_root, "purge-src");
        Directory.CreateDirectory(dir);
        var f = Path.Combine(dir, "p.txt");
        File.WriteAllText(f, "x");
        var report = manager.Execute(new[] { ("r", new FileCandidate(f, 1, DateTime.UtcNow)) });

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
    public void 更新校验_SHA512通过与否均正确判定()
    {
        var payload = "hello kleaner"u8.ToArray();
        var good = Convert.ToHexString(System.Security.Cryptography.SHA512.HashData(payload));
        Assert.True(RuleUpdateService.VerifySha512(payload, good));
        Assert.True(RuleUpdateService.VerifySha512(payload, good.ToLowerInvariant()));
        Assert.False(RuleUpdateService.VerifySha512(payload, "ABCD"));
    }

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
