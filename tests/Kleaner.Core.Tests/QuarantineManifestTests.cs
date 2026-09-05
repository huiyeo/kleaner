using System.Text.Json;
using System.Diagnostics;
using Kleaner.Executor;

namespace Kleaner.Core.Tests;

public sealed class QuarantineManifestTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kleaner-manifest-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _junctions = new();

    public QuarantineManifestTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("outside")]
    [InlineData("sibling")]
    [InlineData("mapping")]
    [InlineData("batch-id")]
    public void 非法清单在移动任何文件前拒绝(string scenario)
    {
        var manager = new QuarantineManager(Path.Combine(_root, "quarantine"), new HistoryManager(Path.Combine(_root, "history.jsonl")));
        var batchDir = Path.Combine(manager.Root, "batch");
        Directory.CreateDirectory(batchDir);
        var original = Path.Combine(_root, "restored.txt");
        var source = scenario switch
        {
            "outside" => Path.Combine(_root, "outside.txt"),
            "sibling" => Path.Combine(manager.Root, "batch-other", "outside.txt"),
            "mapping" => Path.Combine(batchDir, "wrong.txt"),
            _ => Path.Combine(batchDir, original[..1], original[3..])
        };
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "must-stay");
        var validOriginal = Path.Combine(_root, "valid.txt");
        var validSource = Path.Combine(batchDir, validOriginal[..1], validOriginal[3..]);
        Directory.CreateDirectory(Path.GetDirectoryName(validSource)!);
        File.WriteAllText(validSource, "valid-data");
        var batch = new QuarantineBatch(scenario == "batch-id" ? "other" : "batch", DateTime.UtcNow,
            new[]
            {
                new QuarantineEntry(validOriginal, validSource, 10, "rule", "moved"),
                new QuarantineEntry(original, source, 9, "rule", "moved")
            });
        File.WriteAllText(Path.Combine(batchDir, "manifest.json"), JsonSerializer.Serialize(batch,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        Assert.Throws<InvalidDataException>(() => manager.RestoreBatch("batch"));
        Assert.Equal("must-stay", File.ReadAllText(source));
        Assert.False(File.Exists(original));
        Assert.False(File.Exists(validOriginal));
        Assert.Equal("valid-data", File.ReadAllText(validSource));
        Assert.True(File.Exists(Path.Combine(batchDir, "manifest.json")));
    }

    [Theory]
    [InlineData("batch")]
    [InlineData("source")]
    [InlineData("target")]
    public void 还原拒绝路径中的目录联结(string scenario)
    {
        var manager = new QuarantineManager(Path.Combine(_root, "q"), new HistoryManager(Path.Combine(_root, "history.jsonl")));
        var external = Path.Combine(_root, "external");
        Directory.CreateDirectory(external);
        var batchDir = Path.Combine(manager.Root, "batch");
        if (scenario == "batch") CreateJunction(batchDir, external);
        else Directory.CreateDirectory(batchDir);
        var targetDirectory = Path.Combine(_root, "target");
        if (scenario == "target") CreateJunction(targetDirectory, external);
        else Directory.CreateDirectory(targetDirectory);
        var original = Path.Combine(targetDirectory, "restore.txt");
        var source = Path.Combine(batchDir, original[..1], original[3..]);
        var sourceDirectory = Path.GetDirectoryName(source)!;
        if (scenario == "source")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sourceDirectory)!);
            CreateJunction(sourceDirectory, external);
        }
        else Directory.CreateDirectory(sourceDirectory);
        File.WriteAllText(source, "keep-content");
        File.WriteAllText(Path.Combine(batchDir, "manifest.json"), JsonSerializer.Serialize(
            new QuarantineBatch("batch", DateTime.UtcNow, new[] { new QuarantineEntry(original, source, 12, "rule", "moved") }),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        Assert.Throws<InvalidDataException>(() => manager.RestoreBatch("batch"));
        Assert.Equal("keep-content", File.ReadAllText(source));
        Assert.False(File.Exists(original));
        Assert.True(File.Exists(Path.Combine(batchDir, "manifest.json")));
    }

    private void CreateJunction(string path, string target)
    {
        // 所有链接及目标均由本测试的 GUID 临时目录构造；退出时先移除链接本身。
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add($"$ErrorActionPreference = 'Stop'; New-Item -ItemType Junction -Path '{path.Replace("'", "''")}' -Target '{target.Replace("'", "''")}' | Out-Null");
        using var process = Process.Start(start)!;
        if (!process.WaitForExit(10000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("测试目录联结创建超时");
        }
        Assert.Equal(0, process.ExitCode);
        _junctions.Add(path);
        Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint));
    }

    [Fact]
    public void 拒绝通过目录联结初始化隔离区()
    {
        var external = Path.Combine(_root, "external-root");
        Directory.CreateDirectory(external);
        var link = Path.Combine(_root, "root-link");
        CreateJunction(link, external);

        Assert.Throws<InvalidDataException>(() => new QuarantineManager(Path.Combine(link, "new-quarantine"),
            new HistoryManager(Path.Combine(_root, "history.jsonl"))));
        Assert.False(Directory.Exists(Path.Combine(external, "new-quarantine")));
    }

    [Fact]
    public void 隔离区根目录被替换后拒绝清空外部批次()
    {
        var manager = new QuarantineManager(Path.Combine(_root, "q"), new HistoryManager(Path.Combine(_root, "history.jsonl")));
        var external = Path.Combine(_root, "external-root");
        var externalBatch = Path.Combine(external, "batch");
        Directory.CreateDirectory(externalBatch);
        var file = Path.Combine(externalBatch, "keep.txt");
        File.WriteAllText(file, "keep");
        Directory.Move(manager.Root, Path.Combine(_root, "original-q"));
        CreateJunction(manager.Root, external);

        Assert.Throws<InvalidDataException>(() => manager.DeleteBatch("batch"));
        Assert.Throws<InvalidDataException>(() => manager.ListBatches());
        Assert.Equal("keep", File.ReadAllText(file));
    }

    public void Dispose()
    {
        foreach (var junction in _junctions.AsEnumerable().Reverse())
            Directory.Delete(junction, recursive: false);
        Directory.Delete(_root, recursive: true);
    }
}
