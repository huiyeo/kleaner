namespace Kleaner.WebHost;

/// <summary>
/// 具名互斥体单实例（工单 03 决策）。拿不到互斥体 = 已有实例在跑，
/// 由调用方负责唤起已有实例后退出。允许提权后的新实例短暂重试获取。
/// </summary>
internal static class SingleInstanceGuard
{
    public const string MutexName = @"Local\Kleaner.WebHost.SingleInstance";

    /// <returns>已有实例在跑返回 null；否则返回已持有的互斥体（调用方负责 Dispose/Release）。</returns>
    public static Mutex? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: false, MutexName);
        try
        {
            if (!mutex.WaitOne(TimeSpan.Zero))
            {
                mutex.Dispose();
                return null;
            }

            return mutex;
        }
        catch (AbandonedMutexException)
        {
            // 上个实例异常退出没释放互斥体——WaitOne 抛出即表示本线程已接管所有权
            return mutex;
        }
    }
}
