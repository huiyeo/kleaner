using CommunityToolkit.Mvvm.ComponentModel;
using Kleaner.Core;

namespace Kleaner.App;

/// <summary>规则行视图模型：呈现扫描结果（文件数/可释放量），勾选状态驱动清理。</summary>
public sealed partial class RuleRow : ObservableObject
{
    public RuleRow(Rule rule)
    {
        Rule = rule;
        IsSelected = MachineVerified;
    }

    public Rule Rule { get; }

    /// <summary>默认选择统一由 Core 的验证状态策略决定，缺失状态一律按未验证处理。</summary>
    public bool MachineVerified => RuleSelectionPolicy.IsDefaultSelectable(Rule);

    public string Id => Rule.Id;

    public string Name => Rule.Name;

    public string CategoryDisplay => RuleSetLoader.CategoryKey(Rule.Category);

    public string RiskDisplay => Rule.Risk == RiskLevel.Low ? S.Get("RiskLow") : S.Get("RiskMedium");

    public string ElevatedDisplay => Rule.RequiresElevation ? S.Get("Yes") : S.Get("No");

    public string SafetyNotes => Rule.SafetyNotes;

    public RuleScanResult? Result { get; private set; }

    public int FileCount => Result?.FileCount ?? 0;

    public string SizeDisplay => Helpers.FormatBytes(Result?.TotalBytes ?? 0);

    public string Note => Result?.Note ?? (MachineVerified ? string.Empty : S.Get("UnverifiedNote"));

    [ObservableProperty]
    private bool isSelected;

    public void Apply(RuleScanResult result)
    {
        Result = result;
        OnPropertyChanged(nameof(FileCount));
        OnPropertyChanged(nameof(SizeDisplay));
        OnPropertyChanged(nameof(Note));
    }
}
