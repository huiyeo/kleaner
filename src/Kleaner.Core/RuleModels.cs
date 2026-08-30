namespace Kleaner.Core;

public enum RuleCategory { Temp, BrowserCache, DevCache, Updater, System, Application }

public enum RiskLevel { Low, Medium }

public sealed record RuleSet(
    int SchemaVersion,
    IReadOnlyDictionary<string, int>? AgeDaysByCategory,
    int? DefaultAgeDays,
    IReadOnlyList<Rule> Rules);

public sealed record Rule(
    string Id,
    string Name,
    RuleCategory Category,
    RiskLevel Risk,
    IReadOnlyList<string> Paths,
    IReadOnlyList<string> Exclude,
    int? AgeDays,
    int? KeepNewest,
    bool RequiresElevation,
    bool Enabled,
    string SafetyNotes,
    string? SafetyDoc = null,
    string? Verified = null);
