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
        WindowKeyboard.EnableEscClose(this);
        Title = S.Get("QuarantineTitle");
        RestoreButton.Content = S.Get("BtnRestore");
        DeleteButton.Content = S.Get("BtnDeleteBatch");
        PurgeButton.Content = S.Get("BtnPurgeOld");
        RecoverAuditButton.Content = S.Get("BtnRecoverAudit");
        var headers = new[] { S.Get("ColBatchId"), S.Get("ColCreated"), S.Get("ColEntryCount"), S.Get("ColBatchSize"), "" };
        for (var i = 0; i < headers.Length && i < BatchesGrid.Columns.Count; i++)
            BatchesGrid.Columns[i].Header = headers[i];

        _manager = new QuarantineManager(AppSettings.Load().EffectiveQuarantineRoot, new HistoryManager());
        Refresh();
    }

    private async void Refresh()
    {
        var batches = await Task.Run(_manager.ListBatches);
        BatchesGrid.ItemsSource = batches.Select(b => new BatchRow(b)).ToList();
        await RefreshPendingAuditBanner();
    }

    private async Task RefreshPendingAuditBanner()
    {
        // 只读检查，不加操作锁：即使另一进程正在写入也允许刷新，补记由按钮显式触发。
        var status = await Task.Run(_manager.InspectPendingAudit);
        PendingAuditBanner.Visibility = status.HasPending ? Visibility.Visible : Visibility.Collapsed;
        PendingAuditText.Text = status.AllParseable
            ? S.Format("PendingAuditPending", status.ReceiptCount)
            : S.Format("PendingAuditCorrupt", status.ReceiptCount, status.ReceiptCount - status.ParseableCount);
    }

    private async void OnRecoverPendingAudit(object sender, RoutedEventArgs e)
    {
        RecoverAuditButton.IsEnabled = false;
        try
        {
            await Task.Run(_manager.RecoverPendingAudit);
        }
        catch (Exception ex)
        {
            // 锁竞争与凭据损坏的异常消息由执行器提供且面向用户，原样透出。
            MessageBox.Show(S.Format("PendingAuditRecoverFailed", ex.Message), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            RecoverAuditButton.IsEnabled = true;
        }
        await RefreshPendingAuditBanner();
    }

    private bool _busy;

    // 清空/还原属于写操作，期间禁用全部按钮：引擎层的操作锁会拒绝并发，但不该让用户撞上去才发现
    private bool TryBeginOperation()
    {
        if (_busy) return false;
        _busy = true;
        RestoreButton.IsEnabled = false;
        DeleteButton.IsEnabled = false;
        PurgeButton.IsEnabled = false;
        RecoverAuditButton.IsEnabled = false;
        return true;
    }

    private void EndOperation()
    {
        _busy = false;
        RestoreButton.IsEnabled = true;
        DeleteButton.IsEnabled = true;
        PurgeButton.IsEnabled = true;
        RecoverAuditButton.IsEnabled = true;
    }

    private async void OnRestore(object sender, RoutedEventArgs e)
    {
        if (BatchesGrid.SelectedItem is not BatchRow row || !TryBeginOperation())
            return;
        try
        {
            var report = await Task.Run(() => _manager.RestoreBatch(row.Batch.BatchId));
            var message = report.IsComplete
                ? S.Format("RestoreDone", report.RestoredCount)
                : S.Format("RestorePartial", report.RestoredCount, string.Join("\n", report.Skipped.Concat(report.Failed)));
            MessageBox.Show(message, Title, MessageBoxButton.OK, report.IsComplete ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            // RestoreBatch 对缺失或损坏的清单直接抛出（见 docs/deletion-path.md 的坑），此处兜底防止进程崩溃
            MessageBox.Show(ex.Message, S.Get("Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            EndOperation();
        }
        Refresh();
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (BatchesGrid.SelectedItem is not BatchRow row || _busy)
            return;
        // 永久删除路径，确认强度必须高于可还原的清理
        if (MessageBox.Show(
                S.Format("ConfirmDeleteBatchBody", row.BatchId, row.EntryCount, row.SizeDisplay),
                S.Get("ConfirmDeleteBatchTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning)
            != MessageBoxResult.Yes)
            return;
        if (!TryBeginOperation())
            return;
        try
        {
            var report = await Task.Run(() => _manager.DeleteBatch(row.Batch.BatchId));
            if (!report.Deleted)
                MessageBox.Show(S.Format("DeleteBatchFailed", string.Join("\n", report.Failed)), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, S.Get("Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            EndOperation();
        }
        Refresh();
    }

    private async void OnPurge(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        if (MessageBox.Show(
                S.Get("ConfirmPurgeBody"),
                S.Get("ConfirmPurgeTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning)
            != MessageBoxResult.Yes)
            return;
        if (!TryBeginOperation())
            return;
        try
        {
            var purged = await Task.Run(() => _manager.PurgeOlderThan(TimeSpan.FromDays(7)));
            MessageBox.Show(S.Format("PurgeDone", purged), Title);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, S.Get("Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            EndOperation();
        }
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
