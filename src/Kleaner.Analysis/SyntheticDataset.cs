namespace Kleaner.Analysis;

/// <summary>合成数据集参数；全部字段参与复现，同参数同种子必须产出逐字节相同的目录树。</summary>
public sealed record SyntheticDatasetOptions(
    int Seed,
    int FileCount,
    int DirectoryDepth = 3,
    int Branching = 3,
    int DuplicateGroups = 2,
    long MaxFileBytes = 512 * 1024)
{
    public int MinFileBytes => 64;
}

public sealed record SyntheticDatasetReport(
    int FileCount,
    long TotalBytes,
    int DirectoryCount,
    IReadOnlyList<IReadOnlyList<string>> DuplicateGroups);

/// <summary>
/// 性能基准用的确定性合成数据集。布局规则固定：目录树按广度优先展开到 DirectoryDepth 层，
/// 文件按序号轮转落入各目录；大小分布固定为 70% 小（64B–16KB）、25% 中（16KB–128KB）、
/// 5% 大（128KB–MaxFileBytes）；前 DuplicateGroups 组文件共享逐字节相同的填充内容，
/// 用于重复文件哈希基准。随机源为固定种子的 Random，保证跨运行可复现。
/// </summary>
public static class SyntheticDataset
{
    public static SyntheticDatasetReport Generate(string root, SyntheticDatasetOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.FileCount < 1) throw new ArgumentException("FileCount 至少为 1", nameof(options));
        if (options.DirectoryDepth < 1) throw new ArgumentException("DirectoryDepth 至少为 1", nameof(options));
        if (options.Branching < 1) throw new ArgumentException("Branching 至少为 1", nameof(options));
        Directory.CreateDirectory(root);

        var random = new Random(options.Seed);
        var directories = BuildDirectories(root, options);

        // 重复组占用最靠前的序号，副本分散到不同目录。
        var duplicateFiles = new List<IReadOnlyList<string>>();
        var duplicateCount = Math.Min(options.DuplicateGroups * 3, options.FileCount);
        var duplicateIndex = 0;
        var totalBytes = 0L;
        for (var group = 0; group < options.DuplicateGroups && duplicateIndex < duplicateCount; group++)
        {
            var content = new byte[SizeFor(random, options, bucket: group % 3)];
            random.NextBytes(content);
            var copies = new List<string>();
            for (var copy = 0; copy < 3 && duplicateIndex < duplicateCount; copy++, duplicateIndex++)
            {
                var path = Path.Combine(directories[duplicateIndex % directories.Count], $"file-{duplicateIndex:D6}.bin");
                File.WriteAllBytes(path, content);
                totalBytes += content.LongLength;
                copies.Add(path);
            }
            duplicateFiles.Add(copies);
        }

        for (var index = duplicateIndex; index < options.FileCount; index++)
        {
            var size = SizeFor(random, options, bucket: random.Next(100));
            var content = new byte[size];
            random.NextBytes(content);
            var path = Path.Combine(directories[index % directories.Count], $"file-{index:D6}.bin");
            File.WriteAllBytes(path, content);
            totalBytes += size;
        }

        return new SyntheticDatasetReport(
            FileCount: options.FileCount,
            TotalBytes: totalBytes,
            DirectoryCount: directories.Count - 1, // 不计根目录
            DuplicateGroups: duplicateFiles);
    }

    // 70% 小、25% 中、5% 大；重复组按 bucket 直接选定档位，保证组内一致。
    // Next 的上界 exclusive 且必须大于下界，档位上界做防御性夹取。
    private static long SizeFor(Random random, SyntheticDatasetOptions options, int bucket)
    {
        var min = options.MinFileBytes;
        var max = (int)Math.Max(options.MaxFileBytes, min + 1);
        var (low, high) = bucket switch
        {
            0 => (min, Math.Min(16 * 1024, max)),
            1 => (Math.Min(16 * 1024, max - 1), Math.Min(128 * 1024, max)),
            _ => (Math.Min(128 * 1024, max - 1), max),
        };
        if (high <= low) return low;
        return random.Next(low, high);
    }

    private static List<string> BuildDirectories(string root, SyntheticDatasetOptions options)
    {
        var directories = new List<string> { root };
        var frontier = new List<string> { root };
        for (var depth = 1; depth <= options.DirectoryDepth; depth++)
        {
            var next = new List<string>();
            foreach (var parent in frontier)
            {
                for (var branch = 0; branch < options.Branching; branch++)
                {
                    var child = Path.Combine(parent, $"dir-{depth:D2}-{branch:D2}");
                    Directory.CreateDirectory(child);
                    next.Add(child);
                }
            }
            directories.AddRange(next);
            frontier = next;
        }
        return directories;
    }
}
