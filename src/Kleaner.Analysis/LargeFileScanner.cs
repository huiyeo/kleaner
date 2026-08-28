namespace Kleaner.Analysis;

public sealed record LargeFileItem(string Path, long SizeBytes, DateTime LastWriteTimeUtc);

/// <summary>大文件扫描：只读列出根目录下超过阈值的文件，按大小降序。是否处理完全由人工勾选决定，删除走隔离区。</summary>
public static class LargeFileScanner
{
    public static IReadOnlyList<LargeFileItem> Scan(
        string root, long minBytes, int top, CancellationToken token = default)
    {
        var list = new List<LargeFileItem>();
        foreach (var path in FileWalker.EnumerateFiles(root, token))
        {
            try
            {
                var fi = new FileInfo(path);
                if (fi.Length >= minBytes)
                    list.Add(new LargeFileItem(path, fi.Length, fi.LastWriteTimeUtc));
            }
            catch
            {
            }
        }
        return list.OrderByDescending(f => f.SizeBytes).Take(top).ToList();
    }
}
