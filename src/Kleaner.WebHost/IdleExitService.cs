using Microsoft.Extensions.Hosting;

namespace Kleaner.WebHost;

/// <summary>
/// 空闲自动退出（工单 03 决策）：无进行中 job 且无 in-flight 请求持续超过宽限期 → 进程退出。
/// 不引入托盘图标。扫描/清理进行中（job 计数 &gt; 0）不退出；SSE 长连接（in-flight &gt; 0）不退出。
/// </summary>
internal sealed class IdleExitService(
    IActivityTracker tracker,
    KleanerWebHostOptions options,
    IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.IdleCheckIntervalSeconds)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!tracker.IsIdle)
            {
                continue;
            }

            var idleFor = DateTimeOffset.UtcNow - tracker.LastActivity;
            if (idleFor.TotalSeconds >= options.IdleGraceSeconds)
            {
                lifetime.StopApplication();
                return;
            }
        }
    }
}
