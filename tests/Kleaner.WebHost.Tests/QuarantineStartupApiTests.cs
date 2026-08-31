using System.Net;
using System.Text.Json;
using Kleaner.Core;
using Kleaner.Executor;
using Kleaner.WebHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace Kleaner.WebHost.Tests;

/// <summary>
/// 隔离区 / 历史 / 启动项 API 集成测试（工单 13 验收）。
/// 隔离区走真实 QuarantineManager（根目录指向临时目录，种子批次用真实 Execute 产出）；
/// 启动项注入 IStartupEnvironment fake，绝不触碰真实注册表；历史指向临时 jsonl。
/// </summary>
public sealed class QuarantineStartupApiTests : IDisposable
{
    private const int Port = 45172;
    private const string Token = "quarantine-startup-test-token";

    private static readonly JsonSerializerOptions ManifestOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly List<WebApplication> _apps = new();
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(), "kleaner-webhost-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        foreach (var app in _apps)
        {
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // 临时目录清理失败不影响测试结论
        }
    }

    private string SrcDir => Path.Combine(_tempRoot, "src");

    private string QuarantineRoot => Path.Combine(_tempRoot, "quarantine");

    private string BackupDir => Path.Combine(_tempRoot, "startup-backup");

    private string StartupUserDir => Path.Combine(_tempRoot, "startup-user");

    private string HistoryPath => Path.Combine(_tempRoot, "history.jsonl");

    /// <summary>启动项环境 fake：Run 值在内存字典里；HKLM 写删受 AllowElevated 开关控制（模拟提权失败路径）。</summary>
    private sealed class FakeStartupEnvironment : IStartupEnvironment
    {
        private readonly string[] _startupDirs;
        private readonly Dictionary<string, Dictionary<string, string>> _values = new();

        public FakeStartupEnvironment(string[] startupDirs) => _startupDirs = startupDirs;

        /// <summary>默认 true（种子数据照常写 HKLM）；提权失败用例显式关掉。</summary>
        public bool AllowElevated { get; set; } = true;

        public IReadOnlyList<RegistryRunSource> RunSources { get; } = new[]
        {
            new RegistryRunSource(StartupHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU"),
            new RegistryRunSource(StartupHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM"),
        };

        public IReadOnlyList<string> StartupDirectories => _startupDirs;

        public IReadOnlyList<(string Name, string? Data)> EnumerateRunValues(StartupHive hive, string keyPath) =>
            _values.TryGetValue(HiveKey(hive, keyPath), out var values)
                ? values.Select(kv => (kv.Key, (string?)kv.Value)).ToList()
                : Array.Empty<(string, string?)>();

        public void DeleteRunValue(StartupHive hive, string keyPath, string valueName)
        {
            if (hive == StartupHive.LocalMachine && !AllowElevated)
            {
                throw new InvalidOperationException("HKLM 需提权（测试模拟）");
            }

            if (!_values.TryGetValue(HiveKey(hive, keyPath), out var values) || !values.Remove(valueName))
            {
                throw new InvalidOperationException("注册表值不存在");
            }
        }

        public bool RunValueExists(StartupHive hive, string keyPath, string valueName) =>
            _values.TryGetValue(HiveKey(hive, keyPath), out var values) && values.ContainsKey(valueName);

        public void SetRunValue(StartupHive hive, string keyPath, string valueName, string data)
        {
            if (hive == StartupHive.LocalMachine && !AllowElevated)
            {
                throw new InvalidOperationException("HKLM 需提权（测试模拟）");
            }

            if (!_values.TryGetValue(HiveKey(hive, keyPath), out var values))
            {
                values = _values[HiveKey(hive, keyPath)] = new Dictionary<string, string>();
            }

            values[valueName] = data;
        }

        private static string HiveKey(StartupHive hive, string keyPath) => $"{hive}|{keyPath}";
    }

    private (HttpClient Client, QuarantineManager Quarantine, HistoryManager History, FakeStartupEnvironment Env)
        BuildHost()
    {
        Directory.CreateDirectory(SrcDir);
        Directory.CreateDirectory(QuarantineRoot);
        Directory.CreateDirectory(StartupUserDir);
        var history = new HistoryManager(HistoryPath);
        var quarantine = new QuarantineManager(QuarantineRoot, history);
        var env = new FakeStartupEnvironment(new[] { StartupUserDir });

        var app = WebHostAppFactory.Build(new KleanerWebHostOptions
        {
            Port = Port,
            Token = Token,
            UseTestServer = true,
            EnableIdleExit = false,
            QuarantineProvider = () => quarantine,
            HistoryProvider = () => history,
            StartupProvider = () => new StartupManager(BackupDir, history, env),
        });
        app.Start();
        _apps.Add(app);

        var client = app.GetTestClient();
        client.BaseAddress = new Uri($"http://127.0.0.1:{Port}");
        client.DefaultRequestHeaders.Add("X-Kleaner-Token", Token);
        client.DefaultRequestHeaders.Add("Origin", $"http://127.0.0.1:{Port}");
        return (client, quarantine, history, env);
    }

    /// <summary>用真实 Execute 在临时目录里产出一个批次（移走 src\a.log），返回 batchId。</summary>
    private string SeedBatch(QuarantineManager quarantine)
    {
        var source = Path.Combine(SrcDir, "a.log");
        File.WriteAllText(source, "content");
        var report = quarantine.Execute(new[]
        {
            ("rule-a", new FileCandidate(source, 7, DateTime.UtcNow)),
        });
        return report.BatchId;
    }

    /// <summary>手工写一个 8 天前的旧批次（含被隔离文件），供 purge / 列表使用。</summary>
    private string SeedOldBatch()
    {
        const string batchId = "20260101-000000";
        var batchDir = Path.Combine(QuarantineRoot, batchId);
        var quarantined = Path.Combine(batchDir, "D", "old", "a.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(quarantined)!);
        File.WriteAllText(quarantined, "old");
        var batch = new QuarantineBatch(
            batchId,
            DateTime.UtcNow.AddDays(-8),
            new[] { new QuarantineEntry(@"D:\old\a.txt", quarantined, 3, "rule-a") });
        File.WriteAllText(Path.Combine(batchDir, "manifest.json"), JsonSerializer.Serialize(batch, ManifestOpts));
        return batchId;
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> PostJsonAsync(
        HttpClient client, string url, object? body = null)
    {
        var response = await client.PostAsync(
            url,
            new StringContent(
                JsonSerializer.Serialize(body ?? new { }), System.Text.Encoding.UTF8, "application/json"));
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response.StatusCode, doc.RootElement.Clone());
    }

    // ── 隔离区 ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Batches_List_ReturnsViewWithCounts_AndSkipsBrokenDirs()
    {
        var (client, quarantine, _, _) = BuildHost();
        var batchId = SeedBatch(quarantine);
        var oldBatchId = SeedOldBatch();
        Directory.CreateDirectory(Path.Combine(QuarantineRoot, "empty-dir")); // 无 manifest
        Directory.CreateDirectory(Path.Combine(QuarantineRoot, "broken-dir"));
        await File.WriteAllTextAsync(Path.Combine(QuarantineRoot, "broken-dir", "manifest.json"), "not-json");

        var response = await client.GetAsync("/api/quarantine/batches");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var batches = doc.RootElement;

        // 缺失 / 损坏 manifest 的目录被静默跳过（ListBatches 与 GUI 同语义），只返回两个有效批次
        Assert.Equal(2, batches.GetArrayLength());
        var fresh = batches.EnumerateArray().First(b => b.GetProperty("batchId").GetString() == batchId);
        var old = batches.EnumerateArray().First(b => b.GetProperty("batchId").GetString() == oldBatchId);
        Assert.True(fresh.GetProperty("createdUtc").GetDateTime() > old.GetProperty("createdUtc").GetDateTime());
        Assert.Equal(1, fresh.GetProperty("entryCount").GetInt32());
        Assert.Equal(7, fresh.GetProperty("totalBytes").GetInt64());
        Assert.Equal("a.log", Path.GetFileName(fresh.GetProperty("entries")[0].GetProperty("originalPath").GetString()));
    }

    [Fact]
    public async Task Restore_NoConflict_MovesFilesBack_AndRemovesBatch_AndLogsHistory()
    {
        var (client, quarantine, history, _) = BuildHost();
        var batchId = SeedBatch(quarantine);
        var source = Path.Combine(SrcDir, "a.log");
        Assert.False(File.Exists(source)); // 已被隔离移走

        var (status, body) = await PostJsonAsync(client, $"/api/quarantine/batches/{batchId}/restore");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, body.GetProperty("restored").GetInt32());
        Assert.True(File.Exists(source)); // 原路径回归
        Assert.False(Directory.Exists(Path.Combine(QuarantineRoot, batchId))); // 批次目录已删
        var entries = await Task.Run(() => history.Recent(1000));
        Assert.Contains(entries, e => e.Action == "restore" && e.Detail.Contains(batchId));
    }

    [Fact]
    public async Task Restore_NameConflict_NeverOverwrites_UsesRestoreSuffix()
    {
        var (client, quarantine, _, _) = BuildHost();
        var batchId = SeedBatch(quarantine);
        var source = Path.Combine(SrcDir, "a.log");
        File.WriteAllText(source, "new-content"); // 原路径出现同名文件

        var (status, body) = await PostJsonAsync(client, $"/api/quarantine/batches/{batchId}/restore");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, body.GetProperty("restored").GetInt32());
        Assert.Equal("new-content", File.ReadAllText(source)); // 现有文件未被覆盖
        Assert.Equal("content", File.ReadAllText($"{source}.restore-{batchId}"));
    }

    [Fact]
    public async Task Restore_MissingOrCorruptManifest_ReturnsExplicitError_NotException()
    {
        var (client, _, _, _) = BuildHost();
        const string corruptBatchId = "20260101-000001";
        var corruptDir = Path.Combine(QuarantineRoot, corruptBatchId);
        Directory.CreateDirectory(corruptDir);
        await File.WriteAllTextAsync(Path.Combine(corruptDir, "manifest.json"), "not-json");

        // manifest 缺失 → 404
        var (missingStatus, missingBody) = await PostJsonAsync(client, "/api/quarantine/batches/unknown-batch/restore");
        Assert.Equal(HttpStatusCode.NotFound, missingStatus);
        Assert.NotNull(missingBody.GetProperty("error").GetString());

        // manifest 损坏 → 明确错误而非异常外溢（deletion-path.md 记录的 RestoreBatch 无 try-catch 坑由 WebHost 层兜住）
        var (corruptStatus, corruptBody) = await PostJsonAsync(client, $"/api/quarantine/batches/{corruptBatchId}/restore");
        Assert.Equal(HttpStatusCode.InternalServerError, corruptStatus);
        Assert.Contains("manifest", corruptBody.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Restore_BatchIdEscapingRoot_Returns400()
    {
        var (client, _, _, _) = BuildHost();

        var (status, _) = await PostJsonAsync(client, "/api/quarantine/batches/..%5C..%5CWindows/restore");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task DeleteBatch_RemovesDir_LogsDeleteBatch_UnknownReturns404()
    {
        var (client, quarantine, history, _) = BuildHost();
        var batchId = SeedBatch(quarantine);

        var response = await client.DeleteAsync($"/api/quarantine/batches/{batchId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(QuarantineRoot, batchId)));
        var entries = await Task.Run(() => history.Recent(1000));
        Assert.Contains(entries, e => e.Action == "delete-batch" && e.Detail.Contains(batchId));

        // 未知批次：还原 / 删除均 404
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/quarantine/batches/{batchId}")).StatusCode);
        var (restoreStatus, _) = await PostJsonAsync(client, $"/api/quarantine/batches/{batchId}/restore");
        Assert.Equal(HttpStatusCode.NotFound, restoreStatus);
    }

    [Fact]
    public async Task Purge_RemovesOnlyOlderThanSevenDays_AndLogsPurge()
    {
        var (client, quarantine, history, _) = BuildHost();
        var freshBatchId = SeedBatch(quarantine);
        var oldBatchId = SeedOldBatch();

        var (status, body) = await PostJsonAsync(client, "/api/quarantine/purge");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, body.GetProperty("purged").GetInt32());
        Assert.False(Directory.Exists(Path.Combine(QuarantineRoot, oldBatchId)));
        Assert.True(Directory.Exists(Path.Combine(QuarantineRoot, freshBatchId))); // 7 天内批次不动
        var entries = await Task.Run(() => history.Recent(1000));
        Assert.Contains(entries, e => e.Action == "purge");
    }

    // ── 历史 ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task History_ReadOnly_RespectsLimit()
    {
        var (client, _, history, _) = BuildHost();
        history.Append("clean", "一", 1, 1, "ok");
        history.Append("restore", "二", 1, 0, "ok");
        history.Append("delete-batch", "三", 0, 0, "ok");

        using var all = JsonDocument.Parse(await client.GetStringAsync("/api/history"));
        Assert.Equal(3, all.RootElement.GetArrayLength());
        Assert.All(
            all.RootElement.EnumerateArray(),
            e => Assert.NotNull(e.GetProperty("action").GetString()));

        using var limited = JsonDocument.Parse(await client.GetStringAsync("/api/history?limit=2"));
        Assert.Equal(2, limited.RootElement.GetArrayLength());
    }

    // ── 启动项 ───────────────────────────────────────────────────────────────────────

    private const string HkcuRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string HklmRunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    [Fact]
    public async Task Startup_List_ShowsEnabledWithStringEnums_AndDisabledEmpty()
    {
        var (client, _, _, env) = BuildHost();
        env.SetRunValue(StartupHive.CurrentUser, HkcuRunKey, "AppX", "cmd /c appx");
        env.SetRunValue(StartupHive.LocalMachine, HklmRunKey, "SvcY", "cmd /c svcy");
        await File.WriteAllTextAsync(Path.Combine(StartupUserDir, "tool.cmd"), "echo hi");

        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/startup"));
        var enabled = doc.RootElement.GetProperty("enabled");
        var disabled = doc.RootElement.GetProperty("disabled");

        Assert.Empty(disabled.EnumerateArray());
        Assert.Equal(3, enabled.GetArrayLength());

        var appX = enabled.EnumerateArray().First(i => i.GetProperty("id").GetString() == $"reg|HKCU|{HkcuRunKey}|AppX");
        Assert.Equal("registry", appX.GetProperty("kind").GetString());
        Assert.Equal("currentUser", appX.GetProperty("hive").GetString());
        Assert.False(appX.GetProperty("requiresElevation").GetBoolean());

        var svcY = enabled.EnumerateArray().First(i => i.GetProperty("id").GetString() == $"reg|HKLM|{HklmRunKey}|SvcY");
        Assert.Equal("localMachine", svcY.GetProperty("hive").GetString());
        Assert.True(svcY.GetProperty("requiresElevation").GetBoolean());

        var tool = enabled.EnumerateArray().First(i => i.GetProperty("kind").GetString() == "file");
        Assert.Equal("tool", tool.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Startup_Disable_Registry_RemovesValue_LogsStartupDisable()
    {
        var (client, _, history, env) = BuildHost();
        env.SetRunValue(StartupHive.CurrentUser, HkcuRunKey, "AppX", "cmd /c appx");
        var id = $"reg|HKCU|{HkcuRunKey}|AppX";

        var (status, body) = await PostJsonAsync(client, "/api/startup/disable", new { id });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("disabled").GetBoolean());
        Assert.False(env.RunValueExists(StartupHive.CurrentUser, HkcuRunKey, "AppX"));

        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/startup"));
        Assert.DoesNotContain(
            doc.RootElement.GetProperty("enabled").EnumerateArray(),
            i => i.GetProperty("id").GetString() == id);
        var disabledId = doc.RootElement.GetProperty("disabled").EnumerateArray()
            .Single().GetProperty("id").GetString();
        Assert.Equal(id, disabledId);

        var entries = await Task.Run(() => history.Recent(1000));
        Assert.Contains(entries, e => e.Action == "startup-disable" && e.Detail == id);
    }

    [Fact]
    public async Task Startup_Disable_File_MovesIntoBackupDir()
    {
        var (client, _, _, _) = BuildHost();
        var file = Path.Combine(StartupUserDir, "tool.cmd");
        await File.WriteAllTextAsync(file, "echo hi");
        var id = $"file|{file}";

        var (status, _) = await PostJsonAsync(client, "/api/startup/disable", new { id });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(File.Exists(file));
        Assert.True(Directory.Exists(BackupDir));
        Assert.Contains(Directory.GetFiles(BackupDir), f => Path.GetFileName(f) == "tool.cmd");

        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/startup"));
        var disabled = doc.RootElement.GetProperty("disabled").EnumerateArray().Single();
        Assert.Equal(id, disabled.GetProperty("id").GetString());
        Assert.NotNull(disabled.GetProperty("backupFile").GetString());
    }

    [Fact]
    public async Task Startup_Disable_Hklm_WithoutElevation_Fails409_AndRollsBackBackup()
    {
        var (client, _, _, env) = BuildHost();
        env.SetRunValue(StartupHive.LocalMachine, HklmRunKey, "SvcY", "cmd /c svcy");
        env.AllowElevated = false; // 模拟 UAC 取消 / 提权失败
        var id = $"reg|HKLM|{HklmRunKey}|SvcY";

        var (status, body) = await PostJsonAsync(client, "/api/startup/disable", new { id });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.NotNull(body.GetProperty("error").GetString());
        Assert.True(env.RunValueExists(StartupHive.LocalMachine, HklmRunKey, "SvcY")); // 原值仍在

        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/startup"));
        Assert.Empty(doc.RootElement.GetProperty("disabled").EnumerateArray()); // 备份记录已回滚

        // 提权可用后同一 id 可再禁用（回滚语义不变，无脏状态）
        env.AllowElevated = true;
        var (retryStatus, _) = await PostJsonAsync(client, "/api/startup/disable", new { id });
        Assert.Equal(HttpStatusCode.OK, retryStatus);
    }

    [Fact]
    public async Task Startup_Disable_UnknownId_Returns404()
    {
        var (client, _, _, env) = BuildHost();
        env.SetRunValue(StartupHive.CurrentUser, HkcuRunKey, "AppX", "cmd /c appx");

        var (status, _) = await PostJsonAsync(client, "/api/startup/disable", new { id = "reg|HKCU|nope|Nope" });

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task Startup_Restore_Registry_RestoresValue_LogsStartupRestore()
    {
        var (client, _, history, env) = BuildHost();
        env.SetRunValue(StartupHive.CurrentUser, HkcuRunKey, "AppX", "cmd /c appx");
        var id = $"reg|HKCU|{HkcuRunKey}|AppX";
        Assert.Equal(HttpStatusCode.OK, (await PostJsonAsync(client, "/api/startup/disable", new { id })).Status);

        var (status, body) = await PostJsonAsync(client, "/api/startup/restore", new { id });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("restored").GetBoolean());
        Assert.True(env.RunValueExists(StartupHive.CurrentUser, HkcuRunKey, "AppX"));
        Assert.Equal("cmd /c appx", env.EnumerateRunValues(StartupHive.CurrentUser, HkcuRunKey).Single(v => v.Name == "AppX").Data);

        var entries = await Task.Run(() => history.Recent(1000));
        Assert.Contains(entries, e => e.Action == "startup-restore" && e.Detail == id);
    }

    [Fact]
    public async Task Startup_Restore_UnknownOrMissingBackup_Returns404()
    {
        var (client, _, _, env) = BuildHost();
        env.SetRunValue(StartupHive.CurrentUser, HkcuRunKey, "AppX", "cmd /c appx");

        // 启用中的项没有备份记录 → 404
        var (status, _) = await PostJsonAsync(
            client, "/api/startup/restore", new { id = $"reg|HKCU|{HkcuRunKey}|AppX" });
        Assert.Equal(HttpStatusCode.NotFound, status);

        // 完全未知的 id → 404
        var (unknownStatus, _) = await PostJsonAsync(client, "/api/startup/restore", new { id = "whatever" });
        Assert.Equal(HttpStatusCode.NotFound, unknownStatus);
    }
}
