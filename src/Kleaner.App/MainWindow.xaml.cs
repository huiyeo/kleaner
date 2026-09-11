using System.Windows;
using Kleaner.App.Services;

namespace Kleaner.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        S.Load();
        InitializeComponent();
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;
        LoadStrings();
        _viewModel.OpenWindowRequested += OpenWindow;
        Loaded += (_, _) =>
        {
            _viewModel.LoadRules();
            // AI 解释默认关闭：设置启用后才显示入口（ADR 0004 可选解释层）。
            if (AppSettings.Load().AiEnabled)
            {
                AiSection.Visibility = Visibility.Visible;
                AiExplainButton.Visibility = Visibility.Visible;
            }
        };
    }

    private void LoadStrings()
    {
        var headers = new[]
        {
            S.Get("ColSelect"), S.Get("ColName"), S.Get("ColCategory"), S.Get("ColFiles"),
            S.Get("ColSize"), S.Get("ColRisk"), S.Get("ColElevated"), S.Get("ColNote"),
        };
        for (var i = 0; i < headers.Length && i < RulesGrid.Columns.Count; i++)
            RulesGrid.Columns[i].Header = headers[i];
    }

    private void OpenWindow(AppWindow kind)
    {
        Window? window = kind switch
        {
            AppWindow.Quarantine => new QuarantineWindow(),
            AppWindow.Toolbox => new ToolboxWindow(),
            AppWindow.History => new HistoryWindow(),
            AppWindow.Startup => new StartupWindow(),
            AppWindow.Advanced => new AdvancedWindow(),
            AppWindow.Settings => new SettingsWindow(),
            _ => null,
        };
        if (window is null)
            return;
        window.Owner = this;
        window.ShowDialog();
    }

    private async void OnAiExplain(object sender, RoutedEventArgs e)
    {
        var selected = _viewModel.SelectedRow;
        if (selected is null)
        {
            AiOutputText.Text = S.Get("AiNoSelection");
            AiOutputText.Visibility = Visibility.Visible;
            return;
        }
        var settings = AppSettings.Load();
        var endpoint = string.IsNullOrWhiteSpace(settings.AiEndpoint)
            ? AiExplainService.DefaultEndpoint
            : settings.AiEndpoint;
        AiExplainButton.IsEnabled = false;
        AiOutputText.Text = S.Get("AiPending");
        AiOutputText.Visibility = Visibility.Visible;
        try
        {
            var buckets = new[]
            {
                new AiExplainService.Bucket(selected.CategoryDisplay, selected.FileCount, selected.Result?.TotalBytes ?? 0),
            };
            using var http = new System.Net.Http.HttpClient();
            var service = new AiExplainService(http, endpoint);
            var result = await service.ExplainAsync(buckets);
            // 注入降权：疑似提示注入时加显式前缀；输出仅为展示文本，永不进清理链路。
            AiOutputText.Text = result.Ok
                ? (result.Suspicious ? S.Get("AiSuspiciousPrefix") + result.Text : result.Text)
                : result.Error;
        }
        finally
        {
            AiExplainButton.IsEnabled = true;
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
