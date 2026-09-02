using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Kleaner.WebHost;

/// <summary>异步任务创建响应；裁剪发布中不能使用匿名 JSON 对象。</summary>
public sealed record JobAcceptedView(string JobId);

/// <summary>confirm 端点对凭据的判定结果（工单 03 第 5 层：无预览凭据一律拒绝）。</summary>
public enum ConfirmOutcome
{
    /// <summary>token 不符 → 403。token 不被烧毁，凭据持有人仍可重试。</summary>
    BadToken,

    /// <summary>已确认过（confirmToken 一次性）→ 409。</summary>
    AlreadyConfirmed,

    /// <summary>凭据有效且已烧毁，允许执行。</summary>
    Consumed,
}

/// <summary>计划资源的对外形状。confirmToken 只在创建响应中出现一次；GET 一律返回 null。</summary>
public sealed record PlanView(
    string PlanId,
    string? ConfirmToken,
    bool NeedsElevation,
    bool Confirmed,
    int TotalFiles,
    long TotalBytes,
    IReadOnlyList<PlanItemView> Items);

/// <summary>
/// 单个清理计划：planId 与 confirmToken 是资源属性而非请求参数（工单 04），
/// 天然防止「拿旧 plan 绕过新扫描」。确认即烧毁 token，先到先得。
/// </summary>
public sealed class PlanRecord
{
    private readonly object _gate = new();
    private string? _confirmToken;

    public string PlanId { get; } = Guid.NewGuid().ToString("N");

    public CleanPlan Plan { get; }

    public DateTimeOffset CreatedUtc { get; } = DateTimeOffset.UtcNow;

    internal PlanRecord(CleanPlan plan)
    {
        Plan = plan;
        _confirmToken = KleanerWebHostOptions.GenerateToken();
    }

    public bool Confirmed
    {
        get { lock (_gate) return _confirmToken is null; }
    }

    /// <summary>
    /// 校验并烧毁一次性 confirmToken。校验与烧毁在锁内原子完成——并发双确认只有一个能拿到 Consumed，
    /// 执行体据此保证「一次 confirm = 一个批次」。烧毁先于执行：执行中途失败也不允许复用凭据重放删除。
    /// </summary>
    internal ConfirmOutcome TryConsume(string confirmToken)
    {
        lock (_gate)
        {
            if (_confirmToken is null)
            {
                return ConfirmOutcome.AlreadyConfirmed;
            }

            if (!FixedTimeEquals(_confirmToken, confirmToken))
            {
                return ConfirmOutcome.BadToken;
            }

            _confirmToken = null;
            return ConfirmOutcome.Consumed;
        }
    }

    public PlanView ToView(bool includeToken)
    {
        lock (_gate)
        {
            return new PlanView(
                PlanId,
                includeToken ? _confirmToken : null,
                Plan.NeedsElevation,
                _confirmToken is null,
                Plan.TotalFiles,
                Plan.TotalBytes,
                Plan.Items);
        }
    }

    private static bool FixedTimeEquals(string expected, string actual) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(actual));
}

/// <summary>
/// 计划注册表：与 JobRegistry 同策略，记录常驻到进程退出（重连取回，不设 TTL——
/// 计划只有拿到一次性 token 才能执行，泄漏风险由 token 闸兜底）。
/// </summary>
public sealed class PlanRegistry
{
    private readonly ConcurrentDictionary<string, PlanRecord> _plans = new(StringComparer.Ordinal);

    public PlanRecord Create(CleanPlan plan)
    {
        var record = new PlanRecord(plan);
        _plans[record.PlanId] = record;
        return record;
    }

    public PlanRecord? Get(string planId) =>
        _plans.TryGetValue(planId, out var record) ? record : null;
}
