using System.Collections.Concurrent;

namespace Kleaner.WebHost;

/// <summary>job 状态机（工单 07）：running → cancelling → cancelled/completed。终态不可逆。</summary>
public enum JobStatus
{
    Running,
    Cancelling,
    Cancelled,
    Completed,
}

/// <summary>取消端点的结果（工单 11：202 / 404 / 409）。</summary>
public enum JobCancelOutcome
{
    /// <summary>job 不存在 → 404。</summary>
    NotFound,

    /// <summary>已受理取消（Running→Cancelling，或已在 Cancelling 幂等）→ 202。</summary>
    Accepted,

    /// <summary>已终态（cancelled/completed）→ 409。清理类 job 不进本体系（工单 12 的确认闸），同样无取消可达。</summary>
    Conflict,
}

/// <summary>job 快照（REST 对外形状）：不含 CTS 等运行时句柄，重连方据此重建状态（工单 07）。</summary>
public sealed record JobSnapshot(
    string JobId,
    string Kind,
    string Status,
    DateTimeOffset StartedUtc,
    DateTimeOffset? EndedUtc,
    object? Result);

/// <summary>
/// 单个后台 job 的服务端记录：持有自己的 <see cref="CancellationTokenSource"/>
/// 状态迁移与终态清理全部在锁内完成。
/// </summary>
public sealed class JobRecord
{
    private readonly object _gate = new();
    private JobStatus _status = JobStatus.Running;
    private CancellationTokenSource? _cts = new();
    private object? _result;
    private DateTimeOffset? _endedUtc;

    public string JobId { get; } = Guid.NewGuid().ToString("N");

    /// <summary>job 种类：scan / toolbox 等（事件名与快照携带，前端据此渲染）。</summary>
    public string Kind { get; }

    public DateTimeOffset StartedUtc { get; } = DateTimeOffset.UtcNow;

    internal JobRecord(string kind) => Kind = kind;

    public JobStatus Status
    {
        get { lock (_gate) return _status; }
    }

    public DateTimeOffset? EndedUtc
    {
        get { lock (_gate) return _endedUtc; }
    }

    /// <summary>job 结果（scan 类为 ScanReport）；取消时为 null。终态后经 REST 快照可取回。</summary>
    public object? Result
    {
        get { lock (_gate) return _result; }
    }

    /// <summary>运行期取消令牌。只在启动 job 时取用；终态后 CTS 已清理，返回 None。</summary>
    public CancellationToken Token
    {
        get { lock (_gate) return _cts?.Token ?? CancellationToken.None; }
    }

    /// <summary>
    /// 受理取消：Running → Cancelling 并触发令牌；已在 Cancelling 则幂等；终态返回 false（409）。
    /// Cancel 在锁内调用，与 <see cref="Finish"/> 的 Dispose 互斥，避免 use-after-dispose。
    /// </summary>
    internal bool TryBeginCancel()
    {
        lock (_gate)
        {
            if (_status is JobStatus.Cancelled or JobStatus.Completed)
            {
                return false;
            }

            if (_status == JobStatus.Running)
            {
                _status = JobStatus.Cancelling;
                _cts!.Cancel();
            }

            return true;
        }
    }

    /// <summary>进入终态：落结果、清 CTS 防泄漏（工单 07 Comments）。竞态下先到先得，后到为空操作。</summary>
    internal void Finish(JobStatus terminal, object? result)
    {
        lock (_gate)
        {
            if (_status is JobStatus.Cancelled or JobStatus.Completed)
            {
                return;
            }

            _status = terminal;
            _result = result;
            _endedUtc = DateTimeOffset.UtcNow;
            _cts!.Dispose();
            _cts = null;
        }
    }

    public JobSnapshot ToSnapshot()
    {
        lock (_gate)
        {
            return new JobSnapshot(JobId, Kind, _status.ToString(), StartedUtc, _endedUtc, _result);
        }
    }
}

/// <summary>
/// job 注册表：`ConcurrentDictionary&lt;jobId, JobRecord&gt;`，记录常驻到进程退出
///（工单 07：重开浏览器可从 REST 快照取回已完成结果，不做事件回放）。
/// job 计数接 <see cref="IActivityTracker"/>（工单 10 的调用方缺口）。
/// </summary>
public sealed class JobRegistry
{
    private readonly ConcurrentDictionary<string, JobRecord> _jobs = new(StringComparer.Ordinal);
    private readonly IActivityTracker _tracker;

    public JobRegistry(IActivityTracker tracker) => _tracker = tracker;

    /// <summary>创建并登记一个 running job（同时占用一个 job 计数，空闲退出据此等待）。</summary>
    public JobRecord Create(string kind)
    {
        var job = new JobRecord(kind);
        _jobs[job.JobId] = job;
        _tracker.JobStarted();
        return job;
    }

    public JobRecord? Get(string jobId) => _jobs.TryGetValue(jobId, out var job) ? job : null;

    /// <summary>全量快照（重连时前端先取它再接 SSE 增量，工单 07）。</summary>
    public IReadOnlyList<JobSnapshot> Snapshot() =>
        _jobs.Values.OrderBy(job => job.StartedUtc).Select(job => job.ToSnapshot()).ToArray();

    /// <summary>取消语义（工单 11）：未知 404、终态 409、其余 202。</summary>
    public JobCancelOutcome TryCancel(string jobId)
    {
        var job = Get(jobId);
        if (job is null)
        {
            return JobCancelOutcome.NotFound;
        }

        return job.TryBeginCancel() ? JobCancelOutcome.Accepted : JobCancelOutcome.Conflict;
    }

    /// <summary>job 正常跑完（取消请求到达但工作已完成时也走这里，先到先得）。</summary>
    public void Complete(JobRecord job, object? result)
    {
        job.Finish(JobStatus.Completed, result);
        _tracker.JobEnded();
    }

    /// <summary>job 被取消（执行方观察到 OperationCanceledException 后调用）。</summary>
    public void MarkCancelled(JobRecord job)
    {
        job.Finish(JobStatus.Cancelled, null);
        _tracker.JobEnded();
    }
}
