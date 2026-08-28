using System.Text;
using System.Text.RegularExpressions;

namespace Kleaner.Core;

/// <summary>含通配符路径模式的枚举与匹配：* 匹配单段内任意字符，** 匹配任意层级（含零层）；支持 %VAR% 环境变量；一律跳过 reparse point（OneDrive/云盘占位、junction）。</summary>
public static class GlobScanner
{
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
        var normalized = Normalize(pattern);
        var segments = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var firstWild = Array.FindIndex(segments, s => s.Contains('*'));
        if (firstWild < 0)
        {
            if (File.Exists(normalized) && !IsReparsePoint(normalized))
                yield return normalized;
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

    private static IEnumerable<string> Match(string dir, string[] segments, int index)
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
                string[] files;
                try { files = Directory.GetFiles(dir); }
                catch { files = Array.Empty<string>(); }
                foreach (var f in files)
                    if (re.IsMatch(Path.GetFileName(f)) && !IsReparsePoint(f))
                        yield return f;
            }
            else
            {
                var p = Path.Combine(dir, segment);
                if (File.Exists(p) && !IsReparsePoint(p))
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
            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch { subs = Array.Empty<string>(); }
            foreach (var s in subs)
            {
                if (IsReparsePoint(s) || !re.IsMatch(Path.GetFileName(s)))
                    continue;
                foreach (var f in Match(s, segments, index + 1))
                    yield return f;
            }
        }
        else
        {
            var p = Path.Combine(dir, segment);
            if (Directory.Exists(p) && !IsReparsePoint(p))
                foreach (var f in Match(p, segments, index + 1))
                    yield return f;
        }
    }

    private static IEnumerable<string> AllFilesRecursive(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            string[] files;
            string[] dirs;
            try
            {
                files = Directory.GetFiles(dir);
                dirs = Directory.GetDirectories(dir);
            }
            catch
            {
                continue;
            }
            foreach (var f in files)
                if (!IsReparsePoint(f))
                    yield return f;
            foreach (var d in dirs)
                if (!IsReparsePoint(d))
                    stack.Push(d);
        }
    }

    private static IEnumerable<string> AllDirsRecursive(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            string[] dirs;
            try { dirs = Directory.GetDirectories(dir); }
            catch { continue; }
            foreach (var d in dirs)
            {
                if (IsReparsePoint(d))
                    continue;
                yield return d;
                stack.Push(d);
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
