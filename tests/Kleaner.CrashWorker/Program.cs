using Kleaner.Core;
using Kleaner.Executor;

// 仅供测试：根目录必须是临时目录下新建的空夹具，不接受用户隔离区。
if (args.Length != 2 || args[0] is not ("clean-moved" or "restore-moved")) return 2;
var root = Path.GetFullPath(args[1]);
var name = Path.GetFileName(root);
if (!string.Equals(Path.GetDirectoryName(root), Path.TrimEndingDirectorySeparator(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
    || !name.StartsWith("kleaner-crash-", StringComparison.Ordinal)
    || !Guid.TryParseExact(name["kleaner-crash-".Length..], "N", out _)
    || !Directory.Exists(root)
    || Directory.EnumerateFileSystemEntries(root).Any()) return 2;
for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
    if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) return 2;

var source = Path.Combine(root, "source");
Directory.CreateDirectory(source);
foreach (var file in new[] { "a.txt", "b.txt" }) File.WriteAllText(Path.Combine(source, file), file + "-content");
var quarantine = Path.Combine(root, "q");
var history = new HistoryManager(Path.Combine(root, "history.jsonl"));
var rule = new Rule("temporary", "临时测试规则", RuleCategory.Application, RiskLevel.Low,
    new[] { Path.Combine(source, "**") }, Array.Empty<string>(), 0, null, false, true,
    "仅限测试临时目录，用于验证进程中断后的文件保留。");
var set = new RuleSet(1, null, 0, new[] { rule });
var plan = CleanupPlanBuilder.Create(set, new ScanEngine(quarantine).Scan(set), new[] { rule.Id }, quarantine);
if (plan.Items.Count != 2) return 3;

var crashing = new QuarantineManager(quarantine, history, snapshot =>
{
    var stop = args[0] == "clean-moved"
        ? snapshot.Entries.Any(entry => entry.State == "moved")
        : snapshot.Entries.Count == 1;
    // 在真实移动及临时清单 Flush 后退出，故意不执行 finally；不模拟断电。
    if (stop) Environment.Exit(73);
});
if (args[0] == "clean-moved") crashing.Execute(plan);
else
{
    var execution = new QuarantineManager(quarantine, history).Execute(plan);
    crashing.RestoreBatch(execution.BatchId);
}
return 4; // 未命中中断点即测试失败。
