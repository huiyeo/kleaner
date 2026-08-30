using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Kleaner.Analysis;
using Kleaner.Core;
using Kleaner.Executor;

namespace Kleaner.App;

public partial class ToolboxWindow : Window
{
    private CancellationTokenSource? _cts;
    private IReadOnlyList<UsageItem> _usageItems = Array.Empty<UsageItem>();

    public ToolboxWindow()
    {
        InitializeComponent();
        Title = S.Get("ToolboxTitle");
        UsageTab.Header = S.Get("TabUsage");
        LargeTab.Header = S.Get("TabLarge");
        DupTab.Header = S.Get("TabDup");
        UsageRootLabel.Text = S.Get("RootLabel");
        LargeRootLabel.Text = S.Get("RootLabel");
        DupRootLabel.Text = S.Get("RootLabel");
        UsageBrowseButton.Content = S.Get("BtnBrowse");
        LargeBrowseButton.Content = S.Get("BtnBrowse");
        DupBrowseButton.Content = S.Get("BtnBrowse");
        UsageScanButton.Content = S.Get("BtnScan");
        LargeScanButton.Content = S.Get("BtnScan");
        DupScanButton.Content = S.Get("BtnScan");
        UsageEnterButton.Content = S.Get("BtnEnterDir");
        UsageViewList.Content = S.Get("BtnViewList");
        UsageViewTreemap.Content = S.Get("BtnViewTreemap");
        LargeMinLabel.Text = S.Get("MinSizeLabel");
        DupMinLabel.Text = S.Get("MinSizeLabel");
        LargeCleanButton.Content = S.Get("BtnCleanSelected");
        DupCleanButton.Content = S.Get("BtnCleanDupSelected");
        UsageRootBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        LargeRootBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        DupRootBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var usageHeaders = new[] { S.Get("ColPath"), S.Get("ColSize"), S.Get("ColType") };
        for (var i = 0; i < usageHeaders.Length && i < UsageGrid.Columns.Count; i++)
            UsageGrid.Columns[i].Header = usageHeaders[i];
        var largeHeaders = new[] { S.Get("ColSelect"), S.Get("ColPath"), S.Get("ColSize"), S.Get("ColMtime") };
        for (var i = 0; i < largeHeaders.Length && i < LargeGrid.Columns.Count; i++)
            LargeGrid.Columns[i].Header = largeHeaders[i];
        var dupHeaders = new[] { S.Get("ColSelect"), "#", S.Get("ColPath"), S.Get("ColSize"), S.Get("ColTag") };
        for (var i = 0; i < dupHeaders.Length && i < DupGrid.Columns.Count; i++)
            DupGrid.Columns[i].Header = dupHeaders[i];

        Closed += (_, _) => _cts?.Cancel();
        TreemapHost.LayoutUpdated += (_, _) =>
        {
            if (UsageViewTreemap.IsChecked != true)
                return;
            // 宿主尺寸与画布不一致（可见性切换/布局中途取值）时补渲染；收敛后不再触发
            var w = Math.Max(TreemapHost.ActualWidth - 4, 10);
            var h = Math.Max(TreemapHost.ActualHeight - 4, 10);
            if (Math.Abs(TreemapCanvas.Width - w) > 1 || Math.Abs(TreemapCanvas.Height - h) > 1)
                RenderTreemap();
        };
    }

    private void SetBusy(bool busy)
    {
        UsageScanButton.IsEnabled = !busy;
        LargeScanButton.IsEnabled = !busy;
        DupScanButton.IsEnabled = !busy;
        LargeCleanButton.IsEnabled = !busy;
        DupCleanButton.IsEnabled = !busy;
    }

    private async void OnUsageScan(object sender, RoutedEventArgs e) => await ScanUsageAsync();

    private async void OnUsageEnter(object sender, RoutedEventArgs e)
    {
        if (UsageGrid.SelectedItem is UsageRow row && row.Item.IsDirectory)
        {
            UsageRootBox.Text = row.Item.Path;
            await ScanUsageAsync();
        }
    }

    private async Task ScanUsageAsync()
    {
        var root = UsageRootBox.Text.Trim();
        if (root.Length == 0)
            return;
        SetBusy(true);
        _cts = new CancellationTokenSource();
        StatusText.Text = S.Get("StatusScanningGeneric");
        try
        {
            var token = _cts.Token;
            var items = await Task.Run(() => DiskUsageAnalyzer.TopLevel(root, token), token);
            _usageItems = items;
            UsageGrid.ItemsSource = items.Select(i => new UsageRow(i)).ToList();
            StatusText.Text = S.Format("StatusUsageDone", items.Count, Helpers.FormatBytes(items.Sum(i => i.SizeBytes)));
            if (UsageViewTreemap.IsChecked == true)
                RenderTreemapDeferred();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = S.Get("Cancelled");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, S.Get("Error"));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnUsageViewChanged(object sender, RoutedEventArgs e)
    {
        if (UsageGrid is null || TreemapHost is null)
            return; // InitializeComponent 期间触发
        var treemap = UsageViewTreemap.IsChecked == true;
        UsageGrid.Visibility = treemap ? Visibility.Collapsed : Visibility.Visible;
        TreemapHost.Visibility = treemap ? Visibility.Visible : Visibility.Collapsed;
        if (treemap)
            RenderTreemapDeferred();
    }

    /// <summary>可见性/尺寸刚变化时布局尚未完成，推迟到布局结束后再取 ActualWidth 渲染。</summary>
    private void RenderTreemapDeferred() =>
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(RenderTreemap));

    private void RenderTreemap()
    {
        TreemapCanvas.Children.Clear();
        var width = Math.Max(TreemapHost.ActualWidth - 4, 10);
        var height = Math.Max(TreemapHost.ActualHeight - 4, 10);
        TreemapCanvas.Width = width;
        TreemapCanvas.Height = height;
        if (_usageItems.Count == 0)
            return;

        var rects = TreemapLayout.Squarify(
            _usageItems.Select(i => (i.Path, i.SizeBytes)), width, height);
        var labels = new List<(TreemapRect Rect, string Name)>();
        foreach (var r in rects)
        {
            var item = _usageItems.First(i => i.Path == r.Path);
            var border = new System.Windows.Controls.Border
            {
                Width = Math.Max(r.Width - 1, 0),
                Height = Math.Max(r.Height - 1, 0),
                Background = UsageBrush(item),
                BorderBrush = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(1),
                ToolTip = $"{r.Path}\n{Helpers.FormatBytes(r.SizeBytes)}",
                Tag = item,
            };
            TreemapCanvas.Children.Add(border);
            if (r.Width > 40 && r.Height > 22)
            {
                var name = System.IO.Path.GetFileName(r.Path.TrimEnd('\\')) is { Length: > 0 } fn ? fn : r.Path;
                labels.Add((r, $"{name}\n{Helpers.FormatBytes(r.SizeBytes)}"));
            }
        }
        // 标签统一最后绘制，避免被后续矩形盖住
        foreach (var (rect, text) in labels)
        {
            var label = new TextBlock
            {
                Text = text,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                Margin = new Thickness(3, 1, 1, 1),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Width = Math.Max(rect.Width - 6, 0),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(label, rect.X);
            Canvas.SetTop(label, rect.Y);
            TreemapCanvas.Children.Add(label);
        }
        StatusText.Text = S.Get("TreemapHint");
    }

    private void OnTreemapMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
            return;
        if ((e.OriginalSource as FrameworkElement)?.Tag is UsageItem item && item.IsDirectory)
        {
            UsageRootBox.Text = item.Path;
            _ = ScanUsageAsync();
        }
    }

    private static System.Windows.Media.Brush UsageBrush(UsageItem item)
    {
        byte r, g, b;
        if (item.IsDirectory)
            (r, g, b) = (0x2E, 0x6F, 0xB8);
        else
        {
            var ext = System.IO.Path.GetExtension(item.Path);
            var seed = Math.Abs((ext.Length == 0 ? item.Path : ext).GetHashCode(StringComparison.OrdinalIgnoreCase));
            var hue = seed % 360;
            (r, g, b) = HsvToRgb(hue, 0.42, 0.78);
        }
        var brush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Opacity = 0.92;
        return brush;
    }

    private static (byte, byte, byte) HsvToRgb(int hue, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(hue / 60.0 % 2 - 1));
        var m = v - c;
        var (r1, g1, b1) = hue switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return ((byte)((r1 + m) * 255), (byte)((g1 + m) * 255), (byte)((b1 + m) * 255));
    }

    private async void OnLargeScan(object sender, RoutedEventArgs e) => await ScanLargeAsync();

    private async Task ScanLargeAsync()
    {
        var root = LargeRootBox.Text.Trim();
        if (!long.TryParse(LargeMinBox.Text.Trim(), out var minMb) || root.Length == 0)
            return;
        SetBusy(true);
        _cts = new CancellationTokenSource();
        StatusText.Text = S.Get("StatusScanningGeneric");
        try
        {
            var token = _cts.Token;
            var items = await Task.Run(() => LargeFileScanner.Scan(root, minMb * 1024L * 1024, top: 200, token), token);
            LargeGrid.ItemsSource = items.Select(i => new SelectableRow<LargeFileItem>(i, i.Path, Helpers.FormatBytes(i.SizeBytes),
                i.LastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"))).ToList();
            StatusText.Text = S.Format("StatusLargeDone", items.Count, Helpers.FormatBytes(items.Sum(i => i.SizeBytes)));
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = S.Get("Cancelled");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, S.Get("Error"));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnLargeClean(object sender, RoutedEventArgs e)
    {
        var rows = LargeGrid.ItemsSource?.Cast<SelectableRow<LargeFileItem>>()
            .Where(r => r.IsSelected).ToList() ?? new List<SelectableRow<LargeFileItem>>();
        await CleanSelectedAsync(
            rows.Select(r => new FileCandidate(r.Item.Path, r.Item.SizeBytes, r.Item.LastWriteTimeUtc)),
            "large-files", rows.Sum(r => r.Item.SizeBytes));
        if (rows.Count > 0)
            await ScanLargeAsync();
    }

    private async void OnDupScan(object sender, RoutedEventArgs e) => await ScanDupAsync();

    private async Task ScanDupAsync()
    {
        var root = DupRootBox.Text.Trim();
        if (!long.TryParse(DupMinBox.Text.Trim(), out var minMb) || root.Length == 0)
            return;
        SetBusy(true);
        _cts = new CancellationTokenSource();
        StatusText.Text = S.Get("StatusDupScanning");
        try
        {
            var token = _cts.Token;
            var groups = await Task.Run(() => DuplicateFinder.Find(root, minMb * 1024L * 1024, token), token);
            var rows = new List<DupRow>();
            for (var g = 0; g < groups.Count; g++)
            {
                // 每组默认勾选除最新一份外的所有副本；最新一份标记「保留」
                var ordered = groups[g].Files
                    .Select(f => (Path: f, Info: new FileInfo(f)))
                    .OrderByDescending(x => x.Info.LastWriteTimeUtc)
                    .ToList();
                for (var i = 0; i < ordered.Count; i++)
                {
                    var (path, info) = ordered[i];
                    rows.Add(new DupRow(groups[g], g + 1, path, info.Length,
                        isSelected: i > 0,
                        tag: i == 0 ? S.Get("DupKeep") : S.Get("DupCopy")));
                }
            }
            DupGrid.ItemsSource = rows;
            var wasted = groups.Sum(g => g.SizeBytes * (g.Files.Count - 1));
            StatusText.Text = S.Format("StatusDupDone", groups.Count, Helpers.FormatBytes(wasted));
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = S.Get("Cancelled");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, S.Get("Error"));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnDupClean(object sender, RoutedEventArgs e)
    {
        var rows = DupGrid.ItemsSource?.Cast<DupRow>().Where(r => r.IsSelected).ToList();
        if (rows is null || rows.Count == 0)
            return;

        // 安全底线：每组必须至少保留一份
        var keepByGroup = DupGrid.ItemsSource!.Cast<DupRow>()
            .Where(r => !r.IsSelected).Select(r => r.GroupIndex).ToHashSet();
        var offending = rows.Where(r => !keepByGroup.Contains(r.GroupIndex)).ToList();
        if (offending.Count > 0)
        {
            MessageBox.Show(S.Get("DupKeepOneRequired"), Title);
            return;
        }

        var files = rows.Select(r => new FileCandidate(r.Path, r.SizeBytes,
            File.Exists(r.Path) ? File.GetLastWriteTimeUtc(r.Path) : DateTime.UtcNow));
        await CleanSelectedAsync(files, "duplicates", rows.Sum(r => r.SizeBytes));
        if (rows.Count > 0)
            await ScanDupAsync();
    }

    private async Task CleanSelectedAsync(
        IEnumerable<FileCandidate> items, string action, long totalBytes)
    {
        var list = items.ToList();
        if (list.Count == 0)
        {
            MessageBox.Show(S.Get("NothingSelected"), Title);
            return;
        }
        if (MessageBox.Show(
                S.Format("ConfirmCleanBody", list.Count, Helpers.FormatBytes(totalBytes)),
                S.Get("ConfirmCleanTitle"), MessageBoxButton.OKCancel, MessageBoxImage.Question)
            != MessageBoxResult.OK)
            return;

        var settings = AppSettings.Load();
        var history = new HistoryManager();
        var manager = new QuarantineManager(settings.EffectiveQuarantineRoot, history);
        SetBusy(true);
        StatusText.Text = S.Get("StatusCleaning");
        try
        {
            var report = await Task.Run(() => manager.Execute(list.Select(f => (action, f))));
            MessageBox.Show(
                S.Format("CleanDoneBody", report.MovedCount, Helpers.FormatBytes(report.MovedBytes),
                    report.Skipped.Count, report.QuarantineDir),
                S.Get("CleanDoneTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, S.Get("Error"));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnUsageBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog(this) == true)
        {
            UsageRootBox.Text = dialog.FolderName;
            _ = ScanUsageAsync();
        }
    }

    private void OnLargeBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog(this) == true)
            LargeRootBox.Text = dialog.FolderName;
    }

    private void OnDupBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog(this) == true)
            DupRootBox.Text = dialog.FolderName;
    }
}

public sealed class UsageRow(UsageItem item)
{
    public UsageItem Item => item;

    public string Path => item.Path;

    public string SizeDisplay => Helpers.FormatBytes(item.SizeBytes);

    public string TypeDisplay => item.IsDirectory ? S.Get("TypeDir") : S.Get("TypeFile");
}

/// <summary>可勾选行视图模型（路径 + 两个展示列）。</summary>
public sealed class SelectableRow<T>(T item, string path, string col1, string col2) : INotifyPropertyChanged
{
    private bool _isSelected;

    public T Item => item;

    public string Path => path;

    public string Col1 => col1;

    public string Col2 => col2;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class DupRow : INotifyPropertyChanged
{
    private bool _isSelected;

    public DupRow(DuplicateGroup group, int groupIndex, string path, long sizeBytes, bool isSelected, string tag)
    {
        Group = group;
        GroupIndex = groupIndex;
        Path = path;
        SizeBytes = sizeBytes;
        _isSelected = isSelected;
        Tag = tag;
    }

    public DuplicateGroup Group { get; }

    public int GroupIndex { get; }

    public string Path { get; }

    public long SizeBytes { get; }

    public string Tag { get; }

    public string SizeDisplay => Helpers.FormatBytes(SizeBytes);

    public string TagDisplay => Tag;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
