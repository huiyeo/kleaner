using System.Windows;
using Kleaner.Core;
using Microsoft.Win32;

namespace Kleaner.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        Title = S.Get("SettingsTitle");
        QuarantinePathLabel.Text = S.Get("QuarantinePathLabel");
        RuleUpdateLabel.Text = S.Get("RuleUpdateOfficialLabel");
        BrowseButton.Content = S.Get("BtnBrowse");
        CheckUpdateButton.Content = S.Get("BtnCheckUpdate");
        SaveButton.Content = S.Get("BtnSave");

        var settings = AppSettings.Load();
        QuarantinePathBox.Text = settings.QuarantineRoot ?? string.Empty;
        // 官方源是内嵌常量，用户不可输入 URL 或摘要——那是工单 12 移除的不可信更新途径。
        RuleUpdateSourceText.Text = RuleTrust.OfficialManifestUrl;
        RuleUpdateStateText.Text = RuleUpdateService.DescribeLocalState();
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = S.Get("QuarantinePathLabel"),
        };
        if (dialog.ShowDialog(this) == true)
            QuarantinePathBox.Text = dialog.FolderName;
    }

    private async void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        try
        {
            var currentVersion = typeof(SettingsWindow).Assembly.GetName().Version;
            var appVersion = currentVersion is null ? null : $"{currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Build}";
            var error = await RuleUpdateService.UpdateFromOfficialAsync(appVersion);
            MessageBox.Show(error ?? S.Get("RuleUpdateOk"), Title);
            RuleUpdateStateText.Text = RuleUpdateService.DescribeLocalState();
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var settings = AppSettings.Load();
        settings.QuarantineRoot = string.IsNullOrWhiteSpace(QuarantinePathBox.Text) ? null : QuarantinePathBox.Text.Trim();
        settings.Save();
        MessageBox.Show(S.Get("Saved"), Title);
        Close();
    }
}
