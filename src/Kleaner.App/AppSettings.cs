using System.IO;
using System.Text.Json;
using Kleaner.Executor;

namespace Kleaner.App;

public sealed class AppSettings
{
    public string? QuarantineRoot { get; set; }

    // 规则更新的 URL 与摘要不再属于设置：官方源与公钥内嵌于 RuleTrust（工单 12）。
    // 旧 settings.json 里的 RuleUpdateUrl/RuleUpdateSha512 字段在加载时被静默忽略。

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
