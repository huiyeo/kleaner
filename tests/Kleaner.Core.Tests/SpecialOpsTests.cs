using Kleaner.SpecialOps;

namespace Kleaner.Core.Tests;

public sealed class SpecialOpsTests
{
    [Fact]
    public void RegistryScan_OnlyReadsAndReturnsBrokenEntries()
    {
        var root = new RegistryRoot("HKEY_CURRENT_USER", @"Software\Demo\Uninstall");
        var reader = new FakeRegistryReader(root, new FakeRegistryKey(
            children:
            [
                ("Missing", new Dictionary<string, object?>
                {
                    ["DisplayName"] = "Missing App",
                    ["InstallLocation"] = @"C:\missing",
                }),
                ("Present", new Dictionary<string, object?>
                {
                    ["DisplayName"] = "Present App",
                    ["InstallLocation"] = @"C:\present",
                }),
            ]));

        var report = RegistryInspector.ScanBrokenUninstallEntries(
            reader,
            directoryExists: path => path == @"C:\present",
            fileExists: _ => false);

        var entry = Assert.Single(report.Entries);
        Assert.Equal("Missing App", entry.DisplayName);
        Assert.Equal("InstallLocation 目录不存在", entry.Reason);
        Assert.Empty(report.Errors);
        Assert.True(reader.ReadCalls > 0);
        Assert.Equal(0, reader.WriteCalls);
    }

    [Fact]
    public void RegistryScan_RecordsUnreadableChildAndContinues()
    {
        var root = new RegistryRoot("HKEY_CURRENT_USER", @"Software\Demo\Uninstall");
        var key = new FakeRegistryKey(
            children:
            [
                ("Unreadable", null),
                ("Broken", new Dictionary<string, object?>
                {
                    ["DisplayName"] = "Broken App",
                    ["UninstallString"] = @"C:\missing\uninstall.exe /silent",
                }),
            ],
            throwingChildren: ["Unreadable"]);
        var reader = new FakeRegistryReader(root, key);

        var report = RegistryInspector.ScanBrokenUninstallEntries(
            reader,
            directoryExists: _ => false,
            fileExists: _ => false);

        Assert.Equal("Broken App", Assert.Single(report.Entries).DisplayName);
        Assert.Single(report.Errors);
        Assert.Contains("Unreadable", report.Errors[0]);
    }

    [Fact]
    public void WslGuideAndSystemToolCatalogExposeReadOnlyGuidance()
    {
        var guide = WslInspector.BuildCompactGuide(new VhdxInfo(@"C:\wsl\ext4.vhdx", 1_610_612_736));

        Assert.Contains(@"C:\wsl\ext4.vhdx", guide);
        Assert.Contains("不删除 WSL 内的任何数据", guide);
        Assert.Contains(SystemToolGuide.Items, item => item.RequiresAdmin && item.Command.StartsWith("powercfg", StringComparison.OrdinalIgnoreCase));
        Assert.All(SystemToolGuide.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.Note)));
    }

    [Fact]
    public void DefaultRegistryRoots_KeepWindowsUninstallPaths()
    {
        Assert.Equal(3, RegistryInspector.DefaultRoots.Count);
        Assert.All(RegistryInspector.DefaultRoots, root => Assert.Contains(@"\", root.SubKey));
        Assert.Contains(RegistryInspector.DefaultRoots, root =>
            root.SubKey == @"Software\Microsoft\Windows\CurrentVersion\Uninstall");
    }

    private sealed class FakeRegistryReader(RegistryRoot root, FakeRegistryKey key) : IReadOnlyRegistryReader
    {
        public int ReadCalls { get; private set; }

        public int WriteCalls { get; }

        public IReadOnlyList<RegistryRoot> Roots => [root];

        public IReadOnlyRegistryKey? OpenRoot(RegistryRoot requestedRoot)
        {
            Assert.Equal(root, requestedRoot);
            ReadCalls++;
            key.OnRead = () => ReadCalls++;
            return key;
        }
    }

    private sealed class FakeRegistryKey(
        IEnumerable<(string Name, Dictionary<string, object?>? Values)> children,
        IEnumerable<string>? throwingChildren = null) : IReadOnlyRegistryKey
    {
        private readonly Dictionary<string, Dictionary<string, object?>?> _children = children.ToDictionary(item => item.Name, item => item.Values);
        private readonly HashSet<string> _throwingChildren = throwingChildren?.ToHashSet() ?? [];

        public Action? OnRead { get; set; }

        public IEnumerable<string> GetSubKeyNames()
        {
            OnRead?.Invoke();
            return _children.Keys;
        }

        public IReadOnlyRegistryKey? OpenSubKey(string name)
        {
            OnRead?.Invoke();
            if (_throwingChildren.Contains(name))
                throw new UnauthorizedAccessException("Denied by test registry.");
            return _children[name] is { } values
                ? new FakeRegistryKey([], null) { Values = values, OnRead = OnRead }
                : null;
        }

        public object? GetValue(string name)
        {
            OnRead?.Invoke();
            return Values.TryGetValue(name, out var value) ? value : null;
        }

        public Dictionary<string, object?> Values { get; init; } = [];

        public void Dispose()
        {
        }
    }
}
