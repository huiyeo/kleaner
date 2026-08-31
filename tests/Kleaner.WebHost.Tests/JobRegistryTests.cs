using System.Collections.Concurrent;
using Kleaner.Core;
using Kleaner.WebHost;

namespace Kleaner.WebHost.Tests;

/// <summary>JobRegistry 状态机单元测试（工单 11）：running → cancelling → cancelled/completed、终态清理 CTS、tracker 计数。</summary>
public sealed class JobRegistryTests
{
    private sealed class CountingTracker : IActivityTracker
    {
        public int Started;
        public int Ended;
        public DateTimeOffset LastActivity { get; private set; } = DateTimeOffset.UtcNow;
        public bool IsIdle => Started == Ended;

        public void Touch() => LastActivity = DateTimeOffset.UtcNow;
        public void RequestStarted() { }
        public void RequestEnded() { }
        public void JobStarted() { Interlocked.Increment(ref Started); Touch(); }
        public void JobEnded() { Interlocked.Increment(ref Ended); Touch(); }
    }

    [Fact]
    public void TryCancel_RunningJob_TransitionsToCancelling_AndTriggersToken()
    {
        var tracker = new CountingTracker();
        var registry = new JobRegistry(tracker);
        var job = registry.Create("scan");

        Assert.Equal(JobCancelOutcome.Accepted, registry.TryCancel(job.JobId));
        Assert.Equal(JobStatus.Cancelling, job.Status);
        Assert.True(job.Token.IsCancellationRequested);

        // 已在 Cancelling：幂等受理（仍 202）
        Assert.Equal(JobCancelOutcome.Accepted, registry.TryCancel(job.JobId));
    }

    [Fact]
    public void TryCancel_UnknownJob_ReturnsNotFound()
    {
        var registry = new JobRegistry(new CountingTracker());
        Assert.Equal(JobCancelOutcome.NotFound, registry.TryCancel("no-such-job"));
    }

    [Fact]
    public void CancelRequested_ButWorkFinishesFirst_JobCompleted_ResultKept()
    {
        var tracker = new CountingTracker();
        var registry = new JobRegistry(tracker);
        var job = registry.Create("scan");
        var report = new ScanReport(DateTime.UtcNow, Array.Empty<RuleScanResult>(), Array.Empty<string>());

        Assert.Equal(JobCancelOutcome.Accepted, registry.TryCancel(job.JobId));
        registry.Complete(job, report); // 取消请求先到但工作已完成：先到先得落 Completed

        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.Same(report, job.Result);
        Assert.Equal(JobCancelOutcome.Conflict, registry.TryCancel(job.JobId)); // 终态 409
        Assert.Equal(CancellationToken.None, job.Token); // 终态后令牌安全降级（CTS 已清理）
        Assert.Equal(1, tracker.Started);
        Assert.Equal(1, tracker.Ended);
        Assert.True(tracker.IsIdle);
    }

    [Fact]
    public void MarkCancelled_TerminalState_ResultNull_TrackerBalanced()
    {
        var tracker = new CountingTracker();
        var registry = new JobRegistry(tracker);
        var job = registry.Create("toolbox");

        Assert.Equal(JobCancelOutcome.Accepted, registry.TryCancel(job.JobId));
        registry.MarkCancelled(job);

        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.Null(job.Result);
        Assert.NotNull(job.EndedUtc);
        Assert.Equal(JobCancelOutcome.Conflict, registry.TryCancel(job.JobId));
        Assert.Equal(1, tracker.Started);
        Assert.Equal(1, tracker.Ended);
    }

    [Fact]
    public void Snapshot_ContainsAllJobs_AsDtos()
    {
        var registry = new JobRegistry(new CountingTracker());
        var a = registry.Create("scan");
        var b = registry.Create("toolbox");

        var snapshot = registry.Snapshot();

        Assert.Equal(2, snapshot.Count);
        Assert.Contains(snapshot, job => job.JobId == a.JobId && job.Kind == "scan" && job.Status == "Running");
        Assert.Contains(snapshot, job => job.JobId == b.JobId && job.Kind == "toolbox");
    }
}

/// <summary>JobEventBus 单元测试（工单 11）：广播到全部订阅者、退订后不再收、不做回放。</summary>
public sealed class JobEventBusTests
{
    [Fact]
    public void Publish_ReachesAllActiveSubscribers_OnlyWithEventsAfterSubscription()
    {
        var bus = new JobEventBus();
        var received1 = new ConcurrentQueue<SseEvent>();
        var received2 = new ConcurrentQueue<SseEvent>();

        using (var sub1 = bus.Subscribe())
        using (var sub2 = bus.Subscribe())
        {
            Assert.False(sub1.Reader.TryRead(out _)); // 订阅前的事件不存在（无回放，工单 07）

            bus.Publish("job.started", "job-1", new { jobId = "job-1" });

            Assert.True(sub1.Reader.TryRead(out var evt1));
            Assert.True(sub2.Reader.TryRead(out var evt2));
            Assert.Equal("job.started", evt1.Event);
            Assert.Equal("job-1", evt1.JobId);
            Assert.Equal(evt1.Event, evt2.Event);
        }

        // 退订后不再接收
        bus.Publish("job.completed", "job-1", new { jobId = "job-1" });
        Assert.Empty(received1);
        Assert.Empty(received2);
    }
}
