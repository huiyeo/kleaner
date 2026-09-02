using System.Net;
using System.Text;
using System.Text.Json;
using Kleaner.WebHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace Kleaner.WebHost.Tests;

/// <summary>工单 14：settings.json 同源、规则更新薄端点、三项只读工具箱 job 与取消语义。</summary>
public sealed class SettingsAndToolsApiTests : IDisposable
{
    private const int Port = 45172;
    private const string Token = "settings-tools-test-token";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kleaner-webhost-14-" + Guid.NewGuid().ToString("N"));
    private readonly List<WebApplication> _apps = new();

    public SettingsAndToolsApiTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        foreach (var app in _apps)
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Directory.Delete(_root, recursive: true);
    }

    private (HttpClient Client, KleanerWebHostOptions Options) BuildHost(
        Func<string, string, Task<string?>>? update = null,
        Func<ToolboxJobRequest, CancellationToken, object>? tools = null)
    {
        var options = new KleanerWebHostOptions
        {
            Port = Port,
            Token = Token,
            UseTestServer = true,
            EnableIdleExit = false,
            SettingsFilePath = Path.Combine(_root, "settings.json"),
            RuleUpdateExecutor = update,
            ToolboxExecutor = tools,
        };
        var app = WebHostAppFactory.Build(options);
        app.Start();
        _apps.Add(app);
        var client = app.GetTestClient();
        client.BaseAddress = new Uri($"http://127.0.0.1:{Port}");
        client.DefaultRequestHeaders.Add("X-Kleaner-Token", Token);
        client.DefaultRequestHeaders.Add("Origin", $"http://127.0.0.1:{Port}");
        return (client, options);
    }

    private static StringContent Json(object value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static async Task<string> StartToolAsync(HttpClient client, string path, object payload)
    {
        var response = await client.PostAsync(path, Json(payload));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("jobId").GetString()!;
    }

    private static async Task<JsonElement> WaitForStatusAsync(HttpClient client, string jobId, string status)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            using var doc = JsonDocument.Parse(await client.GetStringAsync($"/api/jobs/{jobId}"));
            if (doc.RootElement.GetProperty("status").GetString() == status)
                return doc.RootElement.Clone();
            await Task.Delay(25);
        }
        throw new TimeoutException($"job {jobId} 未在限时内到达 {status}");
    }

    [Fact]
    public async Task Settings_GetPut_UsesGuiCompatibleThreeFieldFile()
    {
        var (client, options) = BuildHost();
        var value = new
        {
            quarantineRoot = @"Q:\KleanerQuarantine",
            ruleUpdateUrl = "https://example.test/rules.v1.json",
            ruleUpdateSha512 = "ABCD",
        };

        var response = await client.PutAsync("/api/settings", Json(value));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var stored = JsonDocument.Parse(await File.ReadAllTextAsync(options.SettingsFilePath!));
        Assert.Equal(3, stored.RootElement.EnumerateObject().Count());
        Assert.Equal(value.quarantineRoot, stored.RootElement.GetProperty("QuarantineRoot").GetString());
        Assert.Equal(value.ruleUpdateUrl, stored.RootElement.GetProperty("RuleUpdateUrl").GetString());
        Assert.Equal(value.ruleUpdateSha512, stored.RootElement.GetProperty("RuleUpdateSha512").GetString());
        Assert.Equal(value.quarantineRoot, HostRuntime.ResolveQuarantineRoot(options));

        using var fetched = JsonDocument.Parse(await client.GetStringAsync("/api/settings"));
        Assert.Equal(value.ruleUpdateUrl, fetched.RootElement.GetProperty("ruleUpdateUrl").GetString());
    }

    [Fact]
    public async Task RuleUpdate_UsesSavedSettingsAndKeepsCoreValidationResult()
    {
        string? url = null;
        string? sha = null;
        var (client, _) = BuildHost((receivedUrl, receivedSha) =>
        {
            url = receivedUrl;
            sha = receivedSha;
            return Task.FromResult<string?>(null);
        });
        await client.PutAsync("/api/settings", Json(new
        {
            quarantineRoot = (string?)null,
            ruleUpdateUrl = "https://example.test/rules.v1.json",
            ruleUpdateSha512 = "0123",
        }));

        var response = await client.PostAsync("/api/rules/update", Json(new { }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("https://example.test/rules.v1.json", url);
        Assert.Equal("0123", sha);
    }

    [Fact]
    public async Task RuleUpdate_MissingSettings_Returns400WithoutCallingExecutor()
    {
        var called = false;
        var (client, _) = BuildHost((_, _) =>
        {
            called = true;
            return Task.FromResult<string?>(null);
        });

        var response = await client.PostAsync("/api/rules/update", Json(new { }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(called);
    }

    [Fact]
    public async Task Tools_AllActionsRunAsJobsAndExposeTypedResults()
    {
        var (client, _) = BuildHost(tools: (request, _) => request switch
        {
            LargeFilesRequest => new[] { new { kind = "large" } },
            DuplicatesRequest => new[] { new { kind = "duplicates" } },
            UsageRequest => new[] { new { kind = "usage" } },
            _ => throw new InvalidOperationException(),
        });

        var cases = new[]
        {
            ("/api/tools/large-files", (object)new { root = _root, minBytes = 1L, top = 10 }, "toolbox.large-files"),
            ("/api/tools/duplicates", (object)new { root = _root, minBytesPerFile = 1L }, "toolbox.duplicates"),
            ("/api/tools/usage", (object)new { root = _root }, "toolbox.usage"),
        };

        foreach (var (path, payload, kind) in cases)
        {
            var jobId = await StartToolAsync(client, path, payload);
            var snapshot = await WaitForStatusAsync(client, jobId, "Completed");
            Assert.Equal(kind, snapshot.GetProperty("kind").GetString());
            Assert.Equal(JsonValueKind.Array, snapshot.GetProperty("result").ValueKind);
        }
    }

    [Fact]
    public async Task Tools_Cancel_Returns202AndLeavesNoSideEffect()
    {
        using var started = new ManualResetEventSlim();
        var sideEffect = false;
        var (client, _) = BuildHost(tools: (_, token) =>
        {
            started.Set();
            token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
            token.ThrowIfCancellationRequested();
            sideEffect = true;
            return Array.Empty<object>();
        });
        var jobId = await StartToolAsync(client, "/api/tools/usage", new { root = _root });
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));

        var cancel = await client.PostAsync($"/api/jobs/{jobId}/cancel", Json(new { }));

        Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        var snapshot = await WaitForStatusAsync(client, jobId, "Cancelled");
        Assert.Equal(JsonValueKind.Null, snapshot.GetProperty("result").ValueKind);
        Assert.False(sideEffect);
    }

    [Fact]
    public async Task Tools_SystemGuide_IsReadOnlyAndUsesTheSpecialOpsSource()
    {
        var (client, _) = BuildHost();

        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/tools/system-guide"));

        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Contains(doc.RootElement.EnumerateArray(), item =>
            item.GetProperty("command").GetString() == "powercfg /h off");
    }
}
