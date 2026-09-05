using System.Diagnostics;
using System.Text.Json;
using Kleaner.Executor;
using Xunit.Abstractions;

namespace Kleaner.Core.Tests;

public sealed class HistoryReadBenchmarkTests(ITestOutputHelper output)
{
    [Fact]
    public void 合成历史流式读取与旧排序结果一致并报告成本()
    {
        var root = Path.Combine(Path.GetTempPath(), "kleaner-history-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "history.jsonl");
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        try
        {
            using (var writer = new StreamWriter(path))
            {
                for (var i = 0; i < 5000; i++)
                {
                    var entry = new HistoryEntry(i.ToString(), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(i % 137),
                        "restore-file", new string('x', 128), 1, 7, "ok");
                    writer.WriteLine(JsonSerializer.Serialize(entry, options));
                }
            }
            // 固定夹具、乱序且有同时间记录；保留旧算法作为非生产对照，不以耗时阈值制造易抖测试。
            List<HistoryEntry> Legacy() => File.ReadAllLines(path)
                .Select(line => JsonSerializer.Deserialize<HistoryEntry>(line, options)!)
                .OrderByDescending(entry => entry.Utc).Take(200).ToList();
            var history = new HistoryManager(path);
            Assert.Equal(Legacy(), history.Recent());
            Assert.Equal(1000, history.Recent(int.MaxValue).Count);
            Assert.Empty(history.Recent(0));
            var expected = Legacy();
            foreach (var (name, read) in new (string, Func<IReadOnlyList<HistoryEntry>>)[]
                     { ("legacy", Legacy), ("bounded", () => history.Recent()) })
            {
                var timings = new List<double>();
                var allocations = new List<long>();
                for (var sample = 0; sample < 5; sample++)
                {
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    var timer = Stopwatch.StartNew();
                    var result = read();
                    timer.Stop();
                    allocations.Add(GC.GetAllocatedBytesForCurrentThread() - before);
                    timings.Add(timer.Elapsed.TotalMilliseconds);
                    Assert.Equal(expected, result);
                }
                timings.Sort();
                output.WriteLine($"{name}: rows=5000, bytes={new FileInfo(path).Length}, samples=5, warm median_ms={timings[2]:F2}, max_ms={timings[4]:F2}, mean_allocated_bytes={allocations.Average():F0}");
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
