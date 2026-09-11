using Kleaner.Analysis;
using Kleaner.Core;

namespace Kleaner.Core.Tests;

/// <summary>bench-rules.json 夹具：规则可加载、可校验，且在合成数据集上产出候选、exclude 实际生效。</summary>
public sealed class SyntheticBenchRulesTests
{
    private static (string Root, SyntheticDatasetOptions Options, SyntheticDatasetReport Report) GenerateDataset(
        int files = 30, int depth = 2, int branching = 2, int dupGroups = 2)
    {
        var root = Path.Combine(Path.GetTempPath(), "kleaner-bench-rules-" + Guid.NewGuid().ToString("N"));
        var options = new SyntheticDatasetOptions(Seed: 20260911, FileCount: files, DirectoryDepth: depth,
            Branching: branching, DuplicateGroups: dupGroups);
        return (root, options, SyntheticDataset.Generate(root, options));
    }

    [Fact]
    public void 规则文件可加载并通过语义校验()
    {
        var (root, options, report) = GenerateDataset();
        try
        {
            var path = SyntheticBenchRules.Write(root, options, report);
            Assert.True(File.Exists(path));

            var set = RuleSetLoader.LoadFromFile(path);
            Assert.Empty(RuleSetLoader.Validate(set));
            Assert.Equal(5, set.Rules.Count);
            Assert.All(set.Rules, r => Assert.All(r.Paths, p =>
                Assert.True(Path.IsPathRooted(p), $"模式必须是绝对路径：{p}")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 数据集作用域扫描产出候选且排除生效()
    {
        var (root, options, report) = GenerateDataset();
        try
        {
            var path = SyntheticBenchRules.Write(root, options, report);
            var set = RuleSetLoader.LoadFromFile(path);

            // 传 --rules 时 root 不是隔离区排除根（与 Benchmarks.RunOnce 的语义一致）
            var scan = new ScanEngine(null).Scan(set);
            Assert.Empty(scan.Errors);

            var recursive = scan.Results.Single(r => r.RuleId == "bench-recursive");
            var subset = scan.Results.Single(r => r.RuleId == "bench-subset");
            var exact = scan.Results.Single(r => r.RuleId == "bench-exact");

            Assert.True(recursive.FileCount > 0, "全树递归规则必须命中数据集文件");
            Assert.True(subset.FileCount > 0, "编号子集规则必须命中");
            // exclude 排除了目录内的 file-0000*.bin，递归规则的命中必须少于数据集总文件数
            Assert.True(recursive.FileCount < options.FileCount,
                $"exclude 未生效：递归规则命中 {recursive.FileCount}，数据集共 {options.FileCount} 个文件");
            Assert.Equal(report.DuplicateGroups[0].Count, exact.FileCount);
            // keepNewest=3：分支规则保留最新 3 份之外的候选
            var branch = scan.Results.Single(r => r.RuleId == "bench-branch");
            Assert.True(branch.FileCount <= report.FileCount, "keepNewest 规则候选数不得越过其枚举总量");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 深度为一的数据集仍生成有效规则()
    {
        var (root, options, report) = GenerateDataset(files: 5, depth: 1, branching: 1, dupGroups: 1);
        try
        {
            var path = SyntheticBenchRules.Write(root, options, report);
            var set = RuleSetLoader.LoadFromFile(path);
            Assert.Empty(RuleSetLoader.Validate(set));

            var scan = new ScanEngine(null).Scan(set);
            Assert.Empty(scan.Errors);
            Assert.True(scan.Results.Sum(r => r.FileCount) > 0, "深度 1 的数据集必须仍有候选命中");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
