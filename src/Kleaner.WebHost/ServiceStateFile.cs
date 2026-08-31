using System.Text.Json;

namespace Kleaner.WebHost;

public sealed record ServiceState(int Port, string Token);

/// <summary>
/// %APPDATA%\Kleaner\service.json：记录当前实例的端口与本次启动 token。
/// 首个实例起服务后写入；二次启动读它来唤起已有实例（工单 03 决策）。
/// </summary>
public static class ServiceStateFile
{
    public static string GetPath(string? directory = null)
    {
        var dir = directory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kleaner");
        return Path.Combine(dir, "service.json");
    }

    public static void Write(int port, string token, string? directory = null)
    {
        var path = GetPath(directory);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new ServiceState(port, token)));
    }

    public static ServiceState? TryRead(string? directory = null)
    {
        try
        {
            var path = GetPath(directory);
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<ServiceState>(File.ReadAllText(path));
        }
        catch
        {
            // 状态文件损坏/不可读不该阻塞二次启动——退化为打开不带 token 的根页面
            return null;
        }
    }
}
