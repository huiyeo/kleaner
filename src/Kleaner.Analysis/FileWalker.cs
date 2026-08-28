namespace Kleaner.Analysis;

/// <summary>共享的只读文件遍历：迭代根目录下全部普通文件（跳过 reparse point 与不可访问目录），支持取消。</summary>
public static class FileWalker
{
    public static IEnumerable<string> EnumerateFiles(string root, CancellationToken token = default)
    {
        if (!Directory.Exists(root))
            yield break;

        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            token.ThrowIfCancellationRequested();
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
            {
                if (!IsReparsePoint(f))
                    yield return f;
            }
            foreach (var d in dirs)
            {
                if (!IsReparsePoint(d))
                    stack.Push(d);
            }
        }
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
