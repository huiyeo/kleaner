using System.Text.Json;
using Kleaner.Analysis;
using Kleaner.Core;
using Kleaner.Executor;
using Microsoft.Win32;

// Kleaner CLI（MangoDisk 式安全契约）：
//   scan（默认）            只读扫描规则库目标，绝不删除
//   clean --rule a,b       计划清理；必须显式 --apply 才执行；非交互环境还必须 --yes
//   large-files --root R   大文件只读列表
//   duplicates --root R    重复文件只读列表
//   usage --root R         空间占用排行（只读）
// 通用参数：--format text|json（默认 text）、--yes
// 任何删除动作：默认 dry-run；非交互（输入重定向）无 --yes 时拒绝执行并以退出码 2 结束。

if (args.Contains("--help") || args.Contains("-h") || (args.Length > 0 && args[0] == "help"))
{
    Usage();
    return 0;
}

var yes = args.Contains("--yes");
var json = args.Contains("--format") &&
           Array.IndexOf(args, "--format") + 1 < args.Length &&
           args[Array.IndexOf(args, "--format") + 1] == "json";
var cmd = args.Length > 0 && !args[0].StartsWith('-') ? args[0] : "scan";
string? Opt(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

var history = new HistoryManager();

try
{
    switch (cmd)
    {
        case "scan":
        {
            var (source, set) = RuleUpdateService.LoadEffective(BundledRulesPath());
            var errors = RuleSetLoader.Validate(set);
            if (errors.Count > 0)
                return Fail(json, errors);
            var report = new ScanEngine(EffectiveQuarantineRoot()).Scan(set);
            Output(json, report, set);
            return 0;
        }
        case "clean":
        {
            var ruleIds = (Opt("--rule") ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.Ordinal);
            var (_, set) = RuleUpdateService.LoadEffective(BundledRulesPath());
            var errors = RuleSetLoader.Validate(set);
            if (errors.Count > 0)
                return Fail(json, errors);

            var chosen = set.Rules.Where(r => ruleIds.Contains(r.Id)).ToList();
            var missing = ruleIds.Except(chosen.Select(r => r.Id), StringComparer.Ordinal).ToList();
            if (missing.Count > 0)
                return Fail(json, new[] { $"未知规则 id：{string.Join(",", missing)}" });

            var report = new ScanEngine(EffectiveQuarantineRoot()).Scan(set);
            var selected = report.Results.Where(r => chosen.Any(c => c.Id == r.RuleId) && r.FileCount > 0).ToList();
            var plan = (Files: selected.Sum(r => r.FileCount), Bytes: selected.Sum(r => r.TotalBytes));

            if (!args.Contains("--apply"))
            {
                // dry-run：只打印计划
                EmitPlan(json, selected, plan);
                return 0;
            }

            var interactive = !System.Console.IsInputRedirected;
            if (!yes)
            {
                if (!interactive)
                {
                    System.Console.Error.WriteLine("非交互环境执行删除必须显式传入 --yes（安全契约）。");
                    return 2;
                }
                System.Console.Write($"将把 {plan.Files} 个文件（{Fmt(plan.Bytes)}）移入隔离区，确认？[y/N] ");
                var answer = System.Console.ReadLine();
                if (!string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
                {
                    System.Console.WriteLine("已取消。");
                    history.Append("cli-clean", "用户取消", 0, 0, "cancelled");
                    return 0;
                }
            }

            var manager = new QuarantineManager(EffectiveQuarantineRoot(), history);
            var items = selected.SelectMany(r => r.Files.Select(f => (r.RuleId, f))).ToList();
            var exec = manager.Execute(items);
            if (json)
                System.Console.WriteLine(JsonSerializer.Serialize(new
                {
                    applied = true,
                    batchId = exec.BatchId,
                    moved = exec.MovedCount,
                    bytes = exec.MovedBytes,
                    skipped = exec.Skipped.Count,
                }, new JsonSerializerOptions { WriteIndented = true }));
            else
                System.Console.WriteLine($"已移入隔离区 {exec.MovedCount} 个文件（{Fmt(exec.MovedBytes)}），跳过 {exec.Skipped.Count}。批次 {exec.BatchId}");
            return 0;
        }
        case "large-files":
        {
            var root = Opt("--root") ?? throw new ArgumentException("large-files 需要 --root");
            var minMb = long.TryParse(Opt("--min-mb"), out var m) ? m : 100;
            var top = int.TryParse(Opt("--top"), out var t) ? t : 50;
            var items = LargeFileScanner.Scan(root, minMb * 1024L * 1024, top);
            if (json)
                Console.WriteLine(JsonSerializer.Serialize(items, JsonIndented()));
            else
                foreach (var i in items)
                    Console.WriteLine($"{Fmt(i.SizeBytes),12}  {i.LastWriteTimeUtc.ToLocalTime():yyyy-MM-dd}  {i.Path}");
            return 0;
        }
        case "duplicates":
        {
            var root = Opt("--root") ?? throw new ArgumentException("duplicates 需要 --root");
            var minMb = long.TryParse(Opt("--min-mb"), out var m) ? m : 1;
            var groups = DuplicateFinder.Find(root, minMb * 1024L * 1024);
            if (json)
                Console.WriteLine(JsonSerializer.Serialize(groups, JsonIndented()));
            else
                foreach (var g in groups)
                {
                    Console.WriteLine($"# 重复组 {g.SizeBytes} x {g.Files.Count}  可回收 {Fmt(g.SizeBytes * (g.Files.Count - 1))}");
                    foreach (var f in g.Files)
                        Console.WriteLine($"    {f}");
                }
            return 0;
        }
        case "usage":
        {
            var root = Opt("--root") ?? throw new ArgumentException("usage 需要 --root");
            var top = int.TryParse(Opt("--top"), out var t) ? t : 30;
            var items = DiskUsageAnalyzer.TopLevel(root).Take(top);
            if (json)
                Console.WriteLine(JsonSerializer.Serialize(items, JsonIndented()));
            else
                foreach (var i in items)
                    Console.WriteLine($"{Fmt(i.SizeBytes),12}  {(i.IsDirectory ? "[目录]" : "[文件]")}  {i.Path}");
            return 0;
        }
        case "startup":
        {
            var manager = new StartupManager();
            var items = manager.Enumerate();
            var disabled = manager.ListDisabled();
            if (json)
                Console.WriteLine(JsonSerializer.Serialize(new { enabled = items, disabled }, JsonIndented()));
            else
            {
                foreach (var i in items)
                    Console.WriteLine($"[启用]   {(i.RequiresElevation ? "管理员 " : "      ")}{i.Name,-28}  {i.Command}");
                foreach (var d in disabled)
                    Console.WriteLine($"[已禁用]         {d.Name,-28}  {d.Command}");
                Console.WriteLine();
                Console.WriteLine($"共 {items.Count} 项启用，{disabled.Count} 项已禁用（禁用备份：{manager.BackupDir}）");
            }
            return 0;
        }
        case "startup-test":
        {
            // 自检：临时 HKCU Run 值与启动文件夹文件 → 禁用 → 还原 → 校验一致 → 清理
            const string valueName = "KleanerSelfTest";
            const string valueData = "\"C:\\Program Files\\KleanerSelfTest\\demo.exe\" /x";
            const string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
            var manager = new StartupManager();
            var failures = new List<string>();

            Registry.SetValue(@"HKEY_CURRENT_USER\" + keyPath, valueName, valueData);
            var notePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Startup), "KleanerSelfTestNote.txt");
            File.WriteAllText(notePath, "kleaner startup self-test");
            try
            {
                var regItem = manager.Enumerate().SingleOrDefault(i => i.Id.EndsWith("|" + valueName));
                if (regItem is null) failures.Add("枚举未发现测试注册表项");
                else
                {
                    manager.Disable(regItem);
                    var gone = Registry.GetValue(@"HKEY_CURRENT_USER\" + keyPath, valueName, null) is null;
                    if (!gone) failures.Add("禁用后注册表值仍存在");
                    manager.Restore(regItem.Id);
                    var back = Registry.GetValue(@"HKEY_CURRENT_USER\" + keyPath, valueName, null) as string;
                    if (back != valueData) failures.Add("还原后注册表值数据不一致");
                }

                var fileItem = manager.Enumerate().SingleOrDefault(i => i.Id == $"file|{notePath}");
                if (fileItem is null) failures.Add("枚举未发现测试文件项");
                else
                {
                    manager.Disable(fileItem);
                    if (File.Exists(notePath)) failures.Add("禁用后启动文件夹文件仍存在");
                    manager.Restore(fileItem.Id);
                    if (!File.Exists(notePath)) failures.Add("还原后启动文件夹文件丢失");
                }
            }
            finally
            {
                // 测试数据清理：无论往返是否成功，都不残留测试启动项
                try
                {
                    using var k = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
                    k?.DeleteValue(valueName, throwOnMissingValue: false);
                }
                catch { }
                try { if (File.Exists(notePath)) File.Delete(notePath); } catch { }
            }

            if (failures.Count == 0)
            {
                Console.WriteLine("startup-test PASS（注册表与文件项禁用/还原往返一致，测试数据已清理）");
                return 0;
            }
            foreach (var f in failures)
                Console.Error.WriteLine("FAIL: " + f);
            return 1;
        }
        default:
            System.Console.Error.WriteLine($"未知命令：{cmd}");
            Usage();
            return 1;
    }
}
catch (Exception ex)
{
    System.Console.Error.WriteLine($"错误：{ex.Message}");
    return 1;
}

static int Fail(bool json, IReadOnlyList<string> errors)
{
    if (json)
        System.Console.WriteLine(JsonSerializer.Serialize(new { errors }, JsonIndented()));
    else
        foreach (var e in errors)
            System.Console.Error.WriteLine("规则错误：" + e);
    return 1;
}

static void EmitPlan(bool json, IReadOnlyList<RuleScanResult> selected, (int Files, long Bytes) plan)
{
    if (json)
        System.Console.WriteLine(JsonSerializer.Serialize(new
        {
            dryRun = true,
            files = plan.Files,
            bytes = plan.Bytes,
            rules = selected.Select(r => new { id = r.RuleId, files = r.FileCount, bytes = r.TotalBytes }),
        }, JsonIndented()));
    else
    {
        System.Console.WriteLine($"[dry-run] 共 {plan.Files} 个文件，{Fmt(plan.Bytes)}。加 --apply 执行；非交互环境还需 --yes。");
        foreach (var r in selected)
            System.Console.WriteLine($"  {r.RuleName,-24} {r.FileCount,6} 个文件  {Fmt(r.TotalBytes),10}");
    }
}

static void Output(bool json, ScanReport report, RuleSet set)
{
    if (json)
        System.Console.WriteLine(JsonSerializer.Serialize(report, JsonIndented()));
    else
    {
        long tf = 0, tb = 0;
        foreach (var r in report.Results.OrderByDescending(r => r.TotalBytes))
        {
            System.Console.WriteLine($"{r.RuleName,-24} {r.FileCount,6} 个文件  {Fmt(r.TotalBytes),10}");
            tf += r.FileCount;
            tb += r.TotalBytes;
        }
        System.Console.WriteLine();
        System.Console.WriteLine($"合计：{tf} 个文件，可释放 {Fmt(tb)}（只读预览，未删除任何文件）");
    }
}

static void Usage()
{
    System.Console.WriteLine("Kleaner CLI — 白名单清理工具的命令行形态");
    System.Console.WriteLine("  scan                          只读扫描规则库目标（默认命令）");
    System.Console.WriteLine("  clean --rule id1,id2          计划清理指定规则；--apply 才执行，非交互必须再加 --yes");
    System.Console.WriteLine("  large-files --root R [--min-mb 100] [--top 50]");
    System.Console.WriteLine("  duplicates  --root R [--min-mb 1]");
    System.Console.WriteLine("  usage       --root R [--top 30]");
    System.Console.WriteLine("  startup                       只读列出启动项（含已禁用备份）");
    System.Console.WriteLine("  startup-test                  启动项禁用/还原往返自检（临时测试项，自动清理）");
    System.Console.WriteLine("  通用：--format text|json   --yes");
}

static JsonSerializerOptions JsonIndented() => new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

static string Fmt(long bytes) =>
    bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):F2} GB"
    : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):F1} MB"
    : $"{bytes / 1024.0:F0} KB";

static string BundledRulesPath() =>
    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "rules", "rules.v1.json"));

static string? AppSettingsRoot()
{
    try
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kleaner", "settings.json");
        if (!File.Exists(path))
            return null;
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.TryGetProperty("QuarantineRoot", out var v)
            ? v.GetString()
            : null;
    }
    catch
    {
        return null;
    }
}

/// <summary>生效隔离区根：设置覆盖优先，缺省回退到剩余空间最大的非系统盘（与 GUI 一致）。</summary>
static string EffectiveQuarantineRoot() => AppSettingsRoot() ?? QuarantineManager.DefaultRoot();

