using System.Net;
using System.Text;
using System.Text.Json;
using Kleaner.Core;
using Kleaner.Executor;
using Kleaner.WebHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace Kleaner.WebHost.Tests;

/// <summary>
/// 清理主链路集成测试（工单 12 验收）：scan job → plan（dry-run）→ confirm（执行）。
/// ScanExecutor / CleanExecutor / ElevationProbe 全部注入 fake，不触碰真实文件系统；
/// 真实执行路径（QuarantineManager.Execute + HistoryManager）由 Executor 层既有测试覆盖。
/// </summary>
public sealed class CleanPlanApiTests : IDisposable
{
    private const int Port = 45172;
    private const string Token = "clean-plan-test-token";

    private readonly List<WebApplication> _apps = new();

    public void Dispose()
    {
        foreach (var app in _apps)
        {
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static RuleSet TestRules() => new(1, null, 30, new Rule[]
    {
        new("rule-a", "规则A", RuleCategory.Temp, RiskLevel.Low, new[] { "C:/tmp/*" },
            Array.Empty<string>(), 30, null, false, true,
            "临时目录示例规则，安全说明满足二十字以上的要求。", Verified: "本机实测于开发机"),
        new("rule-elev", "系统级规则", RuleCategory.System, RiskLevel.Medium, new[] { "C:/sys/*" },
            Array.Empty<string>(), 30, null, true, true,
            "系统级示例规则，安全说明满足二十字以上的要求。", Verified: "文档推断"),
    });

    private static ScanReport SampleReport()
    {
        var utc = DateTime.UtcNow;
        return new ScanReport(
            utc,
            new RuleScanResult[]
            {
                new("rule-a", "规则A", RuleCategory.Temp, RiskLevel.Low, false, 2, 150,
                    "临时目录示例规则，安全说明满足二十字以上的要求。",
                    new FileCandidate[] { new(@"C:\tmp\a.log", 100, utc), new(@"C:\tmp\b.log", 50, utc) }),
                new("rule-elev", "系统级规则", RuleCategory.System, RiskLevel.Medium, true, 1, 50,
                    "系统级示例规则，安全说明满足二十字以上的要求。",
                    new FileCandidate[] { new(@"C:\sys\old\file.bin", 50, utc) }),
            },
            Array.Empty<string>());
    }

    private (HttpClient Client, List<PlanResolvedItem> Executed) BuildHost(
        Func<bool>? elevationProbe = null,
        Func<CancellationToken, ScanReport>? scan = null)
    {
        var executed = new List<PlanResolvedItem>();
        var app = WebHostAppFactory.Build(new KleanerWebHostOptions
        {
            Port = Port,
            Token = Token,
            UseTestServer = true,
            EnableIdleExit = false,
            ElevationProbe = elevationProbe ?? (() => false),
            RuleSetProvider = TestRules,
            ScanExecutor = (set, token, progress) => (scan ?? (_ => SampleReport()))(token),
            CleanExecutor = items =>
            {
                lock (executed)
                {
                    executed.AddRange(items);
                }

                return new ExecutionReport(
                    "20260831-120000", @"Q:\KleanerQuarantine\20260831-120000",
                    items.Sum(i => i.Files.Count), items.Sum(i => i.Files.Sum(f => f.SizeBytes)),
                    Array.Empty<string>());
            },
        });
        app.Start();
        _apps.Add(app);

        var client = app.GetTestClient();
        client.BaseAddress = new Uri($"http://127.0.0.1:{Port}");
        client.DefaultRequestHeaders.Add("X-Kleaner-Token", Token);
        client.DefaultRequestHeaders.Add("Origin", $"http://127.0.0.1:{Port}");
        return (client, executed);
    }

    private static async Task<string> StartScanAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/scan", new StringContent("", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("jobId").GetString()!;
    }

    private static async Task WaitScanCompletedAsync(HttpClient client, string jobId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            using var doc = JsonDocument.Parse(await client.GetStringAsync($"/api/jobs/{jobId}"));
            if (doc.RootElement.GetProperty("status").GetString() == "Completed")
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("扫描 job 未在限时内完成");
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> PostPlanAsync(
        HttpClient client, string jobId, string[] ruleIds)
    {
        var body = JsonSerializer.Serialize(new { jobId, ruleIds });
        var response = await client.PostAsync("/api/plans",
            new StringContent(body, Encoding.UTF8, "application/json"));
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response.StatusCode, doc.RootElement.Clone());
    }

    private static async Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string planId, string? confirmToken)
    {
        var body = JsonSerializer.Serialize(new { confirmToken });
        return await client.PostAsync($"/api/plans/{planId}/confirm",
            new StringContent(body, Encoding.UTF8, "application/json"));
    }

    /// <summary>跑到「拿到 planId + confirmToken」为止（rule-a，无需提权）。</summary>
    private async Task<(HttpClient Client, List<PlanResolvedItem> Executed, string PlanId, string ConfirmToken)>
        BuildPlanAsync(Func<bool>? elevationProbe = null)
    {
        var (client, executed) = BuildHost(elevationProbe);
        var jobId = await StartScanAsync(client);
        await WaitScanCompletedAsync(client, jobId);
        var (status, plan) = await PostPlanAsync(client, jobId, new[] { "rule-a" });
        Assert.Equal(HttpStatusCode.Created, status);
        return (client, executed, plan.GetProperty("planId").GetString()!, plan.GetProperty("confirmToken").GetString()!);
    }

    [Fact]
    public async Task FullChain_PreviewThenConfirm_IsOneBatch_WhitelistOnly()
    {
        var (client, executed, planId, confirmToken) = await BuildPlanAsync();

        // 预览视图：token 不回显、confirmed=false、汇总正确
        using (var preview = JsonDocument.Parse(await client.GetStringAsync($"/api/plans/{planId}")))
        {
            Assert.Null(preview.RootElement.GetProperty("confirmToken").GetString());
            Assert.False(preview.RootElement.GetProperty("confirmed").GetBoolean());
            Assert.False(preview.RootElement.GetProperty("needsElevation").GetBoolean());
            Assert.Equal(2, preview.RootElement.GetProperty("totalFiles").GetInt32());
            Assert.Equal(150, preview.RootElement.GetProperty("totalBytes").GetInt64());
        }

        var response = await ConfirmAsync(client, planId, confirmToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var report = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("20260831-120000", report.RootElement.GetProperty("batchId").GetString());
        Assert.Equal(2, report.RootElement.GetProperty("movedCount").GetInt32());
        Assert.Equal(150, report.RootElement.GetProperty("movedBytes").GetInt64());
        Assert.Empty(report.RootElement.GetProperty("skipped").EnumerateArray());

        // 白名单外路径零触碰：执行体拿到的只能是扫描报告内的文件
        var item = Assert.Single(executed);
        Assert.Equal("rule-a", item.RuleId);
        Assert.Equal(
            new[] { @"C:\tmp\a.log", @"C:\tmp\b.log" },
            item.Files.Select(f => f.FullPath).OrderBy(p => p).ToArray());

        // 一次 confirm = 一个批次：token 已烧毁，重放被拒
        var replay = await ConfirmAsync(client, planId, confirmToken);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
    }

    [Fact]
    public async Task Confirm_WrongToken_Returns403_TokenStillUsableAfterwards()
    {
        var (client, _, planId, confirmToken) = await BuildPlanAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await ConfirmAsync(client, planId, "wrong-token")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ConfirmAsync(client, planId, null)).StatusCode);

        // 凭据未被错误请求烧毁，原持有人仍可确认
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(client, planId, confirmToken)).StatusCode);
    }

    [Fact]
    public async Task Confirm_UnknownPlan_Returns404()
    {
        var (client, _, _, _) = await BuildPlanAsync();

        var response = await ConfirmAsync(client, "nonexistent", "any-token");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Plan_UnknownJob_Returns404()
    {
        var (client, _) = BuildHost();

        var (status, body) = await PostPlanAsync(client, "nonexistent-job", new[] { "rule-a" });

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.NotNull(body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Plan_FromRunningScanJob_Returns409()
    {
        // 扫描挂起不完成：plan 必须来自已完成的扫描，防止拿旧 plan 绕过新扫描（工单 04）
        var (client, _) = BuildHost(scan: token =>
        {
            token.WaitHandle.WaitOne(TimeSpan.FromSeconds(15));
            token.ThrowIfCancellationRequested();
            return SampleReport();
        });
        var jobId = await StartScanAsync(client);

        var (status, body) = await PostPlanAsync(client, jobId, new[] { "rule-a" });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.NotNull(body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Plan_EmptySelection_Returns400()
    {
        var (client, _) = BuildHost();
        var jobId = await StartScanAsync(client);
        await WaitScanCompletedAsync(client, jobId);

        var (status, _) = await PostPlanAsync(client, jobId, Array.Empty<string>());

        // 不复刻 CLI「--rule 缺省静默成功」的坑（deletion-path.md）
        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task Plan_UnknownRuleId_Returns400()
    {
        var (client, _) = BuildHost();
        var jobId = await StartScanAsync(client);
        await WaitScanCompletedAsync(client, jobId);

        var (status, _) = await PostPlanAsync(client, jobId, new[] { "rule-a", "rule-unknown" });

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task Confirm_PlanNeedsElevation_Returns409_NothingExecuted()
    {
        var (client, executed) = BuildHost(elevationProbe: () => false);
        var jobId = await StartScanAsync(client);
        await WaitScanCompletedAsync(client, jobId);
        var (_, plan) = await PostPlanAsync(client, jobId, new[] { "rule-elev" });
        Assert.True(plan.GetProperty("needsElevation").GetBoolean());

        var response = await ConfirmAsync(client, plan.GetProperty("planId").GetString()!, plan.GetProperty("confirmToken").GetString()!);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(executed);
    }

    [Fact]
    public async Task ScanEnvelope_ExposesMachineVerified_AndStringEnums()
    {
        var (client, _) = BuildHost();
        var jobId = await StartScanAsync(client);
        await WaitScanCompletedAsync(client, jobId);

        using var doc = JsonDocument.Parse(await client.GetStringAsync($"/api/jobs/{jobId}"));
        var rules = doc.RootElement.GetProperty("result").GetProperty("rules");

        Assert.Equal(2, rules.GetArrayLength());
        var ruleA = rules.EnumerateArray().First(r => r.GetProperty("ruleId").GetString() == "rule-a");
        var ruleElev = rules.EnumerateArray().First(r => r.GetProperty("ruleId").GetString() == "rule-elev");

        Assert.True(ruleA.GetProperty("machineVerified").GetBoolean());
        Assert.False(ruleElev.GetProperty("machineVerified").GetBoolean());
        Assert.Equal("temp", ruleA.GetProperty("category").GetString());
        Assert.Equal("low", ruleA.GetProperty("risk").GetString());
        Assert.Equal("medium", ruleElev.GetProperty("risk").GetString());
    }

    [Fact]
    public void TrimSafeJsonContext_DeserializesPlanRequest()
    {
        var request = JsonSerializer.Deserialize(
            """{"jobId":"scan-1","ruleIds":["rule-a"]}""",
            KleanerJsonContext.Default.PlanRequest);

        Assert.NotNull(request);
        Assert.Equal("scan-1", request.JobId);
        Assert.Equal(new[] { "rule-a" }, request.RuleIds);
    }
}
