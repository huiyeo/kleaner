using Kleaner.Core;

namespace Kleaner.WebHost;

public interface IScanJobService
{
    /// <summary>启动后台扫描 job：立即返回 running 记录，进度经 SSE 流按规则推送（工单 07）。</summary>
    JobRecord Start(RuleSet set);
}

/// <summary>
/// 扫描 job 执行体：调 09 加了 IProgress&lt;ScanProgress&gt; 的 ScanEngine.Scan，
/// 每规则完成上报桥接为 scan.progress 事件；状态机与 job.started / job.completed / job.cancelled
/// 事件在 <see cref="Run"/> 内收口。断连不影响本任务（任务与连接解耦，工单 07）。
/// 引擎传入隔离区根目录以排除隔离区自身——GUI 现状语义，勿复刻 CLI 不传根的坑（工单 12 验收项）。
/// </summary>
internal sealed class ScanJobService : IScanJobService
{
    private readonly JobRegistry _registry;
    private readonly IJobEventBus _bus;
    private readonly KleanerWebHostOptions _options;

    public ScanJobService(JobRegistry registry, IJobEventBus bus, KleanerWebHostOptions options)
    {
        _registry = registry;
        _bus = bus;
        _options = options;
    }

    public JobRecord Start(RuleSet set)
    {
        var job = _registry.Create("scan");
        Publish("job.started", job.JobId, new { jobId = job.JobId, kind = job.Kind });
        _ = Task.Run(() => Run(job, set));
        return job;
    }

    private void Run(JobRecord job, RuleSet set)
    {
        try
        {
            var executor = _options.ScanExecutor
                ?? ((set, token, progress) => new ScanEngine(HostRuntime.ResolveQuarantineRoot(_options))
                    .Scan(set, token, progress));
            var report = executor(set, job.Token, new ProgressBridge(job.JobId, _bus));
            // 终态结果存薄 envelope（machineVerified / 枚举字符串化，工单 12），
            // 前端经 GET /api/jobs/{id} 快照直接取回，无需另开 /api/scans 端点
            _registry.Complete(job, ScanResultEnvelope.From(set, report));
            Publish("job.completed", job.JobId, new
            {
                jobId = job.JobId,
                kind = job.Kind,
                fileCount = report.Results.Sum(rule => rule.FileCount),
                totalBytes = report.Results.Sum(rule => rule.TotalBytes),
            });
        }
        catch (OperationCanceledException)
        {
            _registry.MarkCancelled(job);
            Publish("job.cancelled", job.JobId, new { jobId = job.JobId, kind = job.Kind });
        }
    }

    private void Publish(string eventName, string jobId, object data) => _bus.Publish(eventName, jobId, data);

    /// <summary>把 09 的每规则完成上报桥接为 SSE 增量（取消路径 ScanEngine 不上报，工单 09 语义）。</summary>
    private sealed class ProgressBridge(string jobId, IJobEventBus bus) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => bus.Publish("scan.progress", jobId, new
        {
            jobId,
            ruleId = value.RuleId,
            fileCount = value.FileCount,
            totalBytes = value.TotalBytes,
        });
    }
}
