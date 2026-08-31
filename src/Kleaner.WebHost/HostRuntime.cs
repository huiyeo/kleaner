using System.Security.Principal;
using System.Text.Json;
using Kleaner.Core;
using Kleaner.Executor;

namespace Kleaner.WebHost;

/// <summary>
/// 宿主运行期解析（工单 12）：规则集来源、隔离区根目录、提权探测。
/// 与 GUI 语义对齐——GUI 读 %APPDATA%\Kleaner\settings.json，CLI 也读同一个文件，WebHost 必须同源。
/// Kleaner.App 的 AppSettings / Helpers 挂在 WPF 工程里，WebHost 不引用它，这里做最小同语义实现。
/// </summary>
internal static class HostRuntime
{
    /// <summary>规则集：seam 优先；否则用户目录覆盖（更新通道下发）→ 内置规则库（随包分发，见 csproj）。</summary>
    public static RuleSet ResolveRuleSet(KleanerWebHostOptions options) =>
        options.RuleSetProvider?.Invoke()
        ?? RuleUpdateService.LoadEffective(Path.Combine(AppContext.BaseDirectory, "rules.v1.json")).Set;

    /// <summary>隔离区根：seam → settings.json 的 QuarantineRoot → QuarantineManager.DefaultRoot（三段与 GUI 同语义）。</summary>
    public static string ResolveQuarantineRoot(KleanerWebHostOptions options) =>
        options.QuarantineRoot ?? ReadSettingsQuarantineRoot() ?? QuarantineManager.DefaultRoot();

    /// <summary>提权判定：seam 优先；否则 WindowsPrincipal 真实判定（语义同 Kleaner.App/Helpers.IsElevated）。</summary>
    public static bool IsElevated(KleanerWebHostOptions options) =>
        options.ElevationProbe?.Invoke() ?? CheckElevated();

    /// <summary>历史管理器（工单 13）：seam 优先；否则默认 %APPDATA%\Kleaner\history.jsonl——与 GUI/CLI 同源，审计不分叉。</summary>
    public static HistoryManager ResolveHistory(KleanerWebHostOptions options) =>
        options.HistoryProvider?.Invoke() ?? new HistoryManager();

    /// <summary>隔离区管理器（工单 13）：seam 优先；否则 ResolveQuarantineRoot + 共享同一 HistoryManager（restore/delete-batch/purge 落历史）。</summary>
    public static QuarantineManager ResolveQuarantine(KleanerWebHostOptions options) =>
        options.QuarantineProvider?.Invoke()
        ?? new QuarantineManager(ResolveQuarantineRoot(options), ResolveHistory(options));

    /// <summary>启动项管理器（工单 13）：seam 优先；否则真实注册表/启动文件夹环境 + 共享同一 HistoryManager（startup-disable/restore 落历史）。</summary>
    public static StartupManager ResolveStartup(KleanerWebHostOptions options) =>
        options.StartupProvider?.Invoke()
        ?? new StartupManager(history: ResolveHistory(options));

    private static string? ReadSettingsQuarantineRoot()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Kleaner", "settings.json");
            if (!File.Exists(path))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("QuarantineRoot", out var value) &&
                value.ValueKind == JsonValueKind.String &&
                value.GetString() is { Length: > 0 } root)
            {
                return root;
            }
        }
        catch
        {
            // settings.json 缺失或损坏：与 GUI 的 AppSettings.Load 同策略，静默回退默认值
        }

        return null;
    }

    private static bool CheckElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
