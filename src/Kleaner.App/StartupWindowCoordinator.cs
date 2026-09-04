using System.Collections.ObjectModel;
using System.Windows;
using Kleaner.Executor;

namespace Kleaner.App;

/// <summary>启动项窗口所需的对话框能力；实现可由 WPF 或测试替身提供。</summary>
public interface IStartupWindowDialog
{
    void ShowInfo(string message, string title);

    bool ConfirmElevation(string message, string title);
}

/// <summary>启动项操作完成后的窗口刷新与状态更新指令。</summary>
public sealed record StartupWindowOperationResult(bool Refresh, string? StatusText);

/// <summary>启动项窗口编排：在调用管理器前完成选中筛选与提权确认。</summary>
public sealed class StartupWindowCoordinator(
    IStartupManager manager,
    IStartupWindowDialog dialog,
    Func<bool> isElevated)
{
    public IReadOnlyList<StartupRow> LoadRows()
    {
        var rows = new List<StartupRow>();
        rows.AddRange(manager.Enumerate().Select(item => new StartupRow(item)));
        rows.AddRange(manager.ListDisabled().Select(item => new StartupRow(item)));
        return rows;
    }

    public string FormatSummary(IEnumerable<StartupRow> rows)
    {
        var snapshot = rows.ToList();
        return snapshot.Count == 0
            ? S.Get("StartupNone")
            : S.Format("StartupStatusLoaded", snapshot.Count(row => !row.IsDisabled), snapshot.Count(row => row.IsDisabled));
    }

    public StartupWindowOperationResult DisableSelected(IEnumerable<StartupRow> rows)
    {
        var targets = rows.Where(row => row.IsSelected && !row.IsDisabled).ToList();
        if (targets.Count == 0)
        {
            dialog.ShowInfo(S.Get("StartupNothingSelected"), S.Get("StartupTitle"));
            return new StartupWindowOperationResult(false, null);
        }

        if (targets.Any(row => row.Item!.RequiresElevation) && !isElevated() &&
            !dialog.ConfirmElevation(S.Get("StartupHklmConfirm"), S.Get("StartupTitle")))
            return new StartupWindowOperationResult(false, null);

        var errors = new List<string>();
        var succeeded = 0;
        foreach (var row in targets)
        {
            try
            {
                manager.Disable(row.Item!);
                succeeded++;
            }
            catch (Exception ex)
            {
                errors.Add($"{row.Name}: {ex.Message}");
            }
        }

        return new StartupWindowOperationResult(
            true,
            errors.Count == 0
                ? S.Format("StartupDisableDone", succeeded)
                : S.Format("StartupPartial", succeeded, string.Join("；", errors)));
    }

    public StartupWindowOperationResult RestoreSelected(IEnumerable<StartupRow> rows)
    {
        var targets = rows.Where(row => row.IsSelected && row.IsDisabled).ToList();
        if (targets.Count == 0)
        {
            dialog.ShowInfo(S.Get("StartupNothingSelected"), S.Get("StartupTitle"));
            return new StartupWindowOperationResult(false, null);
        }

        var errors = new List<string>();
        var succeeded = 0;
        foreach (var row in targets)
        {
            try
            {
                manager.Restore(row.Id);
                succeeded++;
            }
            catch (Exception ex)
            {
                errors.Add($"{row.Name}: {ex.Message}");
            }
        }

        return new StartupWindowOperationResult(
            true,
            errors.Count == 0
                ? S.Format("StartupRestoreDone", succeeded)
                : S.Format("StartupPartial", succeeded, string.Join("；", errors)));
    }
}

/// <summary>WPF 默认对话框实现。</summary>
public sealed class MessageBoxStartupWindowDialog : IStartupWindowDialog
{
    public void ShowInfo(string message, string title) =>
        MessageBox.Show(message, title);

    public bool ConfirmElevation(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
}
