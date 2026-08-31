using System.Windows;
using System.Windows.Controls;
using Kleaner.SpecialOps;

namespace Kleaner.App;

public partial class AdvancedWindow : Window
{
    public AdvancedWindow()
    {
        InitializeComponent();
        Title = S.Get("AdvancedTitle");
        WslTab.Header = S.Get("TabWsl");
        BigItemsTab.Header = S.Get("TabBigItems");
        RegistryTab.Header = S.Get("TabRegistry");
        WslDetectButton.Content = S.Get("WslDetect");
        WslCopyButton.Content = S.Get("BtnCopyScript");
        WslColPath.Header = "Path";
        WslColSize.Header = S.Get("ColSize");
        WslGuideBox.Header = S.Get("WslGuideHeader");
        RegistryScanButton.Content = S.Get("BtnRegistryScan");
        RegistryNoteText.Text = S.Get("RegistryNote");
        RegColName.Header = S.Get("RegistryColName");
        RegColReason.Header = S.Get("RegistryColReason");
        RegColKey.Header = S.Get("RegistryColKey");

        BigItemsList.ItemTemplate = (DataTemplate)FindResource("BigItemTemplate");
        BigItemsList.ItemsSource = SystemToolGuide.Items.Select(i => new BigItemRow(i)).ToList();
        Loaded += (_, _) => LoadWsl();
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
}

public sealed class BigItemRow(SystemToolItem item)
{
    public string Title => item.Title;

    public string Note => item.Note;

    public string Command => item.Command;

    public bool RequiresAdmin => item.RequiresAdmin;

    public string RunLabel => S.Get("BtnRun");
}
