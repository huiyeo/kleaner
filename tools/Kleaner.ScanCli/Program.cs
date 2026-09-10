using System.Diagnostics;
using System.Text.Json;
using Kleaner.Analysis;
using Kleaner.Core;
using Kleaner.Executor;
using Kleaner.ScanCli;
using Microsoft.Win32;

// Kleaner CLI（MangoDisk 式安全契约）：
//   scan（默认）            只读扫描规则库目标，绝不删除
//   clean --rule a,b       计划清理；必须显式 --apply 才执行；非交互环境还必须 --yes
//   large-files --root R   大文件只读列表
//   duplicates --root R    重复文件只读列表
//   usage --root R         空间占用排行（只读）
// 通用参数：--format text|json（默认 text）、--yes
// 位置覆盖（测试与便携场景用）：--rules P 直接加载指定规则文件（绕过更新通道覆盖）、--quarantine-root R、--history-path F
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

var rulesOverride = Opt("--rules");
var quarantineOverride = Opt("--quarantine-root");
var history = new HistoryManager(Opt("--history-path"));

try
{
    switch (cmd)
    {
        case "scan":
        {
            var rulesPath = rulesOverride ?? BundledRulesPath();
            var set = rulesOverride is null
                ? RuleUpdateService.LoadEffective(rulesPath).Set
                : RuleSetLoader.LoadFromFile(rulesPath);
            var errors = RuleSetLoader.Validate(set);
            if (errors.Count > 0)
                return Fail(json, errors);
            var report = new ScanEngine(quarantineOverride ?? EffectiveQuarantineRoot()).Scan(set);
            Output(json, report, set);
            return 0;
        }
        case "clean":
        {
            var ruleIds = (Opt("--rule") ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.Ordinal);
            var rulesPath = rulesOverride ?? BundledRulesPath();
            var set = rulesOverride is null
                ? RuleUpdateService.LoadEffective(rulesPath).Set
                : RuleSetLoader.LoadFromFile(rulesPath);
            var errors = RuleSetLoader.Validate(set);
            if (errors.Count > 0)
                return Fail(json, errors);

            var chosen = set.Rules.Where(r => ruleIds.Contains(r.Id)).ToList();
            var missing = ruleIds.Except(chosen.Select(r => r.Id), StringComparer.Ordinal).ToList();
            if (missing.Count > 0)
                return Fail(json, new[] { $"未知规则 id：{string.Join(",", missing)}" });

            var effectiveQuarantineRoot = quarantineOverride ?? EffectiveQuarantineRoot();
            var report = new ScanEngine(effectiveQuarantineRoot).Scan(set);
            var selected = report.Results.Where(r => chosen.Any(c => c.Id == r.RuleId) && r.FileCount > 0).ToList();
            var cleanupPlan = CleanupPlanBuilder.Create(set, report, ruleIds, effectiveQuarantineRoot);
            var plan = (Files: cleanupPlan.Items.Count, Bytes: cleanupPlan.Items.Sum(item => item.File.SizeBytes));

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

            var manager = new QuarantineManager(quarantineOverride ?? EffectiveQuarantineRoot(), history);
            var exec = manager.Execute(cleanupPlan);
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
        case "gen-dataset":
        {
            var root = Opt("--root") ?? throw new ArgumentException("gen-dataset 需要 --root");
            var options = new SyntheticDatasetOptions(
                Seed: int.TryParse(Opt("--seed"), out var ds) ? ds : 20260906,
                FileCount: int.TryParse(Opt("--files"), out var fc) ? fc : 2000,
                DirectoryDepth: int.TryParse(Opt("--depth"), out var dp) ? dp : 3,
                Branching: int.TryParse(Opt("--branching"), out var br) ? br : 4,
                DuplicateGroups: int.TryParse(Opt("--dup-groups"), out var dg) ? dg : 20);
            var report = SyntheticDataset.Generate(root, options);
            var manifest = new
            {
                kind = "kleaner-synthetic-dataset",
                options.Seed,
                options.FileCount,
                options.DirectoryDepth,
                options.Branching,
                options.DuplicateGroups,
                report.TotalBytes,
                report.DirectoryCount,
                generatedUtc = DateTime.UtcNow,
            };
            File.WriteAllText(Path.Combine(root, "dataset.json"),
                JsonSerializer.Serialize(manifest, JsonIndented()));
            if (json)
                Console.WriteLine(JsonSerializer.Serialize(manifest, JsonIndented()));
            else
                Console.WriteLine($"已生成 {report.FileCount} 个文件（{Fmt(report.TotalBytes)}），{report.DirectoryCount} 个目录，重复组 {report.DuplicateGroups.Count}。");
            return 0;
        }
        case "bench":
        {
            var root = Opt("--root") ?? throw new ArgumentException("bench 需要 --root");
            var iterations = int.TryParse(Opt("--iterations"), out var it) ? it : 5;
            var scenarios = (Opt("--scenarios") ?? string.Join(",", Benchmarks.AllScenarios))
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var outPath = Opt("--out");
            var self = Environment.ProcessPath ?? throw new InvalidOperationException("无法定位当前可执行文件");
            var manifestPath = Path.Combine(root, "dataset.json");
            var dataset = File.Exists(manifestPath)
                ? JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(manifestPath))
                : throw new ArgumentException("数据集缺少 dataset.json，请先用 gen-dataset 生成");
            var scenariosOut = new List<object>();
            foreach (var scenario in scenarios)
            {
                if (!Benchmarks.AllScenarios.Contains(scenario))
                    return Fail(json, new[] { $"未知场景：{scenario}（可用：{string.Join(",", Benchmarks.AllScenarios)}）" });
                var psi = new ProcessStartInfo(self, $"bench-single {scenario} --root \"{root}\" --iterations {iterations}")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                using var child = Process.Start(psi) ?? throw new InvalidOperationException("子进程启动失败");
                var stdout = child.StandardOutput.ReadToEnd();
                child.StandardError.ReadToEnd();
                child.WaitForExit();
                if (child.ExitCode != 0)
                    return Fail(json, new[] { $"场景 {scenario} 测量失败（退出码 {child.ExitCode}）{stdout}" });
                var raw = JsonSerializer.Deserialize<JsonElement>(stdout, CamelCase());
                var samples = raw.GetProperty("elapsedMs").EnumerateArray().Select(e => e.GetDouble()).OrderBy(x => x).ToList();
                var usage = raw.GetProperty("usage");
                scenariosOut.Add(new
                {
                    name = scenario,
                    iterations,
                    elapsedMs = samples,
                    p50Ms = Math.Round(Benchmarks.Percentile(samples, 50), 2),
                    p95Ms = Math.Round(Benchmarks.Percentile(samples, 95), 2),
                    firstProgressMs = raw.TryGetProperty("firstProgressMs", out var fp) && fp.ValueKind == JsonValueKind.Array
                        ? fp.EnumerateArray().Select(e => Math.Round(e.GetDouble(), 2)).ToList() : null,
                    cancelLatencyMs = raw.TryGetProperty("cancelLatencyMs", out var cl) && cl.ValueKind == JsonValueKind.Array
                        ? cl.EnumerateArray().Select(e => Math.Round(e.GetDouble(), 2)).ToList() : null,
                    peakWorkingSetBytes = usage.GetProperty("peakWorkingSetBytes").GetInt64(),
                    cpuTimeMs = usage.GetProperty("cpuTimeMs").GetDouble(),
                    diskReadBytes = usage.TryGetProperty("diskReadBytes", out var drb) ? drb.GetInt64() : 0,
                    diskWriteBytes = usage.TryGetProperty("diskWriteBytes", out var dwb) ? dwb.GetInt64() : 0,
                });
            }

            var payload = new
            {
                environment = new
                {
                    machine = Environment.MachineName,
                    os = Environment.OSVersion.VersionString,
                    osBuild = Environment.OSVersion.Version.Build,
                    processorCount = Environment.ProcessorCount,
                    runtime = Environment.Version.ToString(),
                    timestampUtc = DateTime.UtcNow,
                },
                dataset,
                scenarios = scenariosOut,
            };
            var text = JsonSerializer.Serialize(payload, JsonIndented());
            if (outPath is not null)
                File.WriteAllText(outPath, text);
            Console.WriteLine(text);
            return 0;
        }
        case "bench-single":
        {
            var scenario = args.Length > 1 ? args[1] : throw new ArgumentException("bench-single 需要场景名");
            var root = Opt("--root") ?? throw new ArgumentException("bench-single 需要 --root");
            var iterations = int.TryParse(Opt("--iterations"), out var it2) ? it2 : 5;
            var result = Benchmarks.Run(scenario, root, rulesOverride, iterations);
            var usage = Benchmarks.SelfResourceUsage();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                result.Scenario,
                result.Iterations,
                result.ElapsedMs,
                result.FirstProgressMs,
                result.CancelLatencyMs,
                usage,
            }, CamelCase()));
            return 0;
        }
        case "governance-report":
        {
            // Phase 2 治理指标：纯规则库只读测量，不扫描文件系统、不写任何状态。
            var rulesPath = rulesOverride ?? BundledRulesPath();
            var set = RuleSetLoader.LoadFromFile(rulesPath);
            var errors = RuleSetLoader.Validate(set);
            if (errors.Count > 0)
                return Fail(json, errors);
            var report = RuleGovernance.Report(set);
            if (json)
                Console.WriteLine(JsonSerializer.Serialize(report, JsonIndented()));
            else
            {
                Console.WriteLine($"规则总数            {report.TotalRules,6}");
                Console.WriteLine($"证据覆盖率          {report.EvidenceCoverage,8:P1}  （{report.EvidenceCoveredRules}/{report.TotalRules}）");
                Console.WriteLine($"验证覆盖率          {report.VerifiedCoverage,8:P1}  （{report.VerifiedRules}/{report.TotalRules}）");
                Console.WriteLine($"默认勾选规则数      {report.DefaultSelectableRules,6}");
                Console.WriteLine($"分类覆盖            {report.CategoriesCovered,3} / {report.TotalCategories}");
                Console.WriteLine($"唯一目标根          {report.UniqueTargetRoots,6}");
            }
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
    System.Console.WriteLine("  gen-dataset --root R [--seed N] [--files N] [--depth N] [--branching N] [--dup-groups N]");
    System.Console.WriteLine("                                生成可复现合成数据集（性能基准用，只写临时目录）");
    System.Console.WriteLine("  bench --root R [--iterations 5] [--scenarios csv] [--out F]");
    System.Console.WriteLine("                                规则扫描/空间分析/大文件/重复哈希/取消延迟 基准（JSON 输出）");
    System.Console.WriteLine("  governance-report [--rules P] 治理指标只读测量（证据/验证覆盖率、目标根等）");
    System.Console.WriteLine("  通用：--format text|json   --yes");
    System.Console.WriteLine("  位置覆盖：--rules P（直接加载，绕过更新通道）  --quarantine-root R  --history-path F");
}

static JsonSerializerOptions JsonIndented() => new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

// bench 父子进程之间的结果契约统一 camelCase；与对外 --format json 的 PascalCase 输出互不影响。
static JsonSerializerOptions CamelCase() => new()
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};

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
