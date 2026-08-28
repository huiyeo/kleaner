using Kleaner.Core;
using Kleaner.Executor;

// 无界面只读扫描器：加载规则库 → 全量扫描 → 按规则打印可释放量。绝不执行清理。
// 诊断模式：ScanCli enum <路径模式> 直接打印通配匹配结果。
if (args.Length == 2 && args[0] == "enum")
{
    foreach (var f in GlobScanner.EnumerateFiles(args[1]))
        Console.WriteLine(f);
    return 0;
}

var bundled = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "rules", "rules.v1.json");
bundled = Path.GetFullPath(bundled);
var (source, set) = RuleUpdateService.LoadEffective(bundled);

var errors = RuleSetLoader.Validate(set);
if (errors.Count > 0)
{
    Console.WriteLine("规则校验失败：");
    foreach (var e in errors)
        Console.WriteLine("  - " + e);
    return 1;
}

Console.WriteLine($"规则来源：{source}（{set.Rules.Count} 条）");
Console.WriteLine();

var engine = new ScanEngine();
var report = engine.Scan(set);

long totalFiles = 0, totalBytes = 0;
foreach (var r in report.Results.OrderByDescending(r => r.TotalBytes))
{
    var elev = r.RequiresElevation ? "[管理员] " : "";
    var size = Format(r.TotalBytes);
    Console.WriteLine($"{elev}{r.RuleName,-24} {r.FileCount,6} 个文件  {size,10}");
    totalFiles += r.FileCount;
    totalBytes += r.TotalBytes;
}

Console.WriteLine();
Console.WriteLine($"合计：{totalFiles} 个文件，可释放 {Format(totalBytes)}（本次为只读预览，未删除任何文件）");

if (report.Errors.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("扫描异常：");
    foreach (var e in report.Errors)
        Console.WriteLine("  - " + e);
}

static string Format(long bytes) =>
    bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):F2} GB"
    : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):F1} MB"
    : $"{bytes / 1024.0:F0} KB";

return 0;
