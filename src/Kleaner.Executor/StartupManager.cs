using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Kleaner.Executor;

public enum StartupKind { Registry, File }

public enum StartupHive { CurrentUser, LocalMachine }

/// <summary>启动项条目（枚举结果）。registry 条目 ValueName=注册表值名；file 条目 ValueName=文件名。</summary>
public sealed record StartupItem(
    string Id,
    string Name,
    string Command,
    string Location,
    StartupKind Kind,
    StartupHive? Hive,
    string KeyPath,
    string ValueName,
    bool RequiresElevation);

/// <summary>已禁用启动项的备份记录（持久化于 %APPDATA%\Kleaner\startup-backup，不过期）。</summary>
public sealed record DisabledStartup(
    string Id,
    string Name,
    string Command,
    string Location,
    string Kind,
    string? Hive,
    string KeyPath,
    string ValueName,
    string? BackupFile,
    DateTime DisabledUtc);

/// <summary>注册表 Run 源：hive + 键路径 + 展示名。</summary>
public sealed record RegistryRunSource(StartupHive Hive, string KeyPath, string HiveDisplay);

/// <summary>
/// 启动项来源环境抽象：注册表 Run 键与启动文件夹。默认走真实实现（WindowsStartupEnvironment），
/// 测试可注入假实现，避免触碰真实注册表与启动目录。
/// </summary>
public interface IStartupEnvironment
{
    IReadOnlyList<RegistryRunSource> RunSources { get; }

    IReadOnlyList<string> StartupDirectories { get; }

    IReadOnlyList<(string Name, string? Data)> EnumerateRunValues(StartupHive hive, string keyPath);

    void DeleteRunValue(StartupHive hive, string keyPath, string valueName);

    bool RunValueExists(StartupHive hive, string keyPath, string valueName);

    void SetRunValue(StartupHive hive, string keyPath, string valueName, string data);
}

/// <summary>启动项业务操作抽象，供界面协调层注入测试替身。</summary>
public interface IStartupManager
{
    IReadOnlyList<StartupItem> Enumerate();

    IReadOnlyList<DisabledStartup> ListDisabled();

    void Disable(StartupItem item);

    void Restore(string id);
}

/// <summary>Windows 真实实现：HKCU/HKLM Run 键与用户/公共启动文件夹；HKLM 修改经 UAC 提权调用 reg.exe。</summary>
public sealed class WindowsStartupEnvironment : IStartupEnvironment
{
    public IReadOnlyList<RegistryRunSource> RunSources { get; } = new[]
    {
        new RegistryRunSource(StartupHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU"),
        new RegistryRunSource(StartupHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM"),
        new RegistryRunSource(StartupHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM"),
    };

    public IReadOnlyList<string> StartupDirectories =>
        new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
        };

    public IReadOnlyList<(string Name, string? Data)> EnumerateRunValues(StartupHive hive, string keyPath)
    {
        using var key = hive == StartupHive.CurrentUser
            ? Registry.CurrentUser.OpenSubKey(keyPath, writable: false)
            : Registry.LocalMachine.OpenSubKey(keyPath, writable: false);
        if (key is null)
            return Array.Empty<(string, string?)>();
        return key.GetValueNames()
            .Where(n => !string.IsNullOrEmpty(n)) // 默认值不作为启动项展示
            .Select(n => (n, key.GetValue(n) as string))
            .ToList();
    }

    public void DeleteRunValue(StartupHive hive, string keyPath, string valueName)
    {
        if (hive == StartupHive.LocalMachine && !IsElevated())
        {
            RunElevatedReg($@"delete ""{keyPath}"" /v ""{valueName}"" /f");
            if (RunValueExists(hive, keyPath, valueName))
                throw new InvalidOperationException("提权删除后值仍存在。");
            return;
        }
        using var key = OpenWritable(hive, keyPath);
        key.DeleteValue(valueName, throwOnMissingValue: true);
    }

    public bool RunValueExists(StartupHive hive, string keyPath, string valueName)
    {
        using var key = hive == StartupHive.CurrentUser
            ? Registry.CurrentUser.OpenSubKey(keyPath)
            : Registry.LocalMachine.OpenSubKey(keyPath);
        return key?.GetValueNames().Any(n => n.Equals(valueName, StringComparison.OrdinalIgnoreCase)) == true;
    }

    public void SetRunValue(StartupHive hive, string keyPath, string valueName, string data)
    {
        if (hive == StartupHive.LocalMachine && !IsElevated())
        {
            RunElevatedReg($@"add ""{keyPath}"" /v ""{valueName}"" /t REG_SZ /d ""{EscapeData(data)}"" /f");
            return;
        }
        using var key = OpenWritable(hive, keyPath);
        key.SetValue(valueName, data);
    }

    private static RegistryKey OpenWritable(StartupHive hive, string keyPath) =>
        (hive == StartupHive.CurrentUser
            ? Registry.CurrentUser.OpenSubKey(keyPath, writable: true)
            : Registry.LocalMachine.OpenSubKey(keyPath, writable: true))
        ?? throw new InvalidOperationException($"注册表键不存在：{keyPath}");

    private static bool IsElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    /// <summary>经 UAC 提权调用 reg.exe 并等待结果；取消 UAC 或返回码非 0 均抛异常。</summary>
    private static void RunElevatedReg(string arguments)
    {
        var psi = new ProcessStartInfo("reg.exe", arguments)
        {
            UseShellExecute = true,
            Verb = "runas",
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"reg.exe 退出码 {p.ExitCode}");
    }

    private static string EscapeData(string data) => data.Replace("\"", "\\\"");
}

/// <summary>
/// 启动项管理（保守版）：枚举 HKCU/HKLM Run 与用户/公共启动文件夹；禁用 = 先写备份记录再移除
/// （注册表删值 / 文件移入备份目录），还原 = 按记录重建。HKLM 修改经 UAC 提权调用 reg.exe。
/// 全部操作写入 history.jsonl 审计。
/// </summary>
public sealed class StartupManager : IStartupManager
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly string _backupDir;
    private readonly HistoryManager _history;
    private readonly IStartupEnvironment _env;

    public StartupManager(string? backupDir = null, HistoryManager? history = null, IStartupEnvironment? env = null)
    {
        _backupDir = backupDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Kleaner", "startup-backup");
        _history = history ?? new HistoryManager();
        _env = env ?? new WindowsStartupEnvironment();
    }

    public string BackupDir => _backupDir;

    /// <summary>枚举当前启用的启动项（注册表 Run 值 + 启动文件夹内文件）。</summary>
    public IReadOnlyList<StartupItem> Enumerate()
    {
        var items = new List<StartupItem>();

        foreach (var source in _env.RunSources)
        {
            foreach (var (name, data) in _env.EnumerateRunValues(source.Hive, source.KeyPath))
            {
                items.Add(new StartupItem(
                    Id: $"reg|{source.HiveDisplay}|{source.KeyPath}|{name}",
                    Name: name,
                    Command: data ?? string.Empty,
                    Location: $"{source.HiveDisplay}\\{source.KeyPath}",
                    Kind: StartupKind.Registry,
                    Hive: source.Hive,
                    KeyPath: source.KeyPath,
                    ValueName: name,
                    RequiresElevation: source.Hive == StartupHive.LocalMachine));
            }
        }

        foreach (var dir in _env.StartupDirectories)
        {
            if (!Directory.Exists(dir))
                continue;
            string[] files;
            try
            {
                files = Directory.GetFiles(dir);
            }
            catch
            {
                continue;
            }
            foreach (var f in files)
            {
                if (Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                    continue; // 文件夹视图元数据，不是启动项
                items.Add(new StartupItem(
                    Id: $"file|{f}",
                    Name: Path.GetFileNameWithoutExtension(f),
                    Command: f,
                    Location: dir,
                    Kind: StartupKind.File,
                    Hive: null,
                    KeyPath: dir,
                    ValueName: Path.GetFileName(f),
                    RequiresElevation: false));
            }
        }

        return items.OrderBy(i => i.Location, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
    }

    /// <summary>已禁用（备份中）的启动项。</summary>
    public IReadOnlyList<DisabledStartup> ListDisabled()
    {
        if (!Directory.Exists(_backupDir))
            return Array.Empty<DisabledStartup>();
        var list = new List<DisabledStartup>();
        foreach (var f in Directory.GetFiles(_backupDir, "*.json"))
        {
            try
            {
                list.Add(JsonSerializer.Deserialize<DisabledStartup>(File.ReadAllText(f), JsonOpts)!);
            }
            catch
            {
                // 单条记录损坏不阻塞整体展示
            }
        }
        return list.OrderByDescending(d => d.DisabledUtc).ToList();
    }

    /// <summary>禁用：先写备份记录，再移除（注册表删值 / 文件移入备份目录）。</summary>
    public void Disable(StartupItem item)
    {
        Directory.CreateDirectory(_backupDir);
        var record = new DisabledStartup(
            item.Id, item.Name, item.Command, item.Location,
            item.Kind.ToString(), item.Hive?.ToString(), item.KeyPath, item.ValueName,
            BackupFile: null, DateTime.UtcNow);
        var recordPath = RecordPath(item.Id);

        if (item.Kind == StartupKind.File)
        {
            var backupFile = Path.Combine(_backupDir, Path.GetFileName(item.ValueName));
            if (File.Exists(backupFile))
                throw new InvalidOperationException($"备份目录已存在同名文件：{backupFile}");
            File.Move(item.Command, backupFile);
            record = record with { BackupFile = backupFile };
            WriteRecord(recordPath, record);
        }
        else
        {
            WriteRecord(recordPath, record); // 先落备份再动注册表
            try
            {
                _env.DeleteRunValue(item.Hive!.Value, item.KeyPath, item.ValueName);
            }
            catch
            {
                TryDelete(recordPath); // 删除失败回滚备份记录
                throw;
            }
        }

        _history.Append("startup-disable", item.Id, 1, 0, "ok");
    }

    /// <summary>还原已禁用的启动项。目标位置已被占用时抛异常（保留备份，不覆盖）。</summary>
    public void Restore(string id)
    {
        var recordPath = RecordPath(id);
        if (!File.Exists(recordPath))
            throw new FileNotFoundException("找不到该启动项的备份记录。");
        var record = JsonSerializer.Deserialize<DisabledStartup>(File.ReadAllText(recordPath), JsonOpts)!;

        if (record.Kind == nameof(StartupKind.File))
        {
            if (record.BackupFile is null || !File.Exists(record.BackupFile))
                throw new FileNotFoundException("备份文件丢失，无法还原。");
            var target = Path.Combine(record.KeyPath, record.ValueName);
            if (File.Exists(target))
                throw new InvalidOperationException($"目标位置已存在文件：{target}");
            File.Move(record.BackupFile, target);
        }
        else
        {
            var hive = Enum.Parse<StartupHive>(record.Hive!);
            _env.SetRunValue(hive, record.KeyPath, record.ValueName, record.Command);
        }

        TryDelete(recordPath);
        _history.Append("startup-restore", id, 1, 0, "ok");
    }

    // ---------- 内部 ----------

    private string RecordPath(string id)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..16].ToLowerInvariant();
        return Path.Combine(_backupDir, $"{hash}.json");
    }

    private static void WriteRecord(string path, DisabledStartup record) =>
        File.WriteAllText(path, JsonSerializer.Serialize(record, JsonOpts));

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // 备份记录删除失败不影响主流程
        }
    }
}
