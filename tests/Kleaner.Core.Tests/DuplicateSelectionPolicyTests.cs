using Kleaner.Analysis;
using Xunit;

namespace Kleaner.Core.Tests;

public sealed class DuplicateSelectionPolicyTests
{
    private static DuplicateCandidate C(string path, long size, DateTime mtime) => new(path, size, mtime);

    [Fact]
    public void 组内最新一份保留_其余为副本()
    {
        var files = new[]
        {
            C(@"C:\a\old.txt", 100, new DateTime(2026, 1, 1)),
            C(@"C:\a\new.txt", 100, new DateTime(2026, 3, 1)),
            C(@"C:\a\mid.txt", 100, new DateTime(2026, 2, 1)),
        };

        var plan = DuplicateSelectionPolicy.Plan(files);

        Assert.Equal(3, plan.Count);
        Assert.True(plan[0].IsKeep);
        Assert.Equal(@"C:\a\new.txt", plan[0].Path, ignoreCase: true);
        Assert.False(plan[1].IsKeep);
        Assert.False(plan[2].IsKeep);
    }

    [Fact]
    public void 时间戳相同按路径字典序稳定排序_且仅第一份保留()
    {
        var same = new DateTime(2026, 6, 1, 12, 0, 0);
        var files = new[]
        {
            C(@"C:\b\z.txt", 50, same),
            C(@"C:\b\a.txt", 50, same),
            C(@"C:\b\m.txt", 50, same),
        };

        var plan = DuplicateSelectionPolicy.Plan(files);

        Assert.Equal(@"C:\b\a.txt", plan[0].Path, ignoreCase: true);
        Assert.Equal(@"C:\b\m.txt", plan[1].Path, ignoreCase: true);
        Assert.Equal(@"C:\b\z.txt", plan[2].Path, ignoreCase: true);
        Assert.True(plan[0].IsKeep);
        Assert.False(plan[1].IsKeep);
        Assert.False(plan[2].IsKeep);
    }

    [Fact]
    public void 单文件组_唯一文件保留()
    {
        var plan = DuplicateSelectionPolicy.Plan(new[]
        {
            C(@"C:\c\solo.txt", 10, new DateTime(2026, 5, 1)),
        });

        var item = Assert.Single(plan);
        Assert.True(item.IsKeep);
        Assert.Equal(10, item.SizeBytes);
    }

    [Fact]
    public void 空组返回空()
    {
        Assert.Empty(DuplicateSelectionPolicy.Plan(Array.Empty<DuplicateCandidate>()));
    }

    [Fact]
    public void 保留策略与原始大小时间戳一致()
    {
        var mtime = new DateTime(2026, 7, 7, 8, 0, 0);
        var plan = DuplicateSelectionPolicy.Plan(new[]
        {
            C(@"C:\d\f.txt", 12345, mtime),
        });

        Assert.Equal(12345, plan[0].SizeBytes);
        Assert.Equal(mtime, plan[0].LastWriteTimeUtc);
    }
}
