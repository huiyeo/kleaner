using Kleaner.Core;
using Kleaner.Executor;

// 仅供测试：根目录必须是临时目录下新建的空夹具，不接受用户隔离区。
if (args.Length != 2 || args[0] is not ("clean-moved" or "restore-moved" or "restore-held"
    or "audit-started" or "audit-item" or "audit-prepared" or "audit-finalized" or "audit-appended"
    or "audit-clean-started" or "audit-clean-item" or "audit-clean-prepared" or "audit-clean-finalized" or "audit-clean-appended"
    or "audit-delete-started" or "audit-delete-item" or "audit-delete-prepared" or "audit-delete-finalized" or "audit-delete-appended"
    or "audit-purge-started" or "audit-purge-item" or "audit-purge-prepared" or "audit-purge-finalized" or "audit-purge-appended")) return 2;
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
        : args[0] is ("restore-moved" or "restore-held") && snapshot.Entries.Count == 1;
    // 在真实移动及临时清单 Flush 后退出，故意不执行 finally；不模拟断电。
    if (stop)
    {
        if (args[0] == "restore-held")
        {
            Console.WriteLine("held");
            Console.Out.Flush();
            Console.ReadLine(); // 父测试完成并发拒绝验证后才允许退出。
        }
        Environment.Exit(73);
    }
}, stage =>
{
    if (args[0] == "audit-" + stage) Environment.Exit(73);
});
if (args[0] is "clean-moved" or "audit-clean-started" or "audit-clean-item" or "audit-clean-prepared" or "audit-clean-finalized" or "audit-clean-appended") crashing.Execute(plan);
else if (args[0] is "audit-delete-started" or "audit-delete-item" or "audit-delete-prepared" or "audit-delete-finalized" or "audit-delete-appended")
{
    var execution = new QuarantineManager(quarantine, history).Execute(plan);
    crashing.DeleteBatch(execution.BatchId);
}
else if (args[0] is "audit-purge-started" or "audit-purge-item" or "audit-purge-prepared" or "audit-purge-finalized" or "audit-purge-appended")
{
    new QuarantineManager(quarantine, history).Execute(plan);
    crashing.PurgeOlderThan(TimeSpan.Zero);
}
else
{
    var execution = new QuarantineManager(quarantine, history).Execute(plan);
    crashing.RestoreBatch(execution.BatchId);
}
return 4; // 未命中中断点即测试失败。
