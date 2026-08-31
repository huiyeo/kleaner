using System.Windows;

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
        Loaded += (_, _) => _viewModel.LoadRules();
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

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
