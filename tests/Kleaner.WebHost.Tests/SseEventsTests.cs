using System.Net;
using System.Text;
using System.Text.Json;
using Kleaner.Core;
using Kleaner.WebHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kleaner.WebHost.Tests;

/// <summary>
/// GET /api/events 单一多路复用 SSE 流的集成测试（工单 11 验收）：
/// token 校验（无 token 4xx）、扫描 job 逐规则事件（09 的 IProgress）、断连不取消、按 jobId 多路复用。
/// </summary>
public sealed class SseEventsTests : IDisposable
{
    private const int Port = 45172;
    private const string Token = "sse-events-test-token";
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(20);

    private readonly WebApplication _app;
    private readonly HttpClient _client;

    public SseEventsTests()
    {
        _app = WebHostAppFactory.Build(new KleanerWebHostOptions
        {
            Port = Port,
            Token = Token,
            UseTestServer = true,
            EnableIdleExit = false,
            // 快速 fake：三条规则逐条上报（09 的每规则完成语义），测试断言事件序列
            ScanExecutor = (set, token, progress) =>
            {
                progress.Report(new ScanProgress("rule-a", 2, 128));
                progress.Report(new ScanProgress("rule-b", 1, 64));
                progress.Report(new ScanProgress("rule-c", 0, 0));
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

    private static RuleSet EmptyRuleSet() => new(1, null, null, Array.Empty<Rule>());

    /// <summary>打开 SSE 流（fetch 流式语义：ResponseHeadersRead），返回响应与逐行读取器。</summary>
    private async Task<(HttpResponseMessage Response, StreamReader Reader)> OpenStreamAsync(
        CancellationTokenSource requestLifetime)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/events");
        request.Headers.Add("X-Kleaner-Token", Token);

        var response = await _client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, requestLifetime.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var stream = await response.Content.ReadAsStreamAsync(requestLifetime.Token);
        return (response, new StreamReader(stream));
    }

    private static async Task<List<(string Event, string Data)>> ReadEventsAsync(
        StreamReader reader, int count)
    {
        var events = new List<(string, string)>();
        using var cts = new CancellationTokenSource(ReadTimeout);
        string? eventName = null;
        var data = new StringBuilder();

        while (events.Count < count)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            if (line is null)
            {
                break; // 流被服务端结束
            }

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                eventName = line["event: ".Length..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                data.Append(line["data: ".Length..]);
            }
            else if (line.Length == 0 && eventName is not null)
            {
                events.Add((eventName, data.ToString()));
                eventName = null;
                data.Clear();
            }
        }

        Assert.Equal(count, events.Count);
        return events;
    }

    /// <summary>幂等地补齐 token 默认头（重复 Add 会让头出现两个值、破坏常量时间比对）。</summary>
    private static HttpClient WithTokenHeader(HttpClient client)
    {
        if (!client.DefaultRequestHeaders.Contains("X-Kleaner-Token"))
        {
            client.DefaultRequestHeaders.Add("X-Kleaner-Token", Token);
        }

        return client;
    }

    private Task<HttpResponseMessage> GetJobAsync(string jobId) =>
        WithTokenHeader(_client).GetAsync($"/api/jobs/{jobId}");

    private async Task<JsonElement> WaitForRestStatusAsync(string jobId, string status, int timeoutSeconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var response = await GetJobAsync(jobId);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (doc.RootElement.GetProperty("status").GetString() == status)
            {
                return doc.RootElement.Clone();
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"job {jobId} 未在限时内到达 {status}");
    }

    [Fact]
    public async Task Events_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/events");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Events_ReceiveConnectedJobStartedPerRuleProgressAndCompleted()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var (response, reader) = await OpenStreamAsync(lifetime);

        // 连接确认事件先到，之后启动 job 才不丢事件
        var connected = await ReadEventsAsync(reader, 1);
        Assert.Equal("connected", connected[0].Event);
        Assert.Contains("serverTime", connected[0].Data);

        var job = _app.Services.GetRequiredService<IScanJobService>().Start(EmptyRuleSet());

        var events = await ReadEventsAsync(reader, 5); // started + 3×progress + completed
        Assert.Equal("job.started", events[0].Event);
        Assert.Contains(job.JobId, events[0].Data);

        Assert.Equal("scan.progress", events[1].Event);
        Assert.Equal("scan.progress", events[2].Event);
        Assert.Equal("scan.progress", events[3].Event);
        Assert.Contains("\"ruleId\":\"rule-a\"", events[1].Data);
        Assert.Contains("\"ruleId\":\"rule-b\"", events[2].Data);
        Assert.Contains("\"ruleId\":\"rule-c\"", events[3].Data);
        events.Take(4).ToList().ForEach(evt => Assert.Contains(job.JobId, evt.Data));

        Assert.Equal("job.completed", events[4].Event);
        Assert.Contains(job.JobId, events[4].Data);
        Assert.Contains("\"kind\":\"scan\"", events[4].Data);

        // SSE 到达的事件与 REST 快照一致（重连 = 快照 + 增量，工单 07）
        var snapshot = await WaitForRestStatusAsync(job.JobId, "Completed");
        Assert.Equal(JsonValueKind.Object, snapshot.GetProperty("result").ValueKind);

        response.Dispose();
    }

    [Fact]
    public async Task Events_Disconnect_JobKeepsRunningAndResultRetrievableViaRest()
    {
        // 专用实例：慢速执行器无视取消令牌——断连不得影响任务（工单 07）
        using var app = WebHostAppFactory.Build(new KleanerWebHostOptions
        {
            Port = Port,
            Token = Token,
            UseTestServer = true,
            EnableIdleExit = false,
            ScanExecutor = (set, token, progress) =>
            {
                progress.Report(new ScanProgress("rule-a", 1, 1));
                Thread.Sleep(500); // 模拟扫盘耗时；token 被忽略
                return new ScanReport(DateTime.UtcNow, Array.Empty<RuleScanResult>(), Array.Empty<string>());
            },
        });
        app.Start();
        var client = app.GetTestClient();
        client.BaseAddress = new Uri($"http://127.0.0.1:{Port}");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/events");
        request.Headers.Add("X-Kleaner-Token", Token);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, lifetime.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(lifetime.Token));
        await ReadEventsAsync(reader, 1); // connected

        var job = app.Services.GetRequiredService<IScanJobService>().Start(EmptyRuleSet());

        // 断连（关标签）：只结束本连接，不取消任务（工单 07）
        lifetime.Cancel();
        response.Dispose();

        var deadline = DateTime.UtcNow.AddSeconds(15);
        JsonElement? snapshot = null;
        while (DateTime.UtcNow < deadline)
        {
            var rest = await WithTokenHeader(client).GetAsync($"/api/jobs/{job.JobId}");
            var doc = JsonDocument.Parse(await rest.Content.ReadAsStringAsync());
            if (doc.RootElement.GetProperty("status").GetString() == "Completed")
            {
                snapshot = doc.RootElement.Clone();
                break;
            }

            await Task.Delay(50);
        }

        Assert.True(snapshot is not null, "断连后 job 应继续跑完并从 REST 快照取回");
        Assert.Equal("Completed", snapshot!.Value.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Object, snapshot!.Value.GetProperty("result").ValueKind);

        client.Dispose();
    }

    [Fact]
    public async Task Events_MultiplexesConcurrentJobs_ByJobId()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var (response, reader) = await OpenStreamAsync(lifetime);
        await ReadEventsAsync(reader, 1); // connected

        var service = _app.Services.GetRequiredService<IScanJobService>();
        var job1 = service.Start(EmptyRuleSet());
        var job2 = service.Start(EmptyRuleSet());

        // connected 已读；此处 2×started + 6×progress + 2×completed
        var events = await ReadEventsAsync(reader, 10);

        var startedJobIds = events.Where(evt => evt.Event == "job.started")
            .Select(evt => evt.Data).ToList();
        Assert.Contains(startedJobIds, data => data.Contains(job1.JobId));
        Assert.Contains(startedJobIds, data => data.Contains(job2.JobId));

        // 多路复用：同一流内各 job 的增量以其 jobId 区分，互不串流
        var progressByJob = events.Where(evt => evt.Event == "scan.progress")
            .Select(evt => evt.Data)
            .GroupBy(data => data.Contains(job1.JobId) ? job1.JobId : job2.JobId)
            .ToDictionary(group => group.Key, group => group.Count());
        Assert.Equal(3, progressByJob[job1.JobId]);
        Assert.Equal(3, progressByJob[job2.JobId]);

        Assert.Equal(2, events.Count(evt => evt.Event == "job.completed"));

        response.Dispose();
    }

    [Fact]
    public async Task Cancel_SurfacedOnStream_WithCancelledEventAndTerminalSnapshot()
    {
        // 专用实例：挂起等取消令牌的执行器
        using var app = WebHostAppFactory.Build(new KleanerWebHostOptions
        {
            Port = Port,
            Token = Token,
            UseTestServer = true,
            EnableIdleExit = false,
            ScanExecutor = (set, token, progress) =>
            {
                progress.Report(new ScanProgress("rule-a", 1, 1));
                token.WaitHandle.WaitOne(TimeSpan.FromSeconds(15));
                token.ThrowIfCancellationRequested();
                return new ScanReport(DateTime.UtcNow, Array.Empty<RuleScanResult>(), Array.Empty<string>());
            },
        });
        app.Start();
        var client = app.GetTestClient();
        client.BaseAddress = new Uri($"http://127.0.0.1:{Port}");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/events");
        request.Headers.Add("X-Kleaner-Token", Token);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, lifetime.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(lifetime.Token));
        await ReadEventsAsync(reader, 1); // connected

        var job = app.Services.GetRequiredService<IScanJobService>().Start(EmptyRuleSet());

        var cancelRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/jobs/{job.JobId}/cancel")
        {
            Content = new StringContent(""),
        };
        cancelRequest.Headers.Add("Origin", $"http://127.0.0.1:{Port}");
        cancelRequest.Headers.Add("X-Kleaner-Token", Token);
        var cancelResponse = await client.SendAsync(cancelRequest);
        Assert.Equal(HttpStatusCode.Accepted, cancelResponse.StatusCode);

        // started + progress + cancelled
        var events = await ReadEventsAsync(reader, 3);
        Assert.Equal("job.cancelled", events[2].Event);
        Assert.Contains(job.JobId, events[2].Data);

        var deadline = DateTime.UtcNow.AddSeconds(15);
        JsonElement? snapshot = null;
        while (DateTime.UtcNow < deadline)
        {
            var rest = await WithTokenHeader(client).GetAsync($"/api/jobs/{job.JobId}");
            var doc = JsonDocument.Parse(await rest.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("status", out var statusElement)
                && statusElement.GetString() == "Cancelled")
            {
                snapshot = doc.RootElement.Clone();
                break;
            }

            await Task.Delay(50);
        }

        Assert.True(snapshot is not null, "取消后 job 应到达 Cancelled 终态并可从 REST 快照取回");
        Assert.Equal(JsonValueKind.Null, snapshot!.Value.GetProperty("result").ValueKind);

        client.Dispose();
        response.Dispose();
    }
}
