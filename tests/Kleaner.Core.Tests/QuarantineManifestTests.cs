using System.Text.Json;
using Kleaner.Executor;

namespace Kleaner.Core.Tests;

public sealed class QuarantineManifestTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kleaner-manifest-" + Guid.NewGuid().ToString("N"));

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

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
