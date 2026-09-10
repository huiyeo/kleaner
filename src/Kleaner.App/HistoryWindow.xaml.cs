using System.Diagnostics;
using System.IO;
using System.Windows;
using Kleaner.Executor;

namespace Kleaner.App;

public partial class HistoryWindow : Window
{
    private readonly HistoryManager _history;

    public HistoryWindow()
    {
        InitializeComponent();
        WindowKeyboard.EnableEscClose(this);
        Title = S.Get("HistoryTitle");
        OpenFileButton.Content = S.Get("BtnOpenFile");
        var headers = new[]
        {
            S.Get("HistColTime"), S.Get("HistColAction"), S.Get("HistColDetail"),
            S.Get("ColFiles"), S.Get("HistColBytes"), S.Get("HistColResult"),
        };
        for (var i = 0; i < headers.Length && i < HistoryGrid.Columns.Count; i++)
            HistoryGrid.Columns[i].Header = headers[i];

        _history = new HistoryManager();
        PathText.Text = _history.FilePath;
        var snapshot = _history.RecentVerified();
        HistoryGrid.ItemsSource = snapshot.Entries.Select(e => new HistoryRow(e)).ToList();
        if (snapshot.Entries.Count == 0)
            PathText.Text = S.Get("HistoryEmpty") + "  " + _history.FilePath;
        // 完整性提示只陈述可发现的删改痕迹；断裂记录仍照常列出，不伪装完整也不夸大为防篡改。
        var integrity = snapshot.Integrity;
        if (integrity.Broken)
        {
            IntegrityText.Text = S.Format("HistChainBroken", integrity.BrokenAt ?? 0);
            IntegrityBanner.Visibility = Visibility.Visible;
        }
        else if (integrity.HeadMismatch)
        {
            IntegrityText.Text = S.Get("HistChainHead");
            IntegrityBanner.Visibility = Visibility.Visible;
        }
        else if (integrity.UnchainedCount > 0)
        {
            IntegrityText.Text = S.Format("HistChainLegacy", integrity.UnchainedCount);
            IntegrityBanner.Visibility = Visibility.Visible;
        }
    }

    private void OnOpenFile(object sender, RoutedEventArgs e)
    {
        if (File.Exists(_history.FilePath))
            Process.Start(new ProcessStartInfo(_history.FilePath) { UseShellExecute = true });
    }
}

public sealed class HistoryRow(HistoryEntry entry)
{
    public string TimeDisplay => entry.TimeDisplay;

    public string Action => entry.Action;

    public string Detail => entry.Detail;

    public int FileCount => entry.FileCount;

    public string BytesDisplay => Helpers.FormatBytes(entry.Bytes);

    public string Result => entry.Result;
}
