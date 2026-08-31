using Kleaner.Executor;

namespace Kleaner.WebHost;

/// <summary>隔离区批次的 Web 视图（工单 13）：补 EntryCount / TotalBytes 展示缺口（QuarantineBatch.TotalBytes 标了 JsonIgnore）。</summary>
public sealed record QuarantineBatchView(
    string BatchId,
    DateTime CreatedUtc,
    int EntryCount,
    long TotalBytes,
    IReadOnlyList<QuarantineEntry> Entries)
{
    public static QuarantineBatchView From(QuarantineBatch batch) => new(
        batch.BatchId, batch.CreatedUtc, batch.Entries.Count, batch.TotalBytes, batch.Entries);
}

/// <summary>启用中的启动项 Web 视图（工单 13）：枚举字符串化（kind/hive），前端无需内置枚举表。</summary>
public sealed record StartupItemView(
    string Id,
    string Name,
    string Command,
    string Location,
    string Kind,
    string? Hive,
    string KeyPath,
    string ValueName,
    bool RequiresElevation)
{
    public static StartupItemView From(StartupItem item) => new(
        item.Id, item.Name, item.Command, item.Location,
        item.Kind.ToString().ToLowerInvariant(),
        item.Hive switch
        {
            StartupHive.CurrentUser => "currentUser",
            StartupHive.LocalMachine => "localMachine",
            _ => null,
        },
        item.KeyPath, item.ValueName, item.RequiresElevation);
}

/// <summary>已禁用（备份中）的启动项 Web 视图（工单 13）。</summary>
public sealed record DisabledStartupView(
    string Id,
    string Name,
    string Command,
    string Location,
    string Kind,
    string? Hive,
    string KeyPath,
    string ValueName,
    string? BackupFile,
    DateTime DisabledUtc)
{
    public static DisabledStartupView From(DisabledStartup record) => new(
        record.Id, record.Name, record.Command, record.Location,
        record.Kind.ToLowerInvariant(),
        record.Hive switch
        {
            nameof(StartupHive.CurrentUser) => "currentUser",
            nameof(StartupHive.LocalMachine) => "localMachine",
            _ => null,
        },
        record.KeyPath, record.ValueName, record.BackupFile, record.DisabledUtc);
}

/// <summary>启动项禁用/还原请求体（工单 13）：只传 id，服务端自行重新枚举定位目标，不接受客户端伪造的目标位置。</summary>
public sealed record StartupIdRequest(string Id);
