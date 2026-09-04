namespace Kleaner.Core;

/// <summary>默认勾选策略：决定扫描结果呈现时哪些规则默认勾选。</summary>
public static class RuleSelectionPolicy
{
    /// <summary>
    /// 只有明确以「本机实测」开头的规则才默认勾选。
    /// 缺失、空白或其他验证状态均按未验证处理，避免规则字段遗漏时静默放宽默认清理范围。
    /// </summary>
    public static bool IsDefaultSelectable(Rule rule) =>
        !string.IsNullOrWhiteSpace(rule.Verified) &&
        rule.Verified.StartsWith("本机实测", StringComparison.Ordinal);
}
