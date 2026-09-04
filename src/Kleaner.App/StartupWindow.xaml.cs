using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Kleaner.Executor;

namespace Kleaner.App;

/// <summary>启动项行视图模型：启用项与已禁用备份统一呈现。</summary>
public sealed class StartupRow : INotifyPropertyChanged
{
    public StartupRow(StartupItem item) => Item = item;

    public StartupRow(DisabledStartup disabled) => Disabled = disabled;

    public StartupItem? Item { get; }
    public DisabledStartup? Disabled { get; }

    public bool IsDisabled => Disabled is not null;

    public string Id => Item?.Id ?? Disabled!.Id;

    public string Name => Item?.Name ?? Disabled!.Name;

    public string Command => Item?.Command ?? Disabled!.Command;

    public string Location => Item?.Location ?? Disabled!.Location;

    public string TypeDisplay => IsDisabled
        ? (Disabled!.Kind == nameof(StartupKind.Registry)
            ? S.Get("StartupTypeRegistry")
            : S.Get("StartupTypeFile"))
        : Item!.Kind == StartupKind.Registry
            ? S.Get("StartupTypeRegistry")
            : S.Get("StartupTypeFile");

    public string ElevatedDisplay => (Item?.RequiresElevation ?? false) ? S.Get("Yes") : S.Get("No");

    public string StatusDisplay => IsDisabled ? S.Get("StartupDisabled") : S.Get("StartupEnabled");

    public string Note => Disabled?.DisabledUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? string.Empty;

    private bool _isSelected;

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

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class StartupWindow : Window
{
    private readonly ObservableCollection<StartupRow> _rows = new();
    private readonly StartupWindowCoordinator _coordinator;
    private string _summaryText = string.Empty;

    public StartupWindow()
        : this(new StartupManager(), new MessageBoxStartupWindowDialog(), Helpers.IsElevated)
    {
    }

    public StartupWindow(
        IStartupManager manager,
        IStartupWindowDialog dialog,
        Func<bool> isElevated)
    {
        InitializeComponent();
        LoadStrings();
        StartupGrid.ItemsSource = _rows;
        _coordinator = new StartupWindowCoordinator(manager, dialog, isElevated);
        Loaded += (_, _) => Reload();
    }

    private void LoadStrings()
    {
        Title = S.Get("StartupTitle");
        RefreshButton.Content = S.Get("BtnRefresh");
        DisableButton.Content = S.Get("BtnDisableSel");
        RestoreButton.Content = S.Get("BtnRestoreSel");
        var headers = new[]
        {
            S.Get("ColSelect"), S.Get("ColName"), S.Get("StartupColCommand"), S.Get("StartupColLocation"),
            S.Get("StartupColType"), S.Get("StartupColElevated"), S.Get("StartupColStatus"),
        };
        for (var i = 0; i < headers.Length && i < StartupGrid.Columns.Count; i++)
            StartupGrid.Columns[i].Header = headers[i];
    }

    private void Reload()
    {
        _rows.Clear();
        foreach (var row in _coordinator.LoadRows())
            _rows.Add(row);
        _summaryText = _coordinator.FormatSummary(_rows);
        StatusText.Text = _summaryText;
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Reload();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 选中已禁用备份行时展示禁用时间；其余恢复加载汇总
        var row = StartupGrid.SelectedItem as StartupRow;
        StatusText.Text = row is { IsDisabled: true }
            ? S.Format("StartupSelectedDisabled", row.Note)
            : _summaryText;
    }

    private void OnDisable(object sender, RoutedEventArgs e) =>
        Apply(_coordinator.DisableSelected(_rows));

    private void OnRestore(object sender, RoutedEventArgs e) =>
        Apply(_coordinator.RestoreSelected(_rows));

    private void Apply(StartupWindowOperationResult result)
    {
        if (result.Refresh)
            Reload();
        if (result.StatusText is not null)
            StatusText.Text = result.StatusText;
    }
}
