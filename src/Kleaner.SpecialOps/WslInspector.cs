namespace Kleaner.SpecialOps;

public sealed record VhdxInfo(string Path, long SizeBytes);

/// <summary>WSL 虚拟磁盘检测：只读发现 ext4.vhdx 并给出压缩指引（引导操作，不由工具直接执行删除）。</summary>
public static class WslInspector
{
    public static IReadOnlyList<VhdxInfo> DetectVhdx()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roots = new[]
        {
            Path.Combine(local, "wsl"),
            Path.Combine(local, "Packages"),
        };

        var found = new List<VhdxInfo>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                string[] entries;
                try { entries = Directory.GetFileSystemEntries(dir); }
                catch { continue; }
                foreach (var entry in entries)
                {
                    try
                    {
                        if (File.GetAttributes(entry).HasFlag(FileAttributes.Directory))
                        {
                            if (!File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint))
                                stack.Push(entry);
                        }
                        else if (entry.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase))
                        {
                            found.Add(new VhdxInfo(entry, new FileInfo(entry).Length));
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }
        return found.OrderByDescending(v => v.SizeBytes).ToList();
    }

    /// <summary>生成压缩指引文本（用户手动以管理员执行；压缩会中断运行中的 WSL 会话）。</summary>
    public static string BuildCompactGuide(VhdxInfo vhdx)
    {
        return $@"目标文件：{vhdx.Path}（当前大小 {vhdx.SizeBytes / 1024.0 / 1024.0 / 1024.0:F2} GB）

操作步骤（在管理员 PowerShell 中依次执行）：
1. wsl --shutdown          # 中断所有运行中的 WSL 会话，请先保存工作
2. 选择其一压缩虚拟磁盘：
   方式 A（需要 Hyper-V 模块）：
     Optimize-VHD -Path ""{vhdx.Path}"" -Mode Full
   方式 B（系统自带 diskpart，通用性最好）：
     diskpart
     进入 diskpart 后逐行输入：
       select vdisk file=""{vhdx.Path}""
       compact vdisk
       exit

说明：仅回收虚拟磁盘内部已释放的空间，不删除 WSL 内的任何数据；WSL 内磁盘占用越大，压缩后文件缩减越有限。";
    }
}
