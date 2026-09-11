using System.Text;
using System.Text.RegularExpressions;

namespace Kleaner.Core;

/// <summary>含通配符路径模式的枚举与匹配：* 匹配单段内任意字符，** 匹配任意层级（含零层）；支持 %VAR% 环境变量；一律跳过 reparse point（OneDrive/云盘占位、junction）。</summary>
public static class GlobScanner
{
    // 单趟枚举的两个关键点：属性直接取自查找数据（不再对每个条目补 GetAttributes），
    // reparse point 由枚举器排除且不深入。AttributesToSkip 只排除 reparse——隐藏/系统文件
    // 历史上一直参与匹配，不能落到默认值（默认会额外排除 Hidden|System）。
    private static readonly EnumerationOptions FlatOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false
    };

    private static readonly EnumerationOptions RecursiveOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false
    };

    public static string Normalize(string pattern) =>
        Environment.ExpandEnvironmentVariables(pattern).Replace('/', '\\');

    /// <summary>将路径模式编译为整路径正则（大小写不敏感），供 exclude 等整路径匹配使用。</summary>
    public static Regex ToRegex(string pattern)
    {
        var normalized = Normalize(pattern);
        var sb = new StringBuilder("^");
        for (var i = 0; i < normalized.Length; i++)
        {
            var ch = normalized[i];
            if (ch == '*')
            {
                if (i + 1 < normalized.Length && normalized[i + 1] == '*')
                {
                    sb.Append(".*");
                    i++;
                }
                else
                {
                    sb.Append("[^\\\\]*");
                }
            }
            else if (ch == '\\')
            {
                sb.Append("\\\\");
            }
            else
            {
                sb.Append(Regex.Escape(ch.ToString()));
            }
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    /// <summary>按模式枚举磁盘上的文件。模式必须是以环境变量或盘符开头的绝对路径。</summary>
    public static IEnumerable<string> EnumerateFiles(string pattern)
    {
        foreach (var file in EnumerateFileInfos(pattern))
            yield return file.FullName;
    }

    /// <summary>同 <see cref="EnumerateFiles"/>，但返回枚举条目本身：Length / LastWriteTimeUtc 取自查找数据缓存，调用方无需再补 stat。</summary>
    public static IEnumerable<FileInfo> EnumerateFileInfos(string pattern)
    {
        var normalized = Normalize(pattern);
        var segments = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var firstWild = Array.FindIndex(segments, s => s.Contains('*'));
        if (firstWild < 0)
        {
            var exact = new FileInfo(normalized);
            if (exact.Exists && !exact.Attributes.HasFlag(FileAttributes.ReparsePoint))
                yield return exact;
            yield break;
        }
        if (firstWild == 0)
            throw new FormatException($"模式必须以环境变量或盘符开头的绝对路径：{pattern}");

        var startDir = string.Join("\\", segments, 0, firstWild);
        if (!Directory.Exists(startDir))
            yield break;

        foreach (var file in Match(startDir, segments, firstWild))
            yield return file;
    }

    /// <summary>单趟读出目录的直接子项并按引擎契约排除 reparse point 与不可访问目录。</summary>
    private static (List<FileInfo> Files, List<DirectoryInfo> Dirs) ReadChildren(string dir)
    {
        var files = new List<FileInfo>();
        var dirs = new List<DirectoryInfo>();
        foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", FlatOptions))
        {
            if (entry is DirectoryInfo d) dirs.Add(d);
            else files.Add((FileInfo)entry);
        }
        return (files, dirs);
    }

    private static IEnumerable<FileInfo> Match(string dir, string[] segments, int index)
    {
        var segment = segments[index];
        var isLast = index == segments.Length - 1;

        if (isLast)
        {
            if (segment == "**")
            {
                foreach (var f in AllFilesRecursive(dir))
                    yield return f;
            }
            else if (segment.Contains('*'))
            {
                var re = SegmentRegex(segment);
                foreach (var f in ReadChildren(dir).Files)
                    if (re.IsMatch(Path.GetFileName(f.FullName)))
                        yield return f;
            }
            else
            {
                var p = new FileInfo(Path.Combine(dir, segment));
                if (p.Exists && !p.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    yield return p;
            }
            yield break;
        }

        if (segment == "**")
        {
            // ** 覆盖零层及以上：先试当前目录，再试全部后代目录
            foreach (var f in Match(dir, segments, index + 1))
                yield return f;
            foreach (var sub in AllDirsRecursive(dir))
                foreach (var f in Match(sub, segments, index + 1))
                    yield return f;
        }
        else if (segment.Contains('*'))
        {
            var re = SegmentRegex(segment);
            foreach (var s in ReadChildren(dir).Dirs)
            {
                if (!re.IsMatch(Path.GetFileName(s.FullName)))
                    continue;
                foreach (var f in Match(s.FullName, segments, index + 1))
                    yield return f;
            }
        }
        else
        {
            var p = new DirectoryInfo(Path.Combine(dir, segment));
            if (p.Exists && !p.Attributes.HasFlag(FileAttributes.ReparsePoint))
                foreach (var f in Match(p.FullName, segments, index + 1))
                    yield return f;
        }
    }

    private static IEnumerable<FileInfo> AllFilesRecursive(string root)
    {
        foreach (var f in new DirectoryInfo(root).EnumerateFiles("*", RecursiveOptions))
            yield return f;
    }

    private static IEnumerable<string> AllDirsRecursive(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            foreach (var d in ReadChildren(dir).Dirs)
            {
                yield return d.FullName;
                stack.Push(d.FullName);
            }
        }
    }

    private static Regex SegmentRegex(string segment)
    {
        var sb = new StringBuilder("^");
        foreach (var ch in segment)
        {
            if (ch == '*')
                sb.Append("[^\\\\]*");
            else
                sb.Append(Regex.Escape(ch.ToString()));
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase);
    }

    public static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }
}
