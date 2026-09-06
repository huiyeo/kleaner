using System.Security.Cryptography;
using Kleaner.Analysis;

namespace Kleaner.Core.Tests;

/// <summary>合成数据集生成器是性能基线的地基：不可复现的数据集让一切测量失去意义。</summary>
public sealed class SyntheticDatasetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kleaner-dataset-" + Guid.NewGuid().ToString("N"));

    public SyntheticDatasetTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static IReadOnlyList<(string Relative, long Size, string Sha256)> Inventory(string root)
    {
        var list = new List<(string, long, string)>();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            var bytes = File.ReadAllBytes(file);
            list.Add((Path.GetRelativePath(root, file), bytes.LongLength,
                Convert.ToHexString(SHA256.HashData(bytes))));
        }
        return list;
    }

    [Fact]
    public void 相同种子生成逐字节相同的目录树()
    {
        var first = Path.Combine(_root, "first");
        var second = Path.Combine(_root, "second");
        var options = new SyntheticDatasetOptions(Seed: 42, FileCount: 40);

        var reportA = SyntheticDataset.Generate(first, options);
        var reportB = SyntheticDataset.Generate(second, options);

        Assert.Equal(Inventory(first), Inventory(second));
        Assert.Equal(reportA.FileCount, reportB.FileCount);
        Assert.Equal(reportA.TotalBytes, reportB.TotalBytes);
    }

    [Fact]
    public void 不同种子生成不同布局()
    {
        var first = Path.Combine(_root, "a");
        var second = Path.Combine(_root, "b");

        SyntheticDataset.Generate(first, new SyntheticDatasetOptions(Seed: 1, FileCount: 40));
        SyntheticDataset.Generate(second, new SyntheticDatasetOptions(Seed: 2, FileCount: 40));

        Assert.NotEqual(Inventory(first), Inventory(second));
    }

    [Fact]
    public void 报告计数与实际目录树一致()
    {
        var root = Path.Combine(_root, "counted");
        var report = SyntheticDataset.Generate(root, new SyntheticDatasetOptions(Seed: 7, FileCount: 25, DirectoryDepth: 3, Branching: 2));

        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count();
        var directories = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Count();
        Assert.Equal(report.FileCount, files);
        Assert.Equal(25, files);
        Assert.Equal(report.DirectoryCount, directories);
        Assert.Equal(report.TotalBytes, Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length));
        Assert.True(report.TotalBytes > 0);
    }

    [Fact]
    public void 重复组文件内容逐字节相同()
    {
        var root = Path.Combine(_root, "dup");
        var report = SyntheticDataset.Generate(root, new SyntheticDatasetOptions(Seed: 9, FileCount: 30, DuplicateGroups: 2));

        Assert.Equal(2, report.DuplicateGroups.Count);
        foreach (var group in report.DuplicateGroups)
        {
            var hashes = group.Select(File.ReadAllBytes).Select(bytes => Convert.ToHexString(SHA256.HashData(bytes))).Distinct().ToList();
            Assert.Single(hashes);
            Assert.True(group.Count >= 2, "每组至少两个副本");
        }
    }

    [Fact]
    public void 目录深度不超过设定()
    {
        var root = Path.Combine(_root, "deep");
        SyntheticDataset.Generate(root, new SyntheticDatasetOptions(Seed: 3, FileCount: 30, DirectoryDepth: 3, Branching: 2));

        var deepest = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
            .Max(d => d.Substring(root.Length).Count(Path.DirectorySeparatorChar));
        Assert.True(deepest <= 3, $"实际最深 {deepest} 层，超过设定 3 层");
    }
}
