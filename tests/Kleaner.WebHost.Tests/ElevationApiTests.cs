using System.Net;
using Kleaner.WebHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace Kleaner.WebHost.Tests;

/// <summary>工单 16：提权交接只接受已通过本地安全中间件的请求，且沿用当前端口与 token。</summary>
public sealed class ElevationApiTests : IDisposable
{
    private const int Port = 45172;
    private const string Token = "elevation-test-token";
    private readonly List<WebApplication> _apps = new();

    public void Dispose()
    {
        foreach (var app in _apps)
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Elevate_StartsHandoffWithCurrentPortAndToken()
    {
        (int Port, string Token)? received = null;
        using var client = BuildHost(false, (port, token) =>
        {
            received = (port, token);
            return true;
        });

        var response = await client.PostAsync("/api/elevate", new StringContent(""));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal((Port, Token), received);
    }

    [Fact]
    public async Task Elevate_AlreadyElevated_RejectsWithoutRestart()
    {
        var started = false;
        using var client = BuildHost(true, (_, _) =>
        {
            started = true;
            return true;
        });

        var response = await client.PostAsync("/api/elevate", new StringContent(""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.False(started);
    }

    [Theory]
    [InlineData(new[] { "--handoff-port", "45172", "--handoff-token", "abc" }, true)]
    [InlineData(new[] { "--handoff-port", "0", "--handoff-token", "abc" }, false)]
    public void HandoffArguments_AreValidated(string[] args, bool expected)
    {
        Assert.Equal(expected, ElevationRestart.TryParseHandoff(args, out _, out _));
    }

    [Theory]
    [InlineData(new[] { "Kleaner.WebHost.exe" }, "--handoff-port 45172 --handoff-token abc")]
    [InlineData(new[] { "dotnet.exe", "Kleaner.WebHost.dll" }, "\"Kleaner.WebHost.dll\" --handoff-port 45172 --handoff-token abc")]
    public void HandoffArguments_RetainTheDllWhenHostedByDotnet(string[] commandLine, string expected)
    {
        Assert.Equal(expected, ElevationRestart.BuildArguments(commandLine, 45172, "abc"));
    }

    private HttpClient BuildHost(bool elevated, Func<int, string, bool> restart)
    {
        var app = WebHostAppFactory.Build(new KleanerWebHostOptions
        {
            Port = Port,
            Token = Token,
            UseTestServer = true,
            EnableIdleExit = false,
            ElevationProbe = () => elevated,
            ElevationRestart = restart,
        });
        app.Start();
        _apps.Add(app);
        var client = app.GetTestClient();
        client.BaseAddress = new Uri($"http://127.0.0.1:{Port}");
        client.DefaultRequestHeaders.Add("X-Kleaner-Token", Token);
        client.DefaultRequestHeaders.Add("Origin", $"http://127.0.0.1:{Port}");
        return client;
    }
}
