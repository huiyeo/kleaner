using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kleaner.Core;
using Kleaner.Executor;

namespace Kleaner.App;

/// <summary>主窗口可导航到的子窗口。</summary>
public enum AppWindow
{
    Quarantine,
    Toolbox,
    History,
    Startup,
    Advanced,
    Settings,
}

/// <summary>主窗口视图模型：扫描、取消、清理命令与状态属性。导航通过事件交给 View 打开窗口。</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly ObservableCollection<RuleRow> _rows = new();
    private RuleSet? _ruleSet;
    private string _rulesPath = string.Empty;
    private CancellationTokenSource? _scanCts;
    private bool _cancelRequested;

    public MainWindowViewModel()
    {
        Title = S.Get("AppTitle");
        ScanText = S.Get("BtnScan");
        CleanText = S.Get("BtnClean");
        QuarantineText = S.Get("BtnQuarantine");
        ToolboxText = S.Get("BtnToolbox");
        HistoryText = S.Get("BtnHistory");
        StartupText = S.Get("BtnStartup");
        AdvancedText = S.Get("BtnAdvanced");
        SettingsText = S.Get("BtnSettings");
        CancelText = S.Get("BtnCancel");
        SafetyHeader = S.Get("SafetyNotesHeader");
    }

    /// <summary>请求 View 打开指定子窗口（View 负责设置 Owner 并 ShowDialog）。</summary>
    public event Action<AppWindow>? OpenWindowRequested;

    public ObservableCollection<RuleRow> Rows => _rows;

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string scanText = string.Empty;

    [ObservableProperty]
    private string cleanText = string.Empty;

    [ObservableProperty]
    private string quarantineText = string.Empty;

    [ObservableProperty]
    private string toolboxText = string.Empty;

    [ObservableProperty]
    private string historyText = string.Empty;

    [ObservableProperty]
    private string startupText = string.Empty;

    [ObservableProperty]
    private string advancedText = string.Empty;

    [ObservableProperty]
    private string settingsText = string.Empty;

    [ObservableProperty]
    private string cancelText = string.Empty;

    [ObservableProperty]
    private string safetyHeader = string.Empty;

    [ObservableProperty]
    private string statusText = "…";

    [ObservableProperty]
    private RuleRow? selectedRow;

    [ObservableProperty]
    private string safetyNotesText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelScanCommand))]
    private bool isBusy;

    partial void OnSelectedRowChanged(RuleRow? value) => SafetyNotesText = value?.SafetyNotes ?? string.Empty;

    public void LoadRules()
    {
        try
        {
            var bundled = Path.Combine(AppContext.BaseDirectory, "rules.v1.json");
            (_rulesPath, _ruleSet) = RuleUpdateService.LoadEffective(bundled);

            var errors = RuleSetLoader.Validate(_ruleSet);
            if (errors.Count > 0)
                MessageBox.Show(string.Join("\n", errors), S.Get("Error"));

            _rows.Clear();
            foreach (var rule in _ruleSet.Rules)
                _rows.Add(new RuleRow(rule));
            StatusText = S.Format("StatusRulesLoaded", _ruleSet.Rules.Count, _rulesPath);
            _ = ScanAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, S.Get("Error"));
        }
    }

    private bool CanScan => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        if (_ruleSet is null || _scanCts is not null)
            return;
        var rules = _ruleSet;
        var settings = AppSettings.Load();
        var engine = new ScanEngine(settings.EffectiveQuarantineRoot);
        var cts = new CancellationTokenSource();
        _scanCts = cts;
        IsBusy = true;
        StatusText = S.Get("StatusScanning");
        try
        {
            var token = cts.Token;
            var report = await Task.Run(() => engine.Scan(rules, token), token);
            foreach (var result in report.Results)
            {
                var row = _rows.FirstOrDefault(r => r.Id == result.RuleId);
                row?.Apply(result);
            }
            var errorText = report.Errors.Count == 0 ? string.Empty : string.Join("；", report.Errors);
            StatusText = S.Format("StatusScanDone",
                report.Results.Sum(r => r.FileCount),
                Helpers.FormatBytes(report.Results.Sum(r => r.TotalBytes)),
                errorText);
        }
        catch (OperationCanceledException)
        {
            StatusText = S.Get("Cancelled");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, S.Get("Error"));
        }
        finally
        {
            _scanCts = null;
            _cancelRequested = false;
            IsBusy = false;
        }
    }

    private bool CanCancelScan => IsBusy && !_cancelRequested;

    [RelayCommand(CanExecute = nameof(CanCancelScan))]
    private void CancelScan()
    {
        _cancelRequested = true;
        _scanCts?.Cancel();
        StatusText = S.Get("StatusCancelling");
        CancelScanCommand.NotifyCanExecuteChanged();
    }

    private bool CanClean => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        if (_ruleSet is null)
            return;

        var checkedRows = _rows.Where(r => r.IsSelected).ToList();
        if (checkedRows.Count == 0)
        {
            MessageBox.Show(S.Get("NothingSelected"), Title);
            return;
        }

        if (checkedRows.Any(r => r.Rule.RequiresElevation) && !Helpers.IsElevated())
        {
            if (MessageBox.Show(S.Get("RestartElevatedPrompt"), S.Get("ConfirmCleanTitle"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                Helpers.RestartElevated();
                Application.Current.Shutdown();
            }
            return;
        }

        var selected = checkedRows.Where(r => r.FileCount > 0).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(S.Get("NothingSelected"), Title);
            return;
        }

        var totalFiles = selected.Sum(r => r.FileCount);
        var totalBytes = selected.Sum(r => r.Result!.TotalBytes);
        if (MessageBox.Show(S.Format("ConfirmCleanBody", totalFiles, Helpers.FormatBytes(totalBytes)),
                S.Get("ConfirmCleanTitle"), MessageBoxButton.OKCancel, MessageBoxImage.Question)
            != MessageBoxResult.OK)
            return;

        var settings = AppSettings.Load();
        var manager = new QuarantineManager(settings.EffectiveQuarantineRoot);
        var items = selected.SelectMany(r => r.Result!.Files.Select(f => (r.Id, f))).ToList();
        IsBusy = true;
        StatusText = S.Get("StatusCleaning");
        try
        {
            var report = await Task.Run(() => manager.Execute(items));
            MessageBox.Show(
                S.Format("CleanDoneBody", report.MovedCount, Helpers.FormatBytes(report.MovedBytes),
                    report.Skipped.Count, report.QuarantineDir),
                S.Get("CleanDoneTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, S.Get("Error"));
        }
        finally
        {
            IsBusy = false;
        }
        _ = ScanAsync();
    }

    [RelayCommand]
    private void OpenQuarantine() => OpenWindowRequested?.Invoke(AppWindow.Quarantine);

    [RelayCommand]
    private void OpenToolbox() => OpenWindowRequested?.Invoke(AppWindow.Toolbox);

    [RelayCommand]
    private void OpenHistory() => OpenWindowRequested?.Invoke(AppWindow.History);

    [RelayCommand]
    private void OpenStartup() => OpenWindowRequested?.Invoke(AppWindow.Startup);

    [RelayCommand]
    private void OpenAdvanced() => OpenWindowRequested?.Invoke(AppWindow.Advanced);

    [RelayCommand]
    private void OpenSettings() => OpenWindowRequested?.Invoke(AppWindow.Settings);
}
