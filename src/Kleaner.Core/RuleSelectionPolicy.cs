namespace Kleaner.Core;

/// <summary>默认勾选策略：决定扫描结果呈现时哪些规则默认勾选。</summary>
public static class RuleSelectionPolicy
{
    /// <summary>
    /// verified 以「本机实测」开头的规则视为已在本机验证，默认勾选；
    /// 未声明 verified 的旧规则视同已验证，同样默认勾选。
    /// （保留既有“仅机器验证规则默认选中”的安全语义，供 Web UI 复用。）
    /// </summary>
    public static bool IsDefaultSelectable(Rule rule) =>
        rule.Verified?.StartsWith("本机实测", StringComparison.Ordinal) ?? true;
}
