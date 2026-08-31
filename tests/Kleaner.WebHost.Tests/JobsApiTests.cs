using System.Net;
using System.Text.Json;
using Kleaner.Core;
using Kleaner.WebHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kleaner.WebHost.Tests;

/// <summary>job 体系集成测试（工单 11 验收）：取消语义 202/404/409、取消后状态与结果可见、REST 快照可取回。</summary>
public sealed class JobsApiTests : IDisposable
{
    private const int Port = 45172;
    private const string Token = "jobs-api-test-token";

    private readonly WebApplication _app;
    private readonly HttpClient _client;

    public JobsApiTests()
    {
        _app = WebHostAppFactory.Build(new KleanerWebHostOptions
        {
            Port = Port,
            Token = Token,
            UseTestServer = true,
            EnableIdleExit = false,
            // 慢速 fake：先上报一条规则完成，再挂起等取消令牌——验证取消语义（工单 11）
            ScanExecutor = (set, token, progress) =>
            {
                progress.Report(new ScanProgress("rule-a", 2, 128));
                token.WaitHandle.WaitOne(TimeSpan.FromSeconds(15));
                token.ThrowIfCancellationRequested();
                return new ScanReport(DateTime.UtcNow, Array.Empty<RuleScanResult>(), Array.Empty<string>());
            },
        });
        _app.Start();

        _client = _app.GetTestClient();
        _client.BaseAddress = new Uri($"http://127.0.0.1:{Port}");
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>幂等地补齐 token + Origin 默认头（重复 Add 会让头出现两个值、破坏常量时间比对）。</summary>
    private HttpClient ClientWithApiHeaders()
    {
        if (!_client.DefaultRequestHeaders.Contains("X-Kleaner-Token"))
        {
            _client.DefaultRequestHeaders.Add("X-Kleaner-Token", Token);
        }

        if (!_client.DefaultRequestHeaders.Contains("Origin"))
        {
            _client.DefaultRequestHeaders.Add("Origin", $"http://127.0.0.1:{Port}");
        }

        return _client;
    }

    private static RuleSet EmptyRuleSet() => new(1, null, null, Array.Empty<Rule>());

    private async Task<JsonDocument> GetJobJsonAsync(string jobId)
    {
        var response = await ClientWithApiHeaders().GetAsync($"/api/jobs/{jobId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private async Task<JsonElement> WaitForStatusAsync(string jobId, string status, int timeoutSeconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            using var doc = await GetJobJsonAsync(jobId);
            if (doc.RootElement.GetProperty("status").GetString() == status)
            {
                return doc.RootElement.Clone();
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"job {jobId} 未在限时内到达 {status}");
    }

    [Fact]
    public async Task Cancel_UnknownJob_Returns404()
    {
        var response = await ClientWithApiHeaders().PostAsync($"/api/jobs/{Guid.NewGuid():N}/cancel", new StringContent(""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_FinishedJob_Returns409()
    {
        var registry = _app.Services.GetRequiredService<JobRegistry>();
        var job = registry.Create("scan");
        registry.Complete(job, new ScanReport(DateTime.UtcNow, Array.Empty<RuleScanResult>(), Array.Empty<string>()));

        var response = await ClientWithApiHeaders().PostAsync($"/api/jobs/{job.JobId}/cancel", new StringContent(""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_RunningScanJob_Returns202_JobBecomesCancelledWithNullResult()
    {
        var job = _app.Services.GetRequiredService<IScanJobService>().Start(EmptyRuleSet());

        var response = await ClientWithApiHeaders().PostAsync($"/api/jobs/{job.JobId}/cancel", new StringContent(""));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var snapshot = await WaitForStatusAsync(job.JobId, "Cancelled");
        Assert.Equal("scan", snapshot.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, snapshot.GetProperty("result").ValueKind);
        Assert.NotNull(snapshot.GetProperty("endedUtc").GetString());
    }

    [Fact]
    public async Task Snapshot_JobVisibleInList_AndResultRetrievableAfterCompletion()
    {
        var registry = _app.Services.GetRequiredService<JobRegistry>();
        var service = _app.Services.GetRequiredService<IScanJobService>();

        var running = service.Start(EmptyRuleSet());

        // 快照在 running 期可见（重连 = REST 快照 + SSE 增量，工单 07）
        var listResponse = await ClientWithApiHeaders().GetAsync("/api/jobs");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using (var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync()))
        {
            var listed = list.RootElement.EnumerateArray()
                .Any(jobElement => jobElement.GetProperty("jobId").GetString() == running.JobId);
            Assert.True(listed, "running job 应出现在 /api/jobs 列表");
        }

        using (var runningDoc = await GetJobJsonAsync(running.JobId))
        {
            Assert.Equal("Running", runningDoc.RootElement.GetProperty("status").GetString());
        }

        // 终态后常驻可取回：结果（ScanReport）经 REST 快照可见
        var report = new ScanReport(DateTime.UtcNow, Array.Empty<RuleScanResult>(), Array.Empty<string>());
        registry.Complete(running, report);
        Assert.Equal("scan", running.Kind);

        var finished = await WaitForStatusAsync(running.JobId, "Completed");
        Assert.Equal(JsonValueKind.Object, finished.GetProperty("result").ValueKind);
        Assert.NotNull(finished.GetProperty("startedUtc").GetString());
    }
}
