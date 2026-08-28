namespace Kleaner.Analysis;

public sealed record UsageItem(string Path, long SizeBytes, bool IsDirectory);

/// <summary>空间分析：计算根目录各一级子项（目录含全部后代）的占用，按大小降序。只读。</summary>
public static class DiskUsageAnalyzer
{
    public static IReadOnlyList<UsageItem> TopLevel(string root, CancellationToken token = default)
    {
        if (!Directory.Exists(root))
            return Array.Empty<UsageItem>();

        var items = new List<UsageItem>();
        string[] entries;
        try
        {
            entries = Directory.GetFileSystemEntries(root);
        }
        catch
        {
            return items;
        }

        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (FileWalker.IsReparsePoint(entry))
                    continue; // 云盘占位/junction 大小无意义，跳过
                if (File.Exists(entry))
                {
                    items.Add(new UsageItem(entry, new FileInfo(entry).Length, IsDirectory: false));
                }
                else
                {
                    long total = 0;
                    foreach (var file in FileWalker.EnumerateFiles(entry, token))
                    {
                        try
                        {
                            total += new FileInfo(file).Length;
                        }
                        catch
                        {
                        }
                    }
                    items.Add(new UsageItem(entry, total, IsDirectory: true));
                }
            }
            catch
            {
            }
        }

        return items.OrderByDescending(i => i.SizeBytes).ToList();
    }
}
