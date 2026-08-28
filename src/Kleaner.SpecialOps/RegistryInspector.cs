using Microsoft.Win32;

namespace Kleaner.SpecialOps;

public sealed record BrokenInstallEntry(string Key, string DisplayName, string InstallLocation, string Reason);

/// <summary>注册表只读扫描：找"指向不存在路径"的卸载残留条目。只展示，绝不删除/修改任何注册表内容。</summary>
public static class RegistryInspector
{
    private static readonly (RegistryKey Hive, string SubKey)[] Roots =
    {
        (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
        (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
    };

    public static IReadOnlyList<BrokenInstallEntry> ScanBrokenUninstallEntries()
    {
        var result = new List<BrokenInstallEntry>();
        foreach (var (hive, subKey) in Roots)
        {
            using var key = hive.OpenSubKey(subKey, writable: false);
            if (key is null)
                continue;
            foreach (var childName in key.GetSubKeyNames())
            {
                using var child = key.OpenSubKey(childName, writable: false);
                if (child is null)
                    continue;

                var displayName = child.GetValue("DisplayName") as string;
                if (string.IsNullOrWhiteSpace(displayName))
                    continue;

                var installLocation = child.GetValue("InstallLocation") as string ?? string.Empty;
                var uninstallString = child.GetValue("UninstallString") as string ?? string.Empty;

                string? reason = null;
                if (!string.IsNullOrWhiteSpace(installLocation) && !Directory.Exists(installLocation))
                    reason = "InstallLocation 目录不存在";
                else if (!string.IsNullOrWhiteSpace(uninstallString))
                {
                    var exe = ExtractExePath(uninstallString);
                    if (exe is not null && !File.Exists(exe) && !Directory.Exists(exe))
                        reason = "卸载程序可执行文件不存在";
                }

                if (reason is not null)
                    result.Add(new BrokenInstallEntry($"{hive.Name}\\{subKey}\\{childName}", displayName, installLocation, reason));
            }
        }
        return result;
    }

    public static string? ExtractExePath(string uninstallString)
    {
        var s = uninstallString.Trim().Trim('"');
        if (s.StartsWith("MsiExec.exe", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("MsiExec", StringComparison.OrdinalIgnoreCase))
            return null; // MSI 条目由系统管理，跳过
        int idx = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;
        return s[..(idx + 4)];
    }
}
