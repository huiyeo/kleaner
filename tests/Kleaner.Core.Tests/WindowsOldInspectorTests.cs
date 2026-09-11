using System.Diagnostics;
using Kleaner.SpecialOps;

namespace Kleaner.Core.Tests;

/// <summary>Windows.old 只读检测：存在性、reparse point 排除与有界占用测量。全部夹具使用 GUID 临时目录。</summary>
public sealed class WindowsOldInspectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kleaner-winold-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (!Directory.Exists(_root))
            return;
        // 退出时先移除链接本身再删树：Directory.Delete(recursive) 遇到联结会抛拒绝访问
        RemoveJunctions(_root);
        Directory.Delete(_root, recursive: true);
    }

    private static void RemoveJunctions(string directory)
    {
        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (new DirectoryInfo(child).Attributes.HasFlag(FileAttributes.ReparsePoint))
                Directory.Delete(child, recursive: false);
            else
                RemoveJunctions(child);
        }
    }

    [Fact]
    public void 不存在时报告未检测到()
    {
        Directory.CreateDirectory(_root);

        var info = WindowsOldInspector.Inspect(_root);

        Assert.False(info.Exists);
        Assert.Equal(Path.Combine(_root, "Windows.old"), info.Path);
    }

    [Fact]
    public void 存在时报告已检测到()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Windows.old"));

        var info = WindowsOldInspector.Inspect(_root);

        Assert.True(info.Exists);
    }

    [Fact]
    public void reparse_point_目录不当作安装残留()
    {
        // 引擎纪律：reparse point 一律排除。联结由本测试 GUID 临时目录构造；
        // 用 cmd 而非 powershell——后者 5.1 冷启动在 CI runner 上会撞超时（见 QuarantineManifestTests）。
        var real = Path.Combine(_root, "real-dir");
        Directory.CreateDirectory(real);
        var link = Path.Combine(_root, "Windows.old");
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = $"/d /c mklink /J \"{link}\" \"{real}\""
        };
        using var process = Process.Start(start)!;
        Assert.True(process.WaitForExit(10000), "测试目录联结创建超时");
        Assert.Equal(0, process.ExitCode);

        var info = WindowsOldInspector.Inspect(_root);

        Assert.False(info.Exists);
    }

    [Fact]
    public void 测量统计全部文件并在上限内不截断()
    {
        var dir = Path.Combine(_root, "Windows.old", "sub");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(_root, "Windows.old", "a.bin"), new string('a', 100));
        File.WriteAllText(Path.Combine(dir, "b.bin"), new string('b', 200));
        File.WriteAllText(Path.Combine(dir, "c.bin"), new string('c', 3000));

        var report = WindowsOldInspector.Measure(Path.Combine(_root, "Windows.old"), CancellationToken.None);

        Assert.Equal(3300, report.Bytes);
        Assert.Equal(3, report.FileCount);
        Assert.False(report.Truncated);
    }

    [Fact]
    public void 测量在上限处截断并明确标记()
    {
        var dir = Path.Combine(_root, "Windows.old");
        Directory.CreateDirectory(dir);
        for (var i = 0; i < 5; i++)
            File.WriteAllText(Path.Combine(dir, $"f{i}.bin"), "data");

        var report = WindowsOldInspector.Measure(dir, CancellationToken.None, maxFiles: 3);

        Assert.Equal(3, report.FileCount);
        Assert.True(report.Truncated);
    }

    [Fact]
    public void 测量跳过_reparse_point_子树()
    {
        // Windows.old 内的联结指向外部目录；外部文件不计入占用（联结目标不是安装残留的一部分）
        var dir = Path.Combine(_root, "Windows.old");
        var external = Path.Combine(_root, "external");
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(external);
        File.WriteAllText(Path.Combine(dir, "a.bin"), new string('a', 10));
        File.WriteAllText(Path.Combine(external, "b.bin"), new string('b', 5000));
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = $"/d /c mklink /J \"{Path.Combine(dir, "link")}\" \"{external}\""
        };
        using var process = Process.Start(start)!;
        Assert.True(process.WaitForExit(10000), "测试目录联结创建超时");
        Assert.Equal(0, process.ExitCode);

        var report = WindowsOldInspector.Measure(dir, CancellationToken.None);

        Assert.Equal(10, report.Bytes);
        Assert.Equal(1, report.FileCount);
        Assert.False(report.Truncated);
    }
}
