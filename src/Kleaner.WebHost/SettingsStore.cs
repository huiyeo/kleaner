using System.Text.Json;

namespace Kleaner.WebHost;

/// <summary>settings.json 的三字段存储；保持既有磁盘契约。</summary>
public sealed record HostSettings(string? QuarantineRoot, string? RuleUpdateUrl, string? RuleUpdateSha512)
{
    public HostSettings Normalize() => this with
    {
        QuarantineRoot = NormalizeValue(QuarantineRoot),
        RuleUpdateUrl = NormalizeValue(RuleUpdateUrl),
        RuleUpdateSha512 = NormalizeValue(RuleUpdateSha512),
    };

    private static string? NormalizeValue(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal static class SettingsStore
{
    public static string FilePath(KleanerWebHostOptions options) =>
        options.SettingsFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Kleaner", "settings.json");

    public static HostSettings Load(KleanerWebHostOptions options)
    {
        try
        {
            return (JsonSerializer.Deserialize(File.ReadAllText(FilePath(options)), SettingsFileJsonContext.Default.HostSettings)
                ?? new HostSettings(null, null, null)).Normalize();
        }
        catch
        {
            // 文件缺失或损坏时回退默认值。
            return new HostSettings(null, null, null);
        }
    }

    public static void Save(KleanerWebHostOptions options, HostSettings settings)
    {
        var path = FilePath(options);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(
            settings.Normalize(), SettingsFileJsonContext.Default.HostSettings));
    }
}
