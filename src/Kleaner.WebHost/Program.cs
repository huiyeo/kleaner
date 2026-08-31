using Microsoft.Extensions.Hosting;
using Velopack;

namespace Kleaner.WebHost;

/// <summary>
/// WebHost 进程入口（工单 10；取代 Kleaner.App/Program.cs 的 GUI 入口，工单 04 决策）：
/// VelopackApp 钩子 → 互斥体单实例（二次启动唤起已有实例的浏览器页面后自身退出）→
/// Kestrel 只绑 127.0.0.1（首选端口被占回退随机高端口）→ 写 service.json → 开浏览器 → 跑到空闲退出。
/// </summary>
public static class Program
{
    public static void Main(string[] args)
    {
        // Velopack 钩子必须最先运行（处理更新后的重启动与安装器回调，工单 02）
        VelopackApp.Build().Run();

        using var mutex = SingleInstanceGuard.TryAcquire();
        if (mutex is null)
        {
            // 二次启动：不开新服务，唤起已有实例的浏览器页面后自身退出
            var existing = ServiceStateFile.TryRead();
            FrontendLauncher.Open(existing is { } state
                ? FrontendLauncher.BuildUrl(state.Port, state.Token)
                : FrontendLauncher.BuildUrl(KleanerWebHostOptions.DefaultPreferredPort, null));
            return;
        }

        var options = new KleanerWebHostOptions
        {
            Port = PortPicker.PickFreePort(KleanerWebHostOptions.DefaultPreferredPort),
            Token = KleanerWebHostOptions.GenerateToken(),
        };

        using var app = WebHostAppFactory.Build(options);
        app.Start();

        ServiceStateFile.Write(options.Port, options.Token, options.ServiceStateDirectory);
        FrontendLauncher.Open(FrontendLauncher.BuildUrl(options.Port, options.Token));

        app.WaitForShutdown();
    }
}
