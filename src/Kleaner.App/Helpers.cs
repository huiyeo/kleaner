using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using System.Windows.Input;

namespace Kleaner.App;

public static class Helpers
{
    public static string FormatBytes(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):F2} GB"
        : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):F1} MB"
        : bytes >= 1024 ? $"{bytes / 1024.0:F0} KB"
        : $"{bytes} B";

    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static void RestartElevated()
    {
        var psi = new ProcessStartInfo(Environment.ProcessPath ?? "Kleaner.App.exe")
        {
            UseShellExecute = true,
            Verb = "runas",
        };
        Process.Start(psi);
    }

    public static void RunSystemCommand(string command, bool requiresAdmin)
    {
        var parts = command.Split(' ', 2);
        var psi = new ProcessStartInfo
        {
            FileName = parts[0],
            Arguments = parts.Length > 1 ? parts[1] : string.Empty,
            UseShellExecute = true,
        };
        if (requiresAdmin)
            psi.Verb = "runas";
        Process.Start(psi);
    }
} 

/// <summary>工具窗口的键盘支持：Esc 关闭窗口（主窗口不适用）。</summary>
public static class WindowKeyboard
{
    public static void EnableEscClose(Window window)
    {
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) window.Close();
        };
    }
}
