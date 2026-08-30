using Kleaner.Executor;
using Xunit;

namespace Kleaner.Core.Tests;

public sealed class StartupManagerTests : IDisposable
{
    private readonly string _root;

    public StartupManagerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kleaner-startup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch { }
    }

    /// <summary>假启动环境：注册表与启动目录都在临时目录/内存里，测试不触碰真实注册表与真实启动文件夹。
    /// HKLM/提权路径（reg.exe runas）不纳入单元测试。</summary>
    private sealed class FakeStartupEnvironment : IStartupEnvironment
    {
        public List<RegistryRunSource> Sources { get; } = new();
        public List<string> Directories { get; } = new();
        public Dictionary<(string Hive, string KeyPath), Dictionary<string, string?>> RunValues { get; } = new();
        public HashSet<string> FailingDeletes { get; } = new();

        public IReadOnlyList<RegistryRunSource> RunSources => Sources;

        public IReadOnlyList<string> StartupDirectories => Directories;

        public IReadOnlyList<(string Name, string? Data)> EnumerateRunValues(StartupHive hive, string keyPath)
        {
            if (!RunValues.TryGetValue((hive.ToString(), keyPath), out var values))
                return Array.Empty<(string, string?)>();
            return values.Select(kv => (kv.Key, kv.Value)).ToList();
        }

        public void DeleteRunValue(StartupHive hive, string keyPath, string valueName)
        {
            var key = (hive.ToString(), keyPath);
            if (FailingDeletes.Contains($"{hive}|{keyPath}|{valueName}"))
                throw new InvalidOperationException("模拟注册表删除失败");
            if (RunValues.TryGetValue(key, out var values))
                values.Remove(valueName);
        }

        public bool RunValueExists(StartupHive hive, string keyPath, string valueName) =>
            RunValues.TryGetValue((hive.ToString(), keyPath), out var v) && v.ContainsKey(valueName);

        public void SetRunValue(StartupHive hive, string keyPath, string valueName, string data)
        {
            var key = (hive.ToString(), keyPath);
            if (!RunValues.TryGetValue(key, out var values))
                RunValues[key] = values = new Dictionary<string, string?>();
            values[valueName] = data;
        }
    }

    private StartupManager NewManager(FakeStartupEnvironment env) =>
        new(Path.Combine(_root, "backup"), new HistoryManager(Path.Combine(_root, "history.jsonl")), env);

    private string BackupDir => Path.Combine(_root, "backup");

    [Fact]
    public void 文件型启动项_禁用还原往返一致()
    {
        var startupDir = Path.Combine(_root, "Startup");
        Directory.CreateDirectory(startupDir);
        var notePath = Path.Combine(startupDir, "demo.lnk");
        File.WriteAllText(notePath, "x");

        var env = new FakeStartupEnvironment { Directories = { startupDir } };
        var history = new HistoryManager(Path.Combine(_root, "history.jsonl"));
        var manager = new StartupManager(BackupDir, history, env);

        var item = Assert.Single(manager.Enumerate());
        Assert.Equal(StartupKind.File, item.Kind);

        manager.Disable(item);
        Assert.False(File.Exists(notePath));
        var disabled = Assert.Single(manager.ListDisabled());
        Assert.Equal(nameof(StartupKind.File), disabled.Kind);
        Assert.Equal(notePath, disabled.Command); // 记录保存原路径
        Assert.Equal(Path.Combine(BackupDir, "demo.lnk"), disabled.BackupFile);
        Assert.True(File.Exists(disabled.BackupFile)); // 文件本体在备份目录

        manager.Restore(item.Id);
        Assert.True(File.Exists(notePath));
        Assert.Empty(manager.ListDisabled());

        var actions = history.Recent().Select(h => h.Action).ToList();
        Assert.Contains("startup-disable", actions);
        Assert.Contains("startup-restore", actions);
    }

    [Fact]
    public void 注册表型HKCU_禁用先写备份再删值_还原重建()
    {
        const string keyPath = @"Software\KleanerTest\Run";
        var env = new FakeStartupEnvironment();
        env.Sources.Add(new RegistryRunSource(StartupHive.CurrentUser, keyPath, "HKCU"));
        env.RunValues[("CurrentUser", keyPath)] = new() { ["DemoApp"] = "\"C:\\Demo\\app.exe\" /x" };
        var manager = NewManager(env);

        var item = Assert.Single(manager.Enumerate());
        Assert.Equal(StartupKind.Registry, item.Kind);
        Assert.False(item.RequiresElevation);

        manager.Disable(item);
        Assert.False(env.RunValueExists(StartupHive.CurrentUser, keyPath, "DemoApp"));
        var disabled = Assert.Single(manager.ListDisabled());
        Assert.Equal("\"C:\\Demo\\app.exe\" /x", disabled.Command);

        manager.Restore(item.Id);
        Assert.True(env.RunValueExists(StartupHive.CurrentUser, keyPath, "DemoApp"));
        Assert.Equal("\"C:\\Demo\\app.exe\" /x", env.RunValues[("CurrentUser", keyPath)]["DemoApp"]);
        Assert.Empty(manager.ListDisabled());
    }

    [Fact]
    public void 注册表型_删除失败回滚备份记录()
    {
        const string keyPath = @"Software\KleanerTest\Run";
        var env = new FakeStartupEnvironment();
        env.Sources.Add(new RegistryRunSource(StartupHive.CurrentUser, keyPath, "HKCU"));
        env.RunValues[("CurrentUser", keyPath)] = new() { ["DemoApp"] = "data" };
        env.FailingDeletes.Add($"CurrentUser|{keyPath}|DemoApp");
        var manager = NewManager(env);

        var item = Assert.Single(manager.Enumerate());
        Assert.Throws<InvalidOperationException>(() => manager.Disable(item));
        Assert.Empty(manager.ListDisabled()); // 备份记录已回滚
        Assert.True(env.RunValueExists(StartupHive.CurrentUser, keyPath, "DemoApp")); // 值未被删除
    }

    [Fact]
    public void 错误路径_还原目标被占用不覆盖且备份保留()
    {
        var startupDir = Path.Combine(_root, "Startup");
        Directory.CreateDirectory(startupDir);
        var notePath = Path.Combine(startupDir, "demo.lnk");
        File.WriteAllText(notePath, "original");

        var env = new FakeStartupEnvironment { Directories = { startupDir } };
        var manager = NewManager(env);

        var item = Assert.Single(manager.Enumerate());
        manager.Disable(item);
        Assert.False(File.Exists(notePath));

        File.WriteAllText(notePath, "new-content"); // 原位置出现新文件
        Assert.Throws<InvalidOperationException>(() => manager.Restore(item.Id));
        Assert.Equal("new-content", File.ReadAllText(notePath)); // 未覆盖新文件
        Assert.Single(manager.ListDisabled());                   // 备份记录保留
        Assert.True(File.Exists(Path.Combine(BackupDir, "demo.lnk"))); // 备份文件保留
    }

    [Fact]
    public void 错误路径_备份文件丢失还原抛异常()
    {
        var startupDir = Path.Combine(_root, "Startup");
        Directory.CreateDirectory(startupDir);
        var notePath = Path.Combine(startupDir, "demo.lnk");
        File.WriteAllText(notePath, "x");

        var env = new FakeStartupEnvironment { Directories = { startupDir } };
        var manager = NewManager(env);

        var item = Assert.Single(manager.Enumerate());
        manager.Disable(item);
        File.Delete(Path.Combine(BackupDir, "demo.lnk")); // 备份文件丢失

        Assert.Throws<FileNotFoundException>(() => manager.Restore(item.Id));
        Assert.Single(manager.ListDisabled()); // 记录仍在，等待人工处理
    }

    [Fact]
    public void 错误路径_备份目录同名文件拒绝禁用()
    {
        var dir1 = Path.Combine(_root, "UserStartup");
        var dir2 = Path.Combine(_root, "CommonStartup");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);
        var f1 = Path.Combine(dir1, "demo.lnk");
        var f2 = Path.Combine(dir2, "demo.lnk");
        File.WriteAllText(f1, "a");
        File.WriteAllText(f2, "b");

        var env = new FakeStartupEnvironment { Directories = { dir1, dir2 } };
        var manager = NewManager(env);

        var items = manager.Enumerate().ToList();
        Assert.Equal(2, items.Count);
        var first = items.First(i => i.Id == $"file|{f1}");
        var second = items.First(i => i.Id == $"file|{f2}");

        manager.Disable(first); // 备份目录已有 demo.lnk
        Assert.Throws<InvalidOperationException>(() => manager.Disable(second));
        Assert.Single(manager.ListDisabled());
    }

    [Fact]
    public void 错误路径_找不到备份记录还原抛异常()
    {
        var manager = NewManager(new FakeStartupEnvironment());
        Assert.Throws<FileNotFoundException>(() => manager.Restore("reg|HKCU|Run|NoSuchApp"));
    }
}
