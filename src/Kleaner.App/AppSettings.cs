using System.IO;
using System.Text.Json;
using Kleaner.Executor;

namespace Kleaner.App;

public sealed class AppSettings
{
    public string? QuarantineRoot { get; set; }
    public string? RuleUpdateUrl { get; set; }
    public string? RuleUpdateSha512 { get; set; }

    public string EffectiveQuarantineRoot =>
        string.IsNullOrWhiteSpace(QuarantineRoot) ? QuarantineManager.DefaultRoot() : QuarantineRoot;

    private static string FilePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Kleaner", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath())) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath())!);
        File.WriteAllText(FilePath(), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
