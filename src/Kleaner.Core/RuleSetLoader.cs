using System.Text.Json;

namespace Kleaner.Core;

/// <summary>加载并校验白名单规则集。解析失败即抛异常，语义问题由 <see cref="Validate"/> 收集。</summary>
public static class RuleSetLoader
{
    public static RuleSet LoadFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var schemaVersion = root.GetProperty("schemaVersion").GetInt32();
        if (schemaVersion != 1)
            throw new FormatException($"不支持的 schemaVersion：{schemaVersion}（仅支持 1）");

        int? defaultAgeDays = null;
        IReadOnlyDictionary<string, int>? ageDaysByCategory = null;
        if (root.TryGetProperty("defaults", out var defaults))
        {
            if (defaults.TryGetProperty("ageDays", out var age))
                defaultAgeDays = age.GetInt32();
            if (defaults.TryGetProperty("ageDaysByCategory", out var byCategory))
            {
                var map = new Dictionary<string, int>();
                foreach (var p in byCategory.EnumerateObject())
                    map[p.Name] = p.Value.GetInt32();
                ageDaysByCategory = map;
            }
        }

        var rules = new List<Rule>();
        foreach (var element in root.GetProperty("rules").EnumerateArray())
            rules.Add(ParseRule(element));

        return new RuleSet(schemaVersion, ageDaysByCategory, defaultAgeDays, rules);
    }

    public static RuleSet LoadFromFile(string path) => LoadFromJson(File.ReadAllText(path));

    private static Rule ParseRule(JsonElement e)
    {
        var id = e.GetProperty("id").GetString()!;
        return new Rule(
            Id: id,
            Name: e.GetProperty("name").GetString()!,
            Category: ParseCategory(e.GetProperty("category").GetString()!, id),
            Risk: ParseRisk(e.GetProperty("risk").GetString()!, id),
            Paths: e.GetProperty("paths").EnumerateArray().Select(p => p.GetString()!).ToArray(),
            Exclude: e.TryGetProperty("exclude", out var ex)
                ? ex.EnumerateArray().Select(x => x.GetString()!).ToArray()
                : Array.Empty<string>(),
            AgeDays: TryGetNullableInt(e, "ageDays"),
            KeepNewest: TryGetNullableInt(e, "keepNewest"),
            RequiresElevation: e.GetProperty("requiresElevation").GetBoolean(),
            Enabled: e.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean(),
            SafetyNotes: e.GetProperty("safetyNotes").GetString()!,
            SafetyDoc: e.TryGetProperty("safetyDoc", out var sd) ? sd.GetString() : null);
    }

    private static int? TryGetNullableInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    private static RuleCategory ParseCategory(string s, string id) => s switch
    {
        "temp" => RuleCategory.Temp,
        "browser-cache" => RuleCategory.BrowserCache,
        "dev-cache" => RuleCategory.DevCache,
        "updater" => RuleCategory.Updater,
        "system" => RuleCategory.System,
        "application" => RuleCategory.Application,
        _ => throw new FormatException($"规则 {id} 的 category 非法：{s}")
    };

    private static RiskLevel ParseRisk(string s, string id) => s switch
    {
        "low" => RiskLevel.Low,
        "medium" => RiskLevel.Medium,
        _ => throw new FormatException($"规则 {id} 的 risk 非法：{s}")
    };

    /// <summary>规则级 ageDays → 分类默认 → 全局默认，逐级回退；设置 keepNewest 的规则豁免年龄阈值（按版本保留清理）。</summary>
    public static int? EffectiveAgeDays(this RuleSet set, Rule rule) =>
        rule.KeepNewest is not null
            ? null
            : rule.AgeDays
              ?? (set.AgeDaysByCategory is not null && set.AgeDaysByCategory.TryGetValue(CategoryKey(rule.Category), out var v) ? v : (int?)null)
              ?? set.DefaultAgeDays;

    public static string CategoryKey(RuleCategory category) => category switch
    {
        RuleCategory.Temp => "temp",
        RuleCategory.BrowserCache => "browser-cache",
        RuleCategory.DevCache => "dev-cache",
        RuleCategory.Updater => "updater",
        RuleCategory.System => "system",
        RuleCategory.Application => "application",
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };

    /// <summary>语义校验。任何规则缺年龄阈值且无 keepNewest 时按安全默认拒绝执行。</summary>
    public static IReadOnlyList<string> Validate(RuleSet set)
    {
        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in set.Rules)
        {
            if (!ids.Add(rule.Id))
                errors.Add($"规则 id 重复：{rule.Id}");
            if (rule.SafetyNotes is not { Length: >= 20 })
                errors.Add($"规则 {rule.Id} 的 safetyNotes 不足 20 字，必须文档化安全性依据");
            if (set.EffectiveAgeDays(rule) is null && rule.KeepNewest is null)
                errors.Add($"规则 {rule.Id} 既无年龄阈值也无 keepNewest，按安全默认拒绝执行");
            if (rule.Paths.Count == 0)
                errors.Add($"规则 {rule.Id} 的 paths 为空");
        }
        return errors;
    }
}
