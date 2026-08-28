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
        var entries = _history.Recent();
        HistoryGrid.ItemsSource = entries.Select(e => new HistoryRow(e)).ToList();
        if (entries.Count == 0)
            PathText.Text = S.Get("HistoryEmpty") + "  " + _history.FilePath;
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
