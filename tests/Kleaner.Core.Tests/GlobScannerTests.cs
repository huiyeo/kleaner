using Kleaner.Core;
using Xunit;

namespace Kleaner.Core.Tests;

public sealed class GlobScannerTests : IDisposable
{
    private readonly string _root;
    private readonly List<string> _junctions = new();

    public GlobScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kleaner-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        // 先摘除联结本身再删根，避免递归删除沿联结伤到目标
        foreach (var junction in _junctions.AsEnumerable().Reverse())
        {
            try { Directory.Delete(junction, recursive: false); }
            catch { }
        }
        try { Directory.Delete(_root, true); }
        catch { }
    }

    private string W(string path) => path.Replace('/', '\\');

    private void CreateJunction(string path, string target)
    {
        // 与 QuarantineManifestTests 相同的创建方式；cmd 启动毫秒级，powershell 冷启动在 CI runner 上会超时
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = $"/d /c mklink /J \"{path}\" \"{target}\""
        };
        using var process = System.Diagnostics.Process.Start(start)!;
        if (!process.WaitForExit(10000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("测试目录联结创建超时");
        }
        Assert.Equal(0, process.ExitCode);
        _junctions.Add(path);
    }

    [Fact]
    public void 单层星号_只匹配本段()
    {
        var dir = Path.Combine(_root, "a");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "x.log"), "1");
        File.WriteAllText(Path.Combine(dir, "y.txt"), "2");

        var files = GlobScanner.EnumerateFiles(W(dir) + "\\*.log").ToList();

        Assert.Single(files);
        Assert.EndsWith("x.log", files[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 双层星号_递归匹配所有文件()
    {
        var dir = Path.Combine(_root, "b");
        Directory.CreateDirectory(Path.Combine(dir, "c", "d"));
        File.WriteAllText(Path.Combine(dir, "f1.txt"), "1");
        File.WriteAllText(Path.Combine(dir, "c", "f2.txt"), "2");
        File.WriteAllText(Path.Combine(dir, "c", "d", "f3.txt"), "3");

        var files = GlobScanner.EnumerateFiles(W(dir) + "\\**").ToList();

        Assert.Equal(3, files.Count);
    }

    [Fact]
    public void 中段星号_匹配目录名()
    {
        var baseDir = Path.Combine(_root, "profiles");
        Directory.CreateDirectory(Path.Combine(baseDir, "Default", "Cache"));
        Directory.CreateDirectory(Path.Combine(baseDir, "Profile 1", "Cache"));
        Directory.CreateDirectory(Path.Combine(baseDir, "keep", "Cache"));
        File.WriteAllText(Path.Combine(baseDir, "Default", "Cache", "a.bin"), "1");
        File.WriteAllText(Path.Combine(baseDir, "Profile 1", "Cache", "b.bin"), "2");
        File.WriteAllText(Path.Combine(baseDir, "keep", "Cache", "c.bin"), "3");

        var pattern = W(baseDir) + "\\*\\Cache\\**";
        var files = GlobScanner.EnumerateFiles(pattern).ToList();

        Assert.Equal(3, files.Count);
    }

    [Fact]
    public void 双层星号加扩展名_任意深度匹配()
    {
        var dir = Path.Combine(_root, "u");
        Directory.CreateDirectory(Path.Combine(dir, "pending"));
        File.WriteAllText(Path.Combine(dir, "config.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "app.exe"), "1");
        File.WriteAllText(Path.Combine(dir, "pending", "app-1.0.exe"), "2");
        File.WriteAllText(Path.Combine(dir, "pending", "app-2.0.exe"), "3");

        var files = GlobScanner.EnumerateFiles(W(dir) + "\\**\\*.exe").ToList();

        Assert.Equal(3, files.Count);
        Assert.DoesNotContain(files, f => f.EndsWith("config.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void 环境变量_正确展开()
    {
        var dir = Path.Combine(_root, "e");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "z.bin"), "1");
        Environment.SetEnvironmentVariable("KLEANER_TEST_VAR", dir, EnvironmentVariableTarget.Process);

        var files = GlobScanner.EnumerateFiles("%KLEANER_TEST_VAR%\\**").ToList();

        Assert.Single(files);
        Environment.SetEnvironmentVariable("KLEANER_TEST_VAR", null, EnvironmentVariableTarget.Process);
    }

    [Fact]
    public void ToRegex_整路径匹配与大小写不敏感()
    {
        var re = GlobScanner.ToRegex("%TEMP%\\sub\\**");
        var tempRoot = Environment.GetEnvironmentVariable("TEMP")!;
        Assert.Matches(re, W(tempRoot) + "\\sub\\a\\b.tmp");
        Assert.Matches(re, W(tempRoot).ToUpperInvariant() + "\\SUB\\a\\b.tmp");
        Assert.DoesNotMatch(re, W(tempRoot) + "\\other\\a\\b.tmp");
    }

    [Fact]
    public void 隐藏与系统文件_仍参与匹配()
    {
        // 枚举选项只允许排除 reparse point；若误落默认 AttributesToSkip，Hidden|System 会被静默过滤
        var dir = Path.Combine(_root, "attr");
        Directory.CreateDirectory(dir);
        var hidden = Path.Combine(dir, "hide.log");
        File.WriteAllText(hidden, "1");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        var system = Path.Combine(dir, "sys.log");
        File.WriteAllText(system, "2");
        File.SetAttributes(system, FileAttributes.System);

        var files = GlobScanner.EnumerateFileInfos(W(dir) + "\\*.log").ToList();

        Assert.Equal(2, files.Count);
    }

    [Fact]
    public void 目录联结_不返回也不深入()
    {
        var external = Path.Combine(_root, "outside");
        Directory.CreateDirectory(external);
        File.WriteAllText(Path.Combine(external, "secret.txt"), "keep");
        var dir = Path.Combine(_root, "scanroot");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "own.txt"), "1");
        CreateJunction(Path.Combine(dir, "link"), external);

        var files = GlobScanner.EnumerateFileInfos(W(dir) + "\\**").ToList();

        Assert.Single(files);
        Assert.EndsWith("own.txt", files[0].FullName, StringComparison.OrdinalIgnoreCase);
        Assert.True(files[0].Length > 0);
    }

    [Fact]
    public void 枚举条目_属性与磁盘一致()
    {
        var dir = Path.Combine(_root, "props");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "a.txt");
        File.WriteAllText(file, "0123456789");
        var expected = new FileInfo(file).LastWriteTimeUtc;

        var info = GlobScanner.EnumerateFileInfos(W(dir) + "\\*.txt").Single();

        Assert.Equal(10, info.Length);
        Assert.Equal(expected, info.LastWriteTimeUtc);
    }

    [Fact]
    public void TryGetStartDir_通配符模式返回前缀起始目录()
    {
        var dir = Path.Combine(_root, "start");
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("KLEANER_TEST_VAR", dir, EnvironmentVariableTarget.Process);
        try
        {
            // 语义：第一个通配符之前的完整前缀（与枚举起点一致）
            Assert.Equal(Path.Combine(dir, "sub"), GlobScanner.TryGetStartDir("%KLEANER_TEST_VAR%\\sub\\**"));
            Assert.Equal(dir, GlobScanner.TryGetStartDir("%KLEANER_TEST_VAR%\\*.log"));
            Assert.Equal(dir, GlobScanner.TryGetStartDir("%KLEANER_TEST_VAR%\\**"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("KLEANER_TEST_VAR", null, EnvironmentVariableTarget.Process);
        }
    }

    [Fact]
    public void TryGetStartDir_无通配符返回null()
    {
        Assert.Null(GlobScanner.TryGetStartDir("C:\\exact\\path\\file.bin"));
    }

    [Fact]
    public void TryGetStartDir_首段通配符抛格式异常()
    {
        Assert.Throws<FormatException>(() => GlobScanner.TryGetStartDir("*\\illegal"));
    }
}
