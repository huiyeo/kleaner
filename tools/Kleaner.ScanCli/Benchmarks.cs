using System.Diagnostics;
using Kleaner.Analysis;
using Kleaner.Core;

namespace Kleaner.ScanCli;

public sealed record ScenarioResult(
    string Scenario,
    int Iterations,
    IReadOnlyList<double> ElapsedMs,
    IReadOnlyList<double>? FirstProgressMs,
    IReadOnlyList<double>? CancelLatencyMs);

/// <summary>
/// 性能基准场景。每个场景只做被测量的工作并返回耗时样本；峰值内存与 CPU 时间由父进程
/// 以子进程方式运行本类时采集（bench 调 bench-single），保证逐场景独立测量。
/// 计时口径：Stopwatch 覆盖单次完整调用（含枚举产出），不含数据集生成。
/// </summary>
public static class Benchmarks
{
    public static readonly string[] AllScenarios = ["rules-scan", "usage", "large-files", "duplicates", "cancel"];

    public static ScenarioResult Run(string scenario, string root, string? rulesPath, int iterations)
    {
        var elapsed = new List<double>();
        List<double>? firstProgress = scenario == "cancel" ? [] : null;
        List<double>? cancelLatency = scenario == "cancel" ? [] : null;
        for (var i = 0; i < iterations; i++)
            RunOnce(scenario, root, rulesPath, elapsed, firstProgress, cancelLatency);
        return new ScenarioResult(scenario, iterations, elapsed, firstProgress, cancelLatency);
    }

    /// <summary>进程峰值资源自报：退出后跨进程查询不可用，由 bench-single 在退出前采集。
    /// 磁盘读写字节来自进程 IO 计数器（含子进程继承的文件 IO），是 SLO 冻结条件中的磁盘读取量口径。</summary>
    public static object SelfResourceUsage()
    {
        var self = Process.GetCurrentProcess();
        return new
        {
            peakWorkingSetBytes = self.PeakWorkingSet64,
            cpuTimeMs = Math.Round(self.TotalProcessorTime.TotalMilliseconds, 1),
            diskReadBytes = QueryDiskReadBytes(),
            diskWriteBytes = QueryDiskWriteBytes(),
        };
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessIoCounters(IntPtr processHandle, out IoCounters counters);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    private static ulong QueryDiskReadBytes() =>
        GetProcessIoCounters(Process.GetCurrentProcess().Handle, out var counters) ? counters.ReadTransferCount : 0;

    private static ulong QueryDiskWriteBytes() =>
        GetProcessIoCounters(Process.GetCurrentProcess().Handle, out var counters) ? counters.WriteTransferCount : 0;

    private static void RunOnce(string scenario, string root, string? rulesPath,
        List<double> elapsed, List<double>? firstProgress, List<double>? cancelLatency)
    {
        var clock = Stopwatch.StartNew();
        switch (scenario)
        {
            case "rules-scan":
            {
                var set = RuleSetLoader.LoadFromFile(rulesPath ?? BundledRulesPath());
                RuleSetLoader.Validate(set);
                // --rules 提供的是数据集作用域规则，root 不能再作为隔离区排除根（否则数据集候选全被排除）；
                // 回退捆绑真实规则库时保留旧语义：root 用于防止 user-temp 类规则把合成数据集扫进结果
                var quarantine = rulesPath is null ? root : null;
                var report = new ScanEngine(quarantine).Scan(set);
                clock.Stop();
                if (report.Errors.Count > 0)
                    throw new InvalidOperationException("扫描出现错误，测量无效：" + report.Errors[0]);
                break;
            }
            case "usage":
            {
                var items = DiskUsageAnalyzer.TopLevel(root);
                long seen = 0;
                foreach (var item in items) seen += item.SizeBytes;
                clock.Stop();
                if (seen <= 0) throw new InvalidOperationException("空间分析未读到数据，测量无效");
                break;
            }
            case "large-files":
            {
                var items = LargeFileScanner.Scan(root, 64 * 1024, 100);
                clock.Stop();
                if (items.Count == 0) throw new InvalidOperationException("大文件扫描无结果，测量无效");
                break;
            }
            case "duplicates":
            {
                var groups = DuplicateFinder.Find(root, 64);
                clock.Stop();
                if (groups.Count == 0) throw new InvalidOperationException("重复文件扫描无结果，测量无效");
                break;
            }
            case "cancel":
            {
                var set = RuleSetLoader.LoadFromFile(rulesPath ?? BundledRulesPath());
                RuleSetLoader.Validate(set);
                using var source = new CancellationTokenSource();
                var started = Stopwatch.StartNew();
                var firstSeen = new TaskCompletionSource();
                var cancelIssued = new TaskCompletionSource();
                var progress = new Progress<ScanProgress>(_ =>
                {
                    if (firstSeen.Task.IsCompleted) return;
                    firstSeen.TrySetResult();
                    source.Cancel();
                    cancelIssued.TrySetResult();
                });
                var scanTask = Task.Run(() =>
                {
                    try
                    {
                        // 排除根语义与 rules-scan 一致：数据集作用域规则下 root 不是排除根
                        var quarantine = rulesPath is null ? root : null;
                        return new ScanEngine(quarantine).Scan(set, source.Token, progress);
                    }
                    catch (OperationCanceledException)
                    {
                        // 取消后扫描以 OCE 结束——这正是「已停止」的确认，不是测量失败。
                        return new ScanReport(DateTime.UtcNow, [], []);
                    }
                });
                firstSeen.Task.Wait(TimeSpan.FromSeconds(30));
                var firstProgressMs = started.Elapsed.TotalMilliseconds;
                // 口径按 goals.md：取消指令发出到扫描确认停止的间隔；目标 2 秒内。
                cancelIssued.Task.Wait(TimeSpan.FromSeconds(30));
                var cancelStopwatch = Stopwatch.StartNew();
                // 取消后扫描以 OperationCanceledException 结束——这正是「已停止」的确认。
                if (!scanTask.Wait(TimeSpan.FromSeconds(30)))
                    throw new InvalidOperationException("取消后 30 秒内扫描未停止，取消语义无效");
                cancelStopwatch.Stop();
                clock.Stop();
                firstProgress!.Add(firstProgressMs);
                cancelLatency!.Add(cancelStopwatch.Elapsed.TotalMilliseconds);
                break;
            }
            default:
                throw new ArgumentException($"未知场景：{scenario}");
        }
        elapsed.Add(clock.Elapsed.TotalMilliseconds);
    }

    internal static string BundledRulesPath() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "rules", "rules.v1.json"));

    /// <summary>最近邻秩百分位：升序序列取第 ceil(p/100*n) 个（1 基）。空序列抛异常，不静默给 0。</summary>
    public static double Percentile(IReadOnlyList<double> sortedAscending, int percentile)
    {
        if (sortedAscending.Count == 0) throw new ArgumentException("样本为空", nameof(sortedAscending));
        var rank = (int)Math.Ceiling(percentile / 100.0 * sortedAscending.Count);
        return sortedAscending[Math.Clamp(rank, 1, sortedAscending.Count) - 1];
    }
}
