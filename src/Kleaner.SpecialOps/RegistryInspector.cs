using Microsoft.Win32;

namespace Kleaner.SpecialOps;

public sealed record BrokenInstallEntry(string Key, string DisplayName, string InstallLocation, string Reason);

/// <summary>注册表根路径的只读描述。</summary>
public sealed record RegistryRoot(string HiveName, string SubKey);

/// <summary>只读注册表键抽象，用于隔离系统注册表访问并支持安全测试。</summary>
public interface IReadOnlyRegistryKey : IDisposable
{
    IEnumerable<string> GetSubKeyNames();

    IReadOnlyRegistryKey? OpenSubKey(string name);

    object? GetValue(string name);
}

/// <summary>只读注册表访问入口。接口不提供写入或删除操作。</summary>
public interface IReadOnlyRegistryReader
{
    IReadOnlyList<RegistryRoot> Roots { get; }

    IReadOnlyRegistryKey? OpenRoot(RegistryRoot root);
}

/// <summary>注册表扫描结果，错误被记录但不会阻断其他根或条目的扫描。</summary>
public sealed record RegistryScanReport(
    IReadOnlyList<BrokenInstallEntry> Entries,
    IReadOnlyList<string> Errors);

/// <summary>注册表只读扫描：找“指向不存在路径”的卸载残留条目。只展示，绝不删除或修改注册表内容。</summary>
public static class RegistryInspector
{
    /// <summary>生产扫描覆盖的只读卸载根路径。</summary>
    public static IReadOnlyList<RegistryRoot> DefaultRoots { get; } =
    [
        new(Registry.CurrentUser.Name, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
        new(Registry.LocalMachine.Name, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        new(Registry.LocalMachine.Name, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
    ];

    /// <summary>使用 Windows 注册表扫描卸载残留，保留原有无参调用入口。</summary>
    public static IReadOnlyList<BrokenInstallEntry> ScanBrokenUninstallEntries() =>
        ScanBrokenUninstallEntries(new WindowsRegistryReader()).Entries;

    /// <summary>使用指定的只读注册表与文件存在性查询扫描卸载残留。</summary>
    public static RegistryScanReport ScanBrokenUninstallEntries(
        IReadOnlyRegistryReader registry,
        Func<string, bool>? directoryExists = null,
        Func<string, bool>? fileExists = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        directoryExists ??= Directory.Exists;
        fileExists ??= File.Exists;

        var entries = new List<BrokenInstallEntry>();
        var errors = new List<string>();
        foreach (var root in registry.Roots)
        {
            try
            {
                using var key = registry.OpenRoot(root);
                if (key is null)
                    continue;

                foreach (var childName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var child = key.OpenSubKey(childName);
                        if (child is null)
                            continue;

                        var displayName = child.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(displayName))
                            continue;

                        var installLocation = child.GetValue("InstallLocation") as string ?? string.Empty;
                        var uninstallString = child.GetValue("UninstallString") as string ?? string.Empty;
                        var reason = FindBrokenReason(installLocation, uninstallString, directoryExists, fileExists);
                        if (reason is not null)
                            entries.Add(new BrokenInstallEntry(
                                $"{root.HiveName}\\{root.SubKey}\\{childName}",
                                displayName,
                                installLocation,
                                reason));
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{root.HiveName}\\{root.SubKey}\\{childName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{root.HiveName}\\{root.SubKey}: {ex.Message}");
            }
        }

        return new RegistryScanReport(entries, errors);
    }

    public static string? ExtractExePath(string uninstallString)
    {
        var s = uninstallString.Trim().Trim('"');
        if (s.StartsWith("MsiExec.exe", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("MsiExec", StringComparison.OrdinalIgnoreCase))
            return null;
        var index = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return index < 0 ? null : s[..(index + 4)];
    }

    private static string? FindBrokenReason(
        string installLocation,
        string uninstallString,
        Func<string, bool> directoryExists,
        Func<string, bool> fileExists)
    {
        if (!string.IsNullOrWhiteSpace(installLocation) && !directoryExists(installLocation))
            return "InstallLocation 目录不存在";

        var executablePath = string.IsNullOrWhiteSpace(uninstallString)
            ? null
            : ExtractExePath(uninstallString);
        return executablePath is not null && !fileExists(executablePath) && !directoryExists(executablePath)
            ? "卸载程序可执行文件不存在"
            : null;
    }

    private sealed class WindowsRegistryReader : IReadOnlyRegistryReader
    {
        public IReadOnlyList<RegistryRoot> Roots => RegistryInspector.DefaultRoots;

        public IReadOnlyRegistryKey? OpenRoot(RegistryRoot root)
        {
            var hive = root.HiveName.Equals(Registry.CurrentUser.Name, StringComparison.OrdinalIgnoreCase)
                ? Registry.CurrentUser
                : Registry.LocalMachine;
            var key = hive.OpenSubKey(root.SubKey, writable: false);
            return key is null ? null : new WindowsRegistryKey(key);
        }
    }

    private sealed class WindowsRegistryKey(RegistryKey key) : IReadOnlyRegistryKey
    {
        public IEnumerable<string> GetSubKeyNames() => key.GetSubKeyNames();

        public IReadOnlyRegistryKey? OpenSubKey(string name)
        {
            var child = key.OpenSubKey(name, writable: false);
            return child is null ? null : new WindowsRegistryKey(child);
        }

        public object? GetValue(string name) => key.GetValue(name);

        public void Dispose() => key.Dispose();
    }
}
