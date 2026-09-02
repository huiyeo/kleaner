using System.Text.Json;
using Kleaner.SpecialOps;
using Kleaner.WebHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace Kleaner.WebHost.Tests;

public sealed class AdvancedApiTests : IDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    public AdvancedApiTests()
    {
        _app = WebHostAppFactory.Build(new KleanerWebHostOptions
        {
            Port = 45172, Token = "advanced-test-token", UseTestServer = true, EnableIdleExit = false,
            WslDetector = () => new[] { new VhdxInfo(@"C:\\wsl\\ext4.vhdx", 1024L) },
            RegistryScanner = () => new[] { new BrokenInstallEntry("HKCU\\x", "Old App", "C:\\missing", "目录不存在") },
        });
        _app.Start(); _client = _app.GetTestClient();
        _client.BaseAddress = new Uri("http://127.0.0.1:45172");
        _client.DefaultRequestHeaders.Add("X-Kleaner-Token", "advanced-test-token");
        _client.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:45172");
    }

    public void Dispose() { _client.Dispose(); _app.DisposeAsync().AsTask().GetAwaiter().GetResult(); }

    [Fact]
    public async Task AdvancedEndpoints_ExposeOnlyReadOnlySpecialOpsResults()
    {
        using var wsl = JsonDocument.Parse(await _client.GetStringAsync("/api/advanced/wsl"));
        using var registry = JsonDocument.Parse(await _client.GetStringAsync("/api/advanced/registry"));
        Assert.Contains("wsl --shutdown", wsl.RootElement[0].GetProperty("guide").GetString());
        Assert.Equal("Old App", registry.RootElement[0].GetProperty("displayName").GetString());
    }
}
