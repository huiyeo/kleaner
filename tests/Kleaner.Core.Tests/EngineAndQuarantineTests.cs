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
}
