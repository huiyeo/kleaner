using System.Diagnostics;

namespace Kleaner.WebHost;

/// <summary>经系统默认浏览器打开前端页面。打不开不致命：token 在 service.json，可手动访问。</summary>
internal static class FrontendLauncher
{
    public static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // 无默认浏览器等极端环境：静默降级，用户可自行打开 service.json 里的地址
        }
    }

    public static string BuildUrl(int port, string? token) =>
        $"http://127.0.0.1:{port}/" + (token is null ? "" : $"?token={Uri.EscapeDataString(token)}");
}
