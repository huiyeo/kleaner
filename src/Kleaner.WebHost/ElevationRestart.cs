using System.Diagnostics;

namespace Kleaner.WebHost;

/// <summary>受控提权交接：子进程只接收当前服务签发的端口与 token，并等待旧实例释放单实例锁。</summary>
internal static class ElevationRestart
{
    public static bool Start(int port, string token)
    {
        try
        {
            var arguments = BuildArguments(Environment.GetCommandLineArgs(), port, token);
            return Process.Start(new ProcessStartInfo(Environment.ProcessPath ?? "Kleaner.WebHost.exe", arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory,
            }) is not null;
        }
        catch
        {
            // UAC 被取消或系统拒绝：原实例继续服务，前端显示失败而非断开。
            return false;
        }
    }

    internal static string BuildArguments(IEnumerable<string> commandLine, int port, string token)
    {
        var entry = commandLine.FirstOrDefault(argument => argument.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        var prefix = entry is null ? string.Empty : $"\"{entry}\" ";
        return $"{prefix}--handoff-port {port} --handoff-token {token}";
    }

    public static bool TryParseHandoff(string[] args, out int port, out string? token)
    {
        port = 0;
        token = null;
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--handoff-port") int.TryParse(args[++i], out port);
            else if (args[i] == "--handoff-token") token = args[++i];
        }

        return port is > 0 and <= 65535 && !string.IsNullOrWhiteSpace(token);
    }
}
