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
        RuleUpdateLabel.Text = S.Get("RuleUpdateLabel");
        RuleUpdateShaLabel.Text = S.Get("RuleUpdateShaLabel");
        BrowseButton.Content = S.Get("BtnBrowse");
        CheckUpdateButton.Content = S.Get("BtnCheckUpdate");
        SaveButton.Content = S.Get("BtnSave");

        var settings = AppSettings.Load();
        QuarantinePathBox.Text = settings.QuarantineRoot ?? string.Empty;
        RuleUpdateUrlBox.Text = settings.RuleUpdateUrl ?? string.Empty;
        RuleUpdateShaBox.Text = settings.RuleUpdateSha512 ?? string.Empty;
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
        var url = RuleUpdateUrlBox.Text.Trim();
        var sha = RuleUpdateShaBox.Text.Trim();
        if (url.Length == 0 || sha.Length == 0)
        {
            MessageBox.Show(S.Get("RuleUpdateLabel") + " / " + S.Get("RuleUpdateShaLabel"), Title);
            return;
        }

        CheckUpdateButton.IsEnabled = false;
        try
        {
            var error = await RuleUpdateService.CheckAndUpdateAsync(url, sha);
            MessageBox.Show(error ?? S.Get("RuleUpdateOk"), Title);
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
        settings.RuleUpdateUrl = string.IsNullOrWhiteSpace(RuleUpdateUrlBox.Text) ? null : RuleUpdateUrlBox.Text.Trim();
        settings.RuleUpdateSha512 = string.IsNullOrWhiteSpace(RuleUpdateShaBox.Text) ? null : RuleUpdateShaBox.Text.Trim();
        settings.Save();
        MessageBox.Show(S.Get("Saved"), Title);
        Close();
    }
}
