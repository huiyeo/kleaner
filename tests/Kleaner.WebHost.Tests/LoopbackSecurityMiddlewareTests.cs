using System.Net;
using Kleaner.WebHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace Kleaner.WebHost.Tests;

/// <summary>
/// 五层防护服务端三层的集成测试（工单 10 验收）：
/// 缺 token / 错 Host / 错 Origin 一律 4xx。Host → Origin → Token 顺序固定。
/// </summary>
public sealed class LoopbackSecurityMiddlewareTests : IDisposable
{
    private const int Port = 45172;
    private const string Token = "integration-test-token";

    private readonly WebApplication _app;
    private readonly HttpClient _client;

    public LoopbackSecurityMiddlewareTests()
    {
        _app = WebHostAppFactory.Build(new KleanerWebHostOptions
        {
            Port = Port,
            Token = Token,
            UseTestServer = true,
            EnableIdleExit = false,
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

    private HttpClient ClientWithToken()
    {
        _client.DefaultRequestHeaders.Add("X-Kleaner-Token", Token);
        return _client;
    }

    [Fact]
    public async Task Health_WithValidTokenAndHost_ReturnsOk()
    {
        var response = await ClientWithToken().GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ok", body);
    }

    [Fact]
    public async Task Health_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_WithWrongToken_Returns401()
    {
        _client.DefaultRequestHeaders.Add("X-Kleaner-Token", "not-the-token");

        var response = await _client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_WithWrongHost_Returns400()
    {
        // Host 头必须恰为 127.0.0.1:<port>，localhost 也拒（防 DNS rebinding）
        _client.BaseAddress = new Uri($"http://localhost:{Port}");
        _client.DefaultRequestHeaders.Add("X-Kleaner-Token", Token);
        _client.DefaultRequestHeaders.Host = $"localhost:{Port}";

        var response = await _client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithoutOrigin_Returns403()
    {
        _client.DefaultRequestHeaders.Add("X-Kleaner-Token", Token);

        var response = await _client.PostAsync("/api/ping", new StringContent(""));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithWrongOrigin_Returns403()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/ping")
        {
            Content = new StringContent(""),
        };
        request.Headers.Add("Origin", "http://evil.example");
        request.Headers.Add("X-Kleaner-Token", Token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithValidOriginAndToken_ReturnsOk()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/ping")
        {
            Content = new StringContent(""),
        };
        request.Headers.Add("Origin", $"http://127.0.0.1:{Port}");
        request.Headers.Add("X-Kleaner-Token", Token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Root_StaticShell_IsExemptFromToken_ReturnsOk()
    {
        // token 只护 /api/*：静态 PWA 壳经启动 URL 注入后无需请求头即可加载
        var response = await _client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
