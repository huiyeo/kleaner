namespace Kleaner.SpecialOps;

public sealed record WindowsOldInfo(string Path, bool Exists);

public sealed record WindowsOldSizeReport(long Bytes, long FileCount, bool Truncated);

/// <summary>
/// Windows.old 旧系统安装残留的只读检测。宪法第 8 条：删除即丢失系统回滚能力，属高风险系统项——
/// Kleaner 只做存在性检测与占用测量，删除一律引导 Windows 官方路径（设置-存储 / 磁盘清理），
/// 自身永不提供删除入口、不把该目录纳入任何规则或清理计划。
/// </summary>
public static class WindowsOldInspector
{
    public const string FolderName = "Windows.old";

    /// <summary>存在性检测：零遍历。reparse point 一律排除（与扫描引擎同一纪律）——联结或占位目录不当作安装残留。</summary>
    public static WindowsOldInfo Inspect(string systemDriveRoot)
    {
        var path = Path.Combine(systemDriveRoot, FolderName);
        var dir = new DirectoryInfo(path);
        // DirectoryInfo.Exists 出错时返回 false 不抛出；Exists 为 true 时 Attributes 已随探测取回
        var exists = dir.Exists && !dir.Attributes.HasFlag(FileAttributes.ReparsePoint);
        return new WindowsOldInfo(path, exists);
    }

    /// <summary>
    /// 有界占用测量：流式递归枚举，超过 maxFiles 即截断并显式标记（遍历与结果的有界纪律）。
    /// 排除 reparse point 子树（联结指向的内容不是安装残留的一部分）；不可访问条目跳过。
    /// 只读，调用方应在后台线程执行并允许取消。
    /// </summary>
    public static WindowsOldSizeReport Measure(string windowsOldPath, CancellationToken token, int maxFiles = 200_000)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false,
        };
        long bytes = 0;
        long count = 0;
        foreach (var file in new DirectoryInfo(windowsOldPath).EnumerateFiles("*", options))
        {
            token.ThrowIfCancellationRequested();
            if (count >= maxFiles)
                return new WindowsOldSizeReport(bytes, count, Truncated: true);
            bytes += file.Length;
            count++;
        }
        return new WindowsOldSizeReport(bytes, count, Truncated: false);
    }
}
