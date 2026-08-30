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
    private readonly StartupManager _manager;
    private string _summaryText = string.Empty;

    public StartupWindow()
    {
        InitializeComponent();
        LoadStrings();
        StartupGrid.ItemsSource = _rows;
        _manager = new StartupManager();
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
        foreach (var item in _manager.Enumerate())
            _rows.Add(new StartupRow(item));
        foreach (var disabled in _manager.ListDisabled())
            _rows.Add(new StartupRow(disabled));
        _summaryText = _rows.Count == 0
            ? S.Get("StartupNone")
            : S.Format("StartupStatusLoaded", _rows.Count(r => !r.IsDisabled), _rows.Count(r => r.IsDisabled));
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

    private void OnDisable(object sender, RoutedEventArgs e)
    {
        var targets = _rows.Where(r => r.IsSelected && !r.IsDisabled).ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show(S.Get("StartupNothingSelected"), Title);
            return;
        }
        if (targets.Any(t => t.Item!.RequiresElevation) && !Helpers.IsElevated())
        {
            if (MessageBox.Show(S.Get("StartupHklmConfirm"), Title,
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                return;
        }

        var ok = 0;
        var errors = new List<string>();
        foreach (var row in targets)
        {
            try
            {
                _manager.Disable(row.Item!);
                ok++;
            }
            catch (Exception ex)
            {
                errors.Add($"{row.Name}: {ex.Message}");
            }
        }
        Reload();
        StatusText.Text = errors.Count == 0
            ? S.Format("StartupDisableDone", ok)
            : S.Format("StartupPartial", ok, string.Join("；", errors));
    }

    private void OnRestore(object sender, RoutedEventArgs e)
    {
        var targets = _rows.Where(r => r.IsSelected && r.IsDisabled).ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show(S.Get("StartupNothingSelected"), Title);
            return;
        }

        var ok = 0;
        var errors = new List<string>();
        foreach (var row in targets)
        {
            try
            {
                _manager.Restore(row.Id);
                ok++;
            }
            catch (Exception ex)
            {
                errors.Add($"{row.Name}: {ex.Message}");
            }
        }
        Reload();
        StatusText.Text = errors.Count == 0
            ? S.Format("StartupRestoreDone", ok)
            : S.Format("StartupPartial", ok, string.Join("；", errors));
    }
}
