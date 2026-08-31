using Kleaner.Analysis;

namespace Kleaner.WebHost;

/// <summary>工具箱的只读扫描请求基类；结果只存入 job 快照，绝不把伪规则 id 接入隔离区或历史。</summary>
public abstract record ToolboxJobRequest(string Root);

public sealed record LargeFilesRequest(string Root, long MinBytes, int Top = 200) : ToolboxJobRequest(Root);

public sealed record DuplicatesRequest(string Root, long MinBytesPerFile) : ToolboxJobRequest(Root);

public sealed record UsageRequest(string Root) : ToolboxJobRequest(Root);

public interface IToolboxJobService
{
    JobRecord Start(ToolboxJobRequest request);
}

/// <summary>三个工具箱动作共用的 job 执行体：只发开始/结束 SSE，取消时不产生任何写副作用。</summary>
internal sealed class ToolboxJobService : IToolboxJobService
{
    private readonly JobRegistry _registry;
    private readonly IJobEventBus _bus;
    private readonly KleanerWebHostOptions _options;

    public ToolboxJobService(JobRegistry registry, IJobEventBus bus, KleanerWebHostOptions options)
    {
        _registry = registry;
        _bus = bus;
        _options = options;
    }

    public JobRecord Start(ToolboxJobRequest request)
    {
        var job = _registry.Create(KindFor(request));
        Publish("job.started", job, new { jobId = job.JobId, kind = job.Kind });
        _ = Task.Run(() => Run(job, request));
        return job;
    }

    private void Run(JobRecord job, ToolboxJobRequest request)
    {
        try
        {
            var result = (_options.ToolboxExecutor ?? Execute)(request, job.Token);
            _registry.Complete(job, result);
            Publish("job.completed", job, new { jobId = job.JobId, kind = job.Kind });
        }
        catch (OperationCanceledException)
        {
            _registry.MarkCancelled(job);
            Publish("job.cancelled", job, new { jobId = job.JobId, kind = job.Kind });
        }
        catch (Exception ex)
        {
            // Job 状态机没有 failed 分支；将错误放进完成结果，确保 CTS/活动计数必定收口。
            _registry.Complete(job, new { error = ex.Message });
            Publish("job.completed", job, new { jobId = job.JobId, kind = job.Kind, error = true });
        }
    }

    private static object Execute(ToolboxJobRequest request, CancellationToken token) => request switch
    {
        LargeFilesRequest large => LargeFileScanner.Scan(large.Root, large.MinBytes, large.Top, token),
        DuplicatesRequest duplicates => DuplicateFinder.Find(duplicates.Root, duplicates.MinBytesPerFile, token),
        UsageRequest usage => DiskUsageAnalyzer.TopLevel(usage.Root, token),
        _ => throw new ArgumentOutOfRangeException(nameof(request)),
    };

    private static string KindFor(ToolboxJobRequest request) => request switch
    {
        LargeFilesRequest => "toolbox.large-files",
        DuplicatesRequest => "toolbox.duplicates",
        UsageRequest => "toolbox.usage",
        _ => throw new ArgumentOutOfRangeException(nameof(request)),
    };

    private void Publish(string eventName, JobRecord job, object data) => _bus.Publish(eventName, job.JobId, data);
}
