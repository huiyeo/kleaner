using System.ComponentModel;
using Kleaner.Core;

namespace Kleaner.App;

/// <summary>规则行视图模型：呈现扫描结果（文件数/可释放量），勾选状态驱动清理。</summary>
public sealed class RuleRow : INotifyPropertyChanged
{
    public RuleRow(Rule rule) => Rule = rule;

    public Rule Rule { get; }

    public string Id => Rule.Id;

    public string Name => Rule.Name;

    public string CategoryDisplay => RuleSetLoader.CategoryKey(Rule.Category);

    public string RiskDisplay => Rule.Risk == RiskLevel.Low ? S.Get("RiskLow") : S.Get("RiskMedium");

    public string ElevatedDisplay => Rule.RequiresElevation ? S.Get("Yes") : S.Get("No");

    public string SafetyNotes => Rule.SafetyNotes;

    public RuleScanResult? Result { get; private set; }

    public int FileCount => Result?.FileCount ?? 0;

    public string SizeDisplay => Helpers.FormatBytes(Result?.TotalBytes ?? 0);

    public string Note => Result?.Note ?? string.Empty;

    private bool _isSelected = true;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _isSelected = value;
            Raise(nameof(IsSelected));
        }
    }

    public void Apply(RuleScanResult result)
    {
        Result = result;
        Raise(nameof(FileCount));
        Raise(nameof(SizeDisplay));
        Raise(nameof(Note));
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public event PropertyChangedEventHandler? PropertyChanged;
}
