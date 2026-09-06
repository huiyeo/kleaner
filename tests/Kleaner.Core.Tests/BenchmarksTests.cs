using Kleaner.ScanCli;

namespace Kleaner.Core.Tests;

public sealed class BenchmarksTests
{
    [Fact]
    public void 百分位取最近邻秩()
    {
        double[] samples = [10, 20, 30, 40, 100];
        Assert.Equal(30, Benchmarks.Percentile(samples, 50));
        Assert.Equal(100, Benchmarks.Percentile(samples, 95));
        Assert.Equal(10, Benchmarks.Percentile(samples, 1));
        double[] single = [7];
        Assert.Equal(7, Benchmarks.Percentile(single, 95));
    }

    [Fact]
    public void 空样本拒绝取百分位()
    {
        Assert.Throws<ArgumentException>(() => Benchmarks.Percentile([], 50));
    }

    [Fact]
    public void 未知场景被拒绝()
    {
        var root = Path.Combine(Path.GetTempPath(), "kleaner-bench-scenario-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Throws<ArgumentException>(() => Benchmarks.Run("no-such-scenario", root, null, 1));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
