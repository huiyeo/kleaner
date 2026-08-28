using Kleaner.Core;
using Xunit;

namespace Kleaner.Core.Tests;

public sealed class GlobScannerTests : IDisposable
{
    private readonly string _root;

    public GlobScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kleaner-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch { }
    }

    private string W(string path) => path.Replace('/', '\\');

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
        Assert.True(re.IsMatch(W(tempRoot) + "\\sub\\a\\b.tmp"));
        Assert.True(re.IsMatch(W(tempRoot).ToUpperInvariant() + "\\SUB\\a\\b.tmp"));
        Assert.False(re.IsMatch(W(tempRoot) + "\\other\\a\\b.tmp"));
    }
}
