using Kleaner.App;
using Kleaner.Executor;

namespace Kleaner.Core.Tests;

public sealed class StartupWindowCoordinatorTests
{
    public StartupWindowCoordinatorTests() => S.Load();

    [Fact]
    public void DisableSelected_HklmItemConfirmsBeforeCallingManager()
    {
        var manager = new FakeStartupManager();
        var dialog = new FakeDialog { ElevationAccepted = false };
        var item = NewItem(requiresElevation: true);
        var row = new StartupRow(item) { IsSelected = true };
        var coordinator = new StartupWindowCoordinator(manager, dialog, () => false);

        var cancelled = coordinator.DisableSelected([row]);

        Assert.Equal(1, dialog.ElevationConfirmations);
        Assert.Empty(manager.Disabled);
        Assert.False(cancelled.Refresh);

        dialog.ElevationAccepted = true;
        var completed = coordinator.DisableSelected([row]);

        Assert.Equal(2, dialog.ElevationConfirmations);
        Assert.Equal(item, Assert.Single(manager.Disabled));
        Assert.True(completed.Refresh);
    }

    [Fact]
    public void DisableAndRestore_SelectedFileRowsForwardAndRefresh()
    {
        var manager = new FakeStartupManager();
        var dialog = new FakeDialog();
        var item = NewItem(requiresElevation: false, kind: StartupKind.File);
        var enabled = new StartupRow(item) { IsSelected = true };
        var disabled = new StartupRow(new DisabledStartup(
            item.Id, item.Name, item.Command, item.Location, nameof(StartupKind.File), null,
            item.KeyPath, item.ValueName, null, DateTime.UtcNow)) { IsSelected = true };
        var coordinator = new StartupWindowCoordinator(manager, dialog, () => false);

        var disabledResult = coordinator.DisableSelected([enabled]);
        var restoredResult = coordinator.RestoreSelected([disabled]);

        Assert.Equal(item, Assert.Single(manager.Disabled));
        Assert.Equal(item.Id, Assert.Single(manager.Restored));
        Assert.True(disabledResult.Refresh);
        Assert.True(restoredResult.Refresh);
        Assert.Equal(0, dialog.ElevationConfirmations);
    }

    [Fact]
    public void DisableSelected_ReportsPartialFailureAndStillRefreshes()
    {
        var manager = new FakeStartupManager { FailingId = "fail" };
        var dialog = new FakeDialog();
        var coordinator = new StartupWindowCoordinator(manager, dialog, () => true);
        var rows = new[]
        {
            new StartupRow(NewItem(id: "ok")) { IsSelected = true },
            new StartupRow(NewItem(id: "fail")) { IsSelected = true },
        };

        var result = coordinator.DisableSelected(rows);

        Assert.Single(manager.Disabled);
        Assert.True(result.Refresh);
        Assert.Contains("1", result.StatusText);
        Assert.Contains("Fail App", result.StatusText);
    }

    private static StartupItem NewItem(
        string id = "file|demo.lnk",
        bool requiresElevation = false,
        StartupKind kind = StartupKind.Registry) =>
        new(id, id == "fail" ? "Fail App" : "Demo App", "demo.exe", "C:\\Startup", kind,
            kind == StartupKind.Registry ? StartupHive.LocalMachine : null,
            "Run", "demo", requiresElevation);

    private sealed class FakeStartupManager : IStartupManager
    {
        public List<StartupItem> Disabled { get; } = new();

        public List<string> Restored { get; } = new();

        public string? FailingId { get; init; }

        public IReadOnlyList<StartupItem> Enumerate() => [];

        public IReadOnlyList<DisabledStartup> ListDisabled() => [];

        public void Disable(StartupItem item)
        {
            if (item.Id == FailingId)
                throw new InvalidOperationException("模拟删除失败");
            Disabled.Add(item);
        }

        public void Restore(string id) => Restored.Add(id);
    }

    private sealed class FakeDialog : IStartupWindowDialog
    {
        public bool ElevationAccepted { get; set; } = true;

        public int ElevationConfirmations { get; private set; }

        public List<string> InfoMessages { get; } = new();

        public void ShowInfo(string message, string title) => InfoMessages.Add($"{title}: {message}");

        public bool ConfirmElevation(string message, string title)
        {
            ElevationConfirmations++;
            return ElevationAccepted;
        }
    }
}
