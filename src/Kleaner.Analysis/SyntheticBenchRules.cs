using System.Text.Json;

namespace Kleaner.Analysis;

/// <summary>
/// 基准专用规则文件（bench-rules.json），与 gen-dataset 配套：把 bench 的 rules-scan / cancel
/// 场景锚定在合成数据集上。此前的 bench 未传 --rules，两个场景回退到捆绑的真实规则库（906 个
/// 路径模式）扫描真实用户目录，峰值内存与时延随机器临时/缓存目录的内容漂移，跨日不可复现
/// （2026-09-11 定位，见 docs/performance-baseline.md「内存口径异常」）。
/// 模式组合覆盖 GlobScanner 的全部分支：精确路径、末段通配、中间通配+递归、** 递归、exclude
/// 整路径排除与 keepNewest 版本保留。仅作测量夹具，不代表真实规则，不进入任何清理链路。
/// </summary>
public static class SyntheticBenchRules
{
    public const string FileName = "bench-rules.json";

    /// <summary>在数据集根目录写出 bench-rules.json，返回文件路径。</summary>
    public static string Write(string datasetRoot, SyntheticDatasetOptions options, SyntheticDatasetReport report)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(report);
        if (report.DuplicateGroups.Count == 0)
            throw new ArgumentException("数据集报告缺少重复组，无法构造精确路径规则", nameof(report));

        // ** 覆盖零层及以上：根目录直属文件由 "先试当前目录" 分支命中
        var recursiveAll = Path.Combine(datasetRoot, "**", "*");
        // 只命中目录内、编号 000000-000009 的文件（** 在文件名前要求至少一层目录）
        var numberSubset = Path.Combine(datasetRoot, "**", "file-0000*.bin");
        // 深度不足 2 时退化为根层通配，保证规则集在任何合法参数下仍然有效
        var flatWildcard = options.DirectoryDepth >= 2
            ? Path.Combine(datasetRoot, "dir-02-*", "file-*.bin")
            : Path.Combine(datasetRoot, "file-*.bin");

        var rules = new List<object>
        {
            Rule(
                Id: "bench-recursive",
                Name: "基准-全树递归",
                Category: "temp",
                Paths: [recursiveAll],
                Exclude: [numberSubset],
                AgeDays: 0,
                KeepNewest: null,
                Notes: "基准夹具：递归枚举整个合成数据集并排除编号前缀文件，模拟缓存类规则的宽匹配与排除组合。"),
            Rule(
                Id: "bench-branch",
                Name: "基准-分支递归与版本保留",
                Category: "dev-cache",
                Paths: [Path.Combine(datasetRoot, "dir-01-*", "**")],
                Exclude: [],
                AgeDays: null,
                KeepNewest: 3,
                Notes: "基准夹具：中间通配段加递归枚举第一层分支目录，keepNewest 走版本保留选择路径。"),
            Rule(
                Id: "bench-flat",
                Name: "基准-单层通配",
                Category: "browser-cache",
                Paths: [flatWildcard],
                Exclude: [],
                AgeDays: 0,
                KeepNewest: null,
                Notes: "基准夹具：中间通配段加末段通配的非递归枚举，覆盖目录逐层匹配分支。"),
            Rule(
                Id: "bench-subset",
                Name: "基准-编号子集",
                Category: "temp",
                Paths: [numberSubset],
                Exclude: [],
                AgeDays: 0,
                KeepNewest: null,
                Notes: "基准夹具：递归枚举编号前缀文件的小集合，模拟小命中量的规则形态。"),
            Rule(
                Id: "bench-exact",
                Name: "基准-精确路径",
                Category: "application",
                Paths: report.DuplicateGroups[0].ToArray(),
                Exclude: [],
                AgeDays: 0,
                KeepNewest: null,
                Notes: "基准夹具：重复组副本的精确路径枚举，覆盖无通配符的精确文件分支。"),
        };

        var payload = new { schemaVersion = 1, rules };
        var path = Path.Combine(datasetRoot, FileName);
        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    private static object Rule(
        string Id, string Name, string Category, string[] Paths, string[] Exclude,
        int? AgeDays, int? KeepNewest, string Notes) => new
    {
        id = Id,
        name = Name,
        category = Category,
        risk = "low",
        paths = Paths,
        exclude = Exclude,
        ageDays = AgeDays,
        keepNewest = KeepNewest,
        requiresElevation = false,
        enabled = true,
        safetyNotes = Notes,
    };
}
