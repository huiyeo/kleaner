using System.Security.Cryptography;
using Kleaner.Core;
using Kleaner.Executor;

namespace Kleaner.WebHost;

/// <summary>
/// WebHost 运行选项。生产入口 <see cref="Program"/> 用默认值（端口被占回退随机高端口）；
/// 集成测试通过对象初始化器覆盖（UseTestServer / 固定 token / 关闭空闲退出）。
/// </summary>
public sealed record KleanerWebHostOptions
{
    /// <summary>默认首选端口（工单 03：被外部进程占用则回退随机高端口并更新状态文件）。</summary>
    public const int DefaultPreferredPort = 45172;

    /// <summary>实际监听端口。生产由 <see cref="PortPicker.PickFreePort"/> 决定后回填；
    /// 测试随意指定——它只参与 Host / Origin 校验的期望值。</summary>
    public int Port { get; init; } = DefaultPreferredPort;

    /// <summary>启动 token。null 时由宿主启动流程随机生成（每次进程启动都不同）。</summary>
    public string? Token { get; init; }

    /// <summary>空闲宽限期：无进行中 job 且无 in-flight 请求持续该秒数后进程退出。</summary>
    public int IdleGraceSeconds { get; init; } = 30;

    /// <summary>空闲检查轮询间隔。</summary>
    public int IdleCheckIntervalSeconds { get; init; } = 5;

    /// <summary>空闲自动退出开关（工单 03 决策）。测试里关闭以免干扰。</summary>
    public bool EnableIdleExit { get; init; } = true;

    /// <summary>true 时挂 TestServer 而非 Kestrel（仅供集成测试）。</summary>
    public bool UseTestServer { get; init; }

    /// <summary>TestServer 的静态资源根目录；生产由 Web SDK 的 MapStaticAssets 清单托管。</summary>
    public string? TestStaticWebRoot { get; init; }

    /// <summary>内容根目录覆盖；生产入口固定到可执行文件目录，避免从任意工作目录启动时丢失 PWA 回退文件。</summary>
    public string? ContentRootPath { get; init; }

    /// <summary>service.json 所在目录；null 时为 %APPDATA%\Kleaner。测试可指向临时目录。</summary>
    public string? ServiceStateDirectory { get; init; }

    /// <summary>settings.json 路径覆盖；null 时与 GUI/CLI 共用 %APPDATA%\Kleaner\settings.json。仅测试使用覆盖。</summary>
    public string? SettingsFilePath { get; init; }

    /// <summary>规则更新执行 seam；生产为 RuleUpdateService.CheckAndUpdateAsync，测试不访问网络。</summary>
    public Func<string, string, Task<string?>>? RuleUpdateExecutor { get; init; }

    /// <summary>工具箱只读扫描 seam；生产调用 Kleaner.Analysis，测试注入可控任务验证取消。</summary>
    public Func<ToolboxJobRequest, CancellationToken, object>? ToolboxExecutor { get; init; }

    /// <summary>
    /// 扫描执行器 seam（工单 11）：null 时用真实 <see cref="ScanEngine"/>（09 的 IProgress 每规则上报）；
    /// 集成测试注入慢速/可控 fake 来验证取消与断连语义。
    /// </summary>
    public Func<RuleSet, CancellationToken, IProgress<ScanProgress>, ScanReport>? ScanExecutor { get; init; }

    /// <summary>规则集来源 seam（工单 12）：null 时经 RuleUpdateService.LoadEffective 加载内置/用户覆盖规则。</summary>
    public Func<RuleSet>? RuleSetProvider { get; init; }

    /// <summary>隔离区根目录覆盖（工单 12）：null 时读 settings.json 的 QuarantineRoot，再回退 QuarantineManager.DefaultRoot。</summary>
    public string? QuarantineRoot { get; init; }

    /// <summary>提权探测 seam（工单 12）：null 时用 WindowsPrincipal 真实判定。测试注入固定值。</summary>
    public Func<bool>? ElevationProbe { get; init; }

    /// <summary>提权交接 seam；生产以 runas 重启同端口同 token 的宿主，测试只记录调用。</summary>
    public Func<int, string, bool>? ElevationRestart { get; init; }

    /// <summary>高级模式只读扫描 seam；测试注入固定 WSL/注册表结果，生产调用 SpecialOps。</summary>
    public Func<IReadOnlyList<Kleaner.SpecialOps.VhdxInfo>>? WslDetector { get; init; }

    /// <summary>高级模式注册表残留扫描 seam；不提供任何写入能力。</summary>
    public Func<IReadOnlyList<Kleaner.SpecialOps.BrokenInstallEntry>>? RegistryScanner { get; init; }

    /// <summary>
    /// 清理执行器 seam（工单 12）：null 时走真实 QuarantineManager.Execute（移入隔离区 + 落 clean 历史）。
    /// 集成测试注入 fake，绝不触碰真实文件系统。
    /// </summary>
    public Func<IReadOnlyList<PlanResolvedItem>, ExecutionReport>? CleanExecutor { get; init; }

    /// <summary>历史管理器来源 seam（工单 13）：null 时用默认 %APPDATA%\Kleaner\history.jsonl。测试指向临时文件。</summary>
    public Func<HistoryManager>? HistoryProvider { get; init; }

    /// <summary>隔离区管理器来源 seam（工单 13）：null 时按 HostRuntime 三段解析（此处 seam → settings.json → 默认根）+ 共享历史。测试指向临时根。</summary>
    public Func<QuarantineManager>? QuarantineProvider { get; init; }

    /// <summary>启动项管理器来源 seam（工单 13）：null 时用真实注册表/启动文件夹环境 + 共享历史。测试注入 IStartupEnvironment fake。</summary>
    public Func<StartupManager>? StartupProvider { get; init; }

    public static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
