using System.IO;
using System.Windows;
using System.Windows.Controls;
using Kleaner.SpecialOps;

namespace Kleaner.App;

public partial class AdvancedWindow : Window
{
    public AdvancedWindow()
    {
        InitializeComponent();
        WindowKeyboard.EnableEscClose(this);
        Title = S.Get("AdvancedTitle");
        WslTab.Header = S.Get("TabWsl");
        BigItemsTab.Header = S.Get("TabBigItems");
        WindowsOldTab.Header = S.Get("TabWindowsOld");
        RegistryTab.Header = S.Get("TabRegistry");
        WslDetectButton.Content = S.Get("WslDetect");
        WslCopyButton.Content = S.Get("BtnCopyScript");
        WslColPath.Header = S.Get("ColPath");
        WslColSize.Header = S.Get("ColSize");
        WslGuideBox.Header = S.Get("WslGuideHeader");
        WinOldDetectButton.Content = S.Get("WinOldDetectBtn");
        WinOldMeasureButton.Content = S.Get("WinOldMeasureBtn");
        WinOldSettingsButton.Content = S.Get("WinOldOpenSettings");
        WinOldCleanmgrButton.Content = S.Get("WinOldOpenCleanmgr");
        WinOldWarnText.Text = S.Get("WinOldWarn");
        RegistryScanButton.Content = S.Get("BtnRegistryScan");
        RegistryNoteText.Text = S.Get("RegistryNote");
        RegColName.Header = S.Get("RegistryColName");
        RegColReason.Header = S.Get("RegistryColReason");
        RegColKey.Header = S.Get("RegistryColKey");

        BigItemsList.ItemTemplate = (DataTemplate)FindResource("BigItemTemplate");
        BigItemsList.ItemsSource = SystemToolGuide.Items.Select(i => new BigItemRow(i)).ToList();
        Loaded += (_, _) => { LoadWsl(); LoadWindowsOld(); };
    }

    private async void LoadWsl()
    {
        var items = await Task.Run(WslInspector.DetectVhdx);
        WslList.ItemsSource = items;
        if (items.Count == 0)
            WslGuide.Text = S.Get("WslNone");
    }

    private void OnDetectWsl(object sender, RoutedEventArgs e) => LoadWsl();

    private void OnWslSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WslList.SelectedItem is VhdxInfo vhdx)
            WslGuide.Text = WslInspector.BuildCompactGuide(vhdx);
    }

    private void OnCopyGuide(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(WslGuide.Text))
            Clipboard.SetText(WslGuide.Text);
    }

    private async void OnRegistryScan(object sender, RoutedEventArgs e)
    {
        RegistryScanButton.IsEnabled = false;
        try
        {
            var entries = await Task.Run(RegistryInspector.ScanBrokenUninstallEntries);
            RegistryList.ItemsSource = entries;
            if (entries.Count == 0)
                MessageBox.Show(S.Get("RegistryNone"), Title);
        }
        finally
        {
            RegistryScanButton.IsEnabled = true;
        }
    }

    private void OnRunTool(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not BigItemRow item)
            return;
        var privilege = item.RequiresAdmin ? S.Get("RunAsAdmin") : S.Get("RunAsNormal");
        if (MessageBox.Show(
                S.Format("ConfirmRunToolBody", privilege, item.Title),
                S.Get("ConfirmRunToolTitle"), MessageBoxButton.OKCancel, MessageBoxImage.Question)
            != MessageBoxResult.OK)
            return;
        Helpers.RunSystemCommand(item.Command, item.RequiresAdmin);
    }

    private WindowsOldInfo? _windowsOld;

    // 检测只做存在性判断（零遍历），窗口打开即执行；测试以临时根注入，App 侧固定取系统目录所在盘
    private async void LoadWindowsOld()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        _windowsOld = await Task.Run(() => WindowsOldInspector.Inspect(root));
        if (_windowsOld.Exists)
        {
            WinOldStatusText.Text = S.Format("WinOldFoundPath", _windowsOld.Path);
            WinOldSizeText.Text = string.Empty;
            SetWindowsOldActionsEnabled(true);
        }
        else
        {
            WinOldStatusText.Text = S.Get("WinOldNone");
            WinOldSizeText.Text = string.Empty;
            SetWindowsOldActionsEnabled(false);
        }
    }

    private void OnDetectWindowsOld(object sender, RoutedEventArgs e) => LoadWindowsOld();

    private void SetWindowsOldActionsEnabled(bool enabled)
    {
        WinOldMeasureButton.IsEnabled = enabled;
        WinOldSettingsButton.IsEnabled = enabled;
        WinOldCleanmgrButton.IsEnabled = enabled;
    }

    private async void OnMeasureWindowsOld(object sender, RoutedEventArgs e)
    {
        if (_windowsOld is not { Exists: true })
            return;
        WinOldMeasureButton.IsEnabled = false;
        try
        {
            // 有界测量在残留很大时可能持续数秒（上限 20 万文件），显式触发 + 后台执行
            var path = _windowsOld.Path;
            var report = await Task.Run(() => WindowsOldInspector.Measure(path, CancellationToken.None));
            var text = S.Format("WinOldSizeResult", Helpers.FormatBytes(report.Bytes), report.FileCount);
            if (report.Truncated)
                text += S.Get("WinOldSizeTruncated");
            WinOldSizeText.Text = text;
        }
        finally
        {
            WinOldMeasureButton.IsEnabled = _windowsOld is { Exists: true };
        }
    }

    // 官方入口仅负责打开系统自带工具，删除动作与回滚期限提示全部发生在官方界面内
    private void OnOpenStorageSettings(object sender, RoutedEventArgs e) =>
        Helpers.RunSystemCommand("ms-settings:storagesense", requiresAdmin: false);

    private void OnOpenDiskCleanup(object sender, RoutedEventArgs e) =>
        Helpers.RunSystemCommand("cleanmgr.exe", requiresAdmin: false);
}

public sealed class BigItemRow(SystemToolItem item)
{
    public string Title => item.Title;

    public string Note => item.Note;

    public string Command => item.Command;

    public bool RequiresAdmin => item.RequiresAdmin;

    public string RunLabel => S.Get("BtnRun");
}
