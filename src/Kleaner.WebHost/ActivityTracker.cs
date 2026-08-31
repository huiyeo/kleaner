namespace Kleaner.WebHost;

/// <summary>
/// 进程活跃度跟踪（工单 03：空闲自动退出的判据）。
/// 「无进行中 job 且无 in-flight 请求」持续超过宽限期即视为空闲。
/// in-flight 请求计数天然覆盖 SSE 长连接（工单 11 的事件流会占用一个 in-flight 请求），
/// 因此浏览器页面开着（SW/SSE 活跃）时进程不会误退。
/// </summary>
public interface IActivityTracker
{
    /// <summary>有外部动静（任意请求进出、任务推进）时刷新最后活跃时刻。</summary>
    void Touch();

    void RequestStarted();
    void RequestEnded();
    void JobStarted();
    void JobEnded();

    bool IsIdle { get; }
    DateTimeOffset LastActivity { get; }
}

internal sealed class ActivityTracker : IActivityTracker
{
    private long _inFlightRequests;
    private long _runningJobs;

    public DateTimeOffset LastActivity { get; private set; } = DateTimeOffset.UtcNow;

    public bool IsIdle =>
        Interlocked.Read(ref _inFlightRequests) == 0 && Interlocked.Read(ref _runningJobs) == 0;

    public void Touch() => LastActivity = DateTimeOffset.UtcNow;

    public void RequestStarted()
    {
        Interlocked.Increment(ref _inFlightRequests);
        Touch();
    }

    public void RequestEnded()
    {
        Interlocked.Decrement(ref _inFlightRequests);
        Touch();
    }

    public void JobStarted()
    {
        Interlocked.Increment(ref _runningJobs);
        Touch();
    }

    public void JobEnded()
    {
        Interlocked.Decrement(ref _runningJobs);
        Touch();
    }
}
