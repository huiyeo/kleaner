using System.Windows;
using System.Windows.Controls;
using Kleaner.Executor;

namespace Kleaner.App;

public partial class QuarantineWindow : Window
{
    private readonly QuarantineManager _manager;

    public QuarantineWindow()
    {
        InitializeComponent();
        Title = S.Get("QuarantineTitle");
        RestoreButton.Content = S.Get("BtnRestore");
        DeleteButton.Content = S.Get("BtnDeleteBatch");
        PurgeButton.Content = S.Get("BtnPurgeOld");
        var headers = new[] { S.Get("ColBatchId"), S.Get("ColCreated"), S.Get("ColEntryCount"), S.Get("ColBatchSize"), "" };
        for (var i = 0; i < headers.Length && i < BatchesGrid.Columns.Count; i++)
            BatchesGrid.Columns[i].Header = headers[i];

        _manager = new QuarantineManager(AppSettings.Load().EffectiveQuarantineRoot);
        Refresh();
    }

    private async void Refresh()
    {
        var batches = await Task.Run(_manager.ListBatches);
        BatchesGrid.ItemsSource = batches.Select(b => new BatchRow(b)).ToList();
    }

    private void OnRestore(object sender, RoutedEventArgs e)
    {
        if (BatchesGrid.SelectedItem is not BatchRow row)
            return;
        var restored = _manager.RestoreBatch(row.Batch.BatchId);
        MessageBox.Show(S.Format("RestoreDone", restored), Title);
        Refresh();
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (BatchesGrid.SelectedItem is not BatchRow row)
            return;
        _manager.DeleteBatch(row.Batch.BatchId);
        Refresh();
    }

    private void OnPurge(object sender, RoutedEventArgs e)
    {
        var purged = _manager.PurgeOlderThan(TimeSpan.FromDays(7));
        MessageBox.Show(S.Format("PurgeDone", purged), Title);
        Refresh();
    }
}

public sealed class BatchRow(QuarantineBatch batch)
{
    public QuarantineBatch Batch => batch;

    public string BatchId => batch.BatchId;

    public string CreatedDisplay => batch.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public int EntryCount => batch.Entries.Count;

    public string SizeDisplay => Helpers.FormatBytes(batch.TotalBytes);
}
