using System.Net;
using System.Text.Json;
using Kleaner.WebHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace Kleaner.WebHost.Tests;

/// <summary>工单 15：静态 PWA 壳由生产管线直接托管，manifest、SW 与前端路由回退不可脱节。</summary>
public sealed class PwaShellTests : IDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    public PwaShellTests()
    {
        _app = WebHostAppFactory.Build(new KleanerWebHostOptions
        {
            Port = 45172,
            Token = "pwa-shell-test-token",
            UseTestServer = true,
            EnableIdleExit = false,
            TestStaticWebRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
        });
        _app.Start();
        _client = _app.GetTestClient();
        _client.BaseAddress = new Uri("http://127.0.0.1:45172");
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task ShellAndClientRoute_AreServedWithoutApiToken()
    {
        var home = await _client.GetAsync("/");
        var route = await _client.GetAsync("/quarantine");

        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Equal(HttpStatusCode.OK, route.StatusCode);
        Assert.Equal("text/html", home.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<kleaner-shell>", await home.Content.ReadAsStringAsync());
        Assert.Contains("<kleaner-shell>", await route.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ManifestAndServiceWorker_DeclareInstallAndOfflineShellStrategy()
    {
        using var manifest = JsonDocument.Parse(await _client.GetStringAsync("/manifest.webmanifest"));
        var root = manifest.RootElement;
        Assert.Equal("standalone", root.GetProperty("display").GetString());
        Assert.Equal("/", root.GetProperty("start_url").GetString());
        Assert.Equal("image/svg+xml", root.GetProperty("icons")[0].GetProperty("type").GetString());

        var serviceWorker = await _client.GetStringAsync("/sw.js");
        Assert.Contains("kleaner-shell-v1", serviceWorker);
        Assert.Contains("/index.html", serviceWorker);
        Assert.Contains("url.pathname.startsWith(\"/api/\")", serviceWorker);
        Assert.Contains("SKIP_WAITING", serviceWorker);
    }

    [Fact]
    public async Task EventClient_KeepsLaunchTokenInSessionAndReconnectsWithBackoff()
    {
        var client = await _client.GetStringAsync("/app.js");

        Assert.Contains("sessionStorage.setItem(TOKEN_KEY, token)", client);
        Assert.Contains("history.replaceState({}, \"\", url)", client);
        Assert.Contains("\"X-Kleaner-Token\": token", client);
        Assert.Contains("fetch(\"/api/events\", { headers: apiHeaders(), cache: \"no-store\" })", client);
        Assert.Contains("Math.min(1000 * 2 ** attempt, 15000)", client);
    }
}
