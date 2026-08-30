using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Kleaner.Core;
using Kleaner.Executor;

namespace Kleaner.App;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<RuleRow> _rows = new();
    private RuleSet? _ruleSet;
    private string _rulesPath = string.Empty;

    public MainWindow()
    {
        S.Load();
        InitializeComponent();
        LoadStrings();
        RulesGrid.ItemsSource = _rows;
        Loaded += (_, _) => LoadRules();
    }

    private void LoadStrings()
    {
        Title = S.Get("AppTitle");
        ScanButton.Content = S.Get("BtnScan");
        CleanButton.Content = S.Get("BtnClean");
        QuarantineButton.Content = S.Get("BtnQuarantine");
        ToolboxButton.Content = S.Get("BtnToolbox");
        HistoryButton.Content = S.Get("BtnHistory");
        StartupButton.Content = S.Get("BtnStartup");
        AdvancedButton.Content = S.Get("BtnAdvanced");
        SettingsButton.Content = S.Get("BtnSettings");
        SafetyBox.Header = S.Get("SafetyNotesHeader");

        var headers = new[]
        {
            S.Get("ColSelect"), S.Get("ColName"), S.Get("ColCategory"), S.Get("ColFiles"),
            S.Get("ColSize"), S.Get("ColRisk"), S.Get("ColElevated"), S.Get("ColNote"),
        };
        for (var i = 0; i < headers.Length && i < RulesGrid.Columns.Count; i++)
            RulesGrid.Columns[i].Header = headers[i];
    }

    private void LoadRules()
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
            StatusText.Text = S.Format("StatusRulesLoaded", _ruleSet.Rules.Count, _rulesPath);
            StartScan();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, S.Get("Error"));
        }
    }

    private void SetBusy(string message)
    {
        StatusText.Text = message;
        ScanButton.IsEnabled = false;
        CleanButton.IsEnabled = false;
    }

    private void SetIdle()
    {
        ScanButton.IsEnabled = true;
        CleanButton.IsEnabled = true;
    }

    private async void StartScan()
    {
        if (_ruleSet is null)
            return;
        var rules = _ruleSet;
        var settings = AppSettings.Load();
        var engine = new ScanEngine(settings.EffectiveQuarantineRoot);
        SetBusy(S.Get("StatusScanning"));
        try
        {
            var report = await Task.Run(() => engine.Scan(rules));
            foreach (var result in report.Results)
            {
                var row = _rows.FirstOrDefault(r => r.Id == result.RuleId);
                row?.Apply(result);
            }
            var errorText = report.Errors.Count == 0 ? string.Empty : string.Join("；", report.Errors);
            StatusText.Text = S.Format("StatusScanDone",
                report.Results.Sum(r => r.FileCount),
                Helpers.FormatBytes(report.Results.Sum(r => r.TotalBytes)),
                errorText);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, S.Get("Error"));
        }
        finally
        {
            SetIdle();
        }
    }

    private void OnScan(object sender, RoutedEventArgs e) => StartScan();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        SafetyNotesBox.Text = (RulesGrid.SelectedItem as RuleRow)?.SafetyNotes ?? string.Empty;

    private async void OnClean(object sender, RoutedEventArgs e)
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
        SetBusy(S.Get("StatusCleaning"));
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
            SetIdle();
        }
        StartScan();
    }

    private void OnQuarantine(object sender, RoutedEventArgs e) => new QuarantineWindow { Owner = this }.ShowDialog();

    private void OnToolbox(object sender, RoutedEventArgs e) => new ToolboxWindow { Owner = this }.ShowDialog();

    private void OnHistory(object sender, RoutedEventArgs e) => new HistoryWindow { Owner = this }.ShowDialog();

    private void OnStartup(object sender, RoutedEventArgs e) => new StartupWindow { Owner = this }.ShowDialog();

    private void OnAdvanced(object sender, RoutedEventArgs e) => new AdvancedWindow { Owner = this }.ShowDialog();

    private void OnSettings(object sender, RoutedEventArgs e) => new SettingsWindow { Owner = this }.ShowDialog();
}
