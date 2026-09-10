namespace Kleaner.Core;

/// <summary>默认勾选策略：决定扫描结果呈现时哪些规则默认勾选。</summary>
public static class RuleSelectionPolicy
{
    /// <summary>
    /// 只有明确以「本机实测」开头的规则才默认勾选。
    /// 缺失、空白或其他验证状态均按未验证处理，避免规则字段遗漏时静默放宽默认清理范围。
    /// 已撤回（Deprecated）的规则一票否决——撤回强于任何验证状态。
    /// </summary>
    public static bool IsDefaultSelectable(Rule rule) =>
        !rule.Deprecated &&
        !string.IsNullOrWhiteSpace(rule.Verified) &&
        rule.Verified.StartsWith("本机实测", StringComparison.Ordinal);
}
