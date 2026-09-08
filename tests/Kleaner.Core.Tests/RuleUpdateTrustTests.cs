using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kleaner.Core;

namespace Kleaner.Core.Tests;

/// <summary>
/// 工单 12：规则更新的官方签名信任链。全部用例使用仅测试可见的 Ed25519 密钥对，
/// 生产公钥内嵌于 RuleTrust；测试只经注入的假 HTTP 与临时状态目录，不触真实更新源。
/// </summary>
public sealed class RuleUpdateTrustTests
{
    // 原始 32 字节 Ed25519 私钥种子（openssl PEM 的 DER 尾 32 字节）；对应公钥见下。
    private const string TestPrivateKeyBase64 = "BpquOiZsRdcFp0bRrMbrfRbf4uz0xxdw2JDBfarCJqk=";
    private const string TestPublicKeyBase64 = "SK6lqjaGviMfD3HxoUSgPgAWD6FKWqD+sIQIXW4tKvA=";

    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly string _stateDir = Path.Combine(Path.GetTempPath(), "kleaner-trust-" + Guid.NewGuid().ToString("N"));

    public RuleUpdateTrustTests() => Directory.CreateDirectory(_stateDir);

    private static string Sha512Hex(byte[] payload) => Convert.ToHexString(SHA512.HashData(payload)).ToLowerInvariant();

    private static string Sign(string canonical)
    {
        var signature = Ed25519Verify.SignData(Convert.FromBase64String(TestPrivateKeyBase64), Encoding.UTF8.GetBytes(canonical));
        return Convert.ToBase64String(signature);
    }

    private static readonly JsonSerializerOptions ManifestJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static string Manifest(string version, string rulesSha, string minAppVersion = "0.2.4") =>
        JsonSerializer.Serialize(new RuleManifest(version, "2026-09-06T11:00:00+00:00", minAppVersion, rulesSha,
            Sign(RuleTrust.CanonicalPayload(version, "2026-09-06T11:00:00+00:00", minAppVersion, rulesSha))), ManifestJson);

    private const string ValidRulesJson = """
        {"schemaVersion":1,"rules":[{"id":"trust-test","name":"信任测试规则","category":"temp","risk":"low",
        "paths":["%TEMP%/kleaner-trust-fixture/**"],"ageDays":0,"requiresElevation":false,
        "safetyNotes":"仅测试使用的临时目录规则说明，用于验证更新信任链的语义校验路径，绝不触及真实用户文件。"}]}
        """;

    private FakeHttp FakeHttp() => new(new Dictionary<string, byte[]>
    {
        ["manifest"] = Encoding.UTF8.GetBytes(Manifest("1.0.0", Sha512Hex(Encoding.UTF8.GetBytes(ValidRulesJson)))),
        ["rules"] = Encoding.UTF8.GetBytes(ValidRulesJson),
    });

    private Task<string?> Update(FakeHttp http, string appVersion = "0.2.4", string? lastAccepted = null) =>
        RuleUpdateService.UpdateFromOfficialAsync(
            appVersion,
            new HttpClient(http),
            manifestUrl: "https://example.test/rules-manifest.json",
            rulesUrl: "https://example.test/rules.v1.json",
            expectedPublicKey: TestPublicKeyBase64,
            utcNow: () => Now,
            stateDirectory: _stateDir);

    private string OverridePath => Path.Combine(_stateDir, "rules.v1.json");

    private string LastGoodPath => Path.Combine(_stateDir, "rules.last-good.json");

    [Fact]
    public async Task 签名与摘要正确的首个清单被接受并原子落盘()
    {
        var error = await Update(FakeHttp());

        Assert.Null(error);
        Assert.Equal(ValidRulesJson, File.ReadAllText(OverridePath).TrimEnd());
        Assert.True(File.Exists(LastGoodPath), "首次更新必须同时保留最后已知良好副本");
        var state = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(_stateDir, "manifest-state.json")));
        Assert.Equal("1.0.0", state.GetProperty("lastAcceptedVersion").GetString());
    }

    [Fact]
    public async Task 篡改清单版本使签名失效并被拒绝()
    {
        var http = FakeHttp();
        var sha = Sha512Hex(Encoding.UTF8.GetBytes(ValidRulesJson));
        const string published = "2026-09-06T11:00:00+00:00";
        // 用 1.0.0 的签名冒充 9.9.9 的清单——真实的篡改形态。
        http.Rewrite("manifest", JsonSerializer.Serialize(new RuleManifest("9.9.9", published, "0.2.4", sha,
            Sign(RuleTrust.CanonicalPayload("1.0.0", published, "0.2.4", sha))), ManifestJson));

        var error = await Update(http);

        Assert.NotNull(error);
        Assert.Contains("签名", error);
        Assert.False(File.Exists(OverridePath), "被拒绝的更新不得落盘");
    }

    [Fact]
    public async Task 篡改规则负载因摘要不符被拒绝()
    {
        var http = FakeHttp();
        http.Rewrite("rules", Encoding.UTF8.GetBytes(ValidRulesJson.Replace("trust-test", "evil-rule")));

        var error = await Update(http);

        Assert.NotNull(error);
        Assert.Contains("摘要", error);
        Assert.False(File.Exists(OverridePath));
    }

    [Fact]
    public async Task 版本不高于已接受版本按降级拒绝()
    {
        await Update(FakeHttp());
        var before = File.ReadAllText(OverridePath);

        var downgraded = FakeHttp();
        downgraded.Rewrite("manifest", Manifest("0.9.0", Sha512Hex(Encoding.UTF8.GetBytes(ValidRulesJson))));
        var error = await Update(downgraded);

        Assert.NotNull(error);
        Assert.Contains("降级", error);
        Assert.Equal(before, File.ReadAllText(OverridePath));
    }

    [Fact]
    public async Task 清单要求更高应用版本时拒绝并提示升级()
    {
        var higher = FakeHttp();
        higher.Rewrite("manifest", Manifest("1.0.0", Sha512Hex(Encoding.UTF8.GetBytes(ValidRulesJson)), minAppVersion: "99.0.0"));

        var error = await Update(higher, appVersion: "0.2.4");

        Assert.NotNull(error);
        Assert.Contains("升级", error);
        Assert.False(File.Exists(OverridePath));
    }

    [Fact]
    public async Task 发布时间晚于容忍窗口的清单被假时钟拒绝()
    {
        var future = FakeHttp();
        var sha = Sha512Hex(Encoding.UTF8.GetBytes(ValidRulesJson));
        const string published = "2026-09-20T00:00:00+00:00";
        future.Rewrite("manifest", JsonSerializer.Serialize(new RuleManifest("1.0.0", published, "0.2.4", sha,
            Sign(RuleTrust.CanonicalPayload("1.0.0", published, "0.2.4", sha))), ManifestJson));

        var error = await Update(future);

        Assert.NotNull(error);
        Assert.Contains("未来", error);
    }

    [Fact]
    public async Task 下载失败时本地覆盖保持不变()
    {
        await Update(FakeHttp());
        var before = File.ReadAllText(OverridePath);
        var broken = new FakeHttp(new Dictionary<string, byte[]>()); // 任何请求都 404

        var error = await Update(broken);

        Assert.NotNull(error);
        Assert.Equal(before, File.ReadAllText(OverridePath));
    }

    [Fact]
    public void 本地覆盖损坏时回退最后已知良好()
    {
        File.WriteAllText(Path.Combine(_stateDir, "rules.last-good.json"), ValidRulesJson);
        File.WriteAllText(OverridePath, "{corrupted");

        var (path, set) = RuleUpdateService.LoadEffective("bundled-fallback.json", _stateDir);

        Assert.Equal(LastGoodPath, path);
        Assert.Single(set.Rules);
    }

    [Fact]
    public void 覆盖与最后已知良好都缺失时回退内置规则()
    {
        var bundled = Path.Combine(_stateDir, "bundled.json");
        File.WriteAllText(bundled, ValidRulesJson);
        var (path, _) = RuleUpdateService.LoadEffective(bundled, _stateDir);
        Assert.Equal(bundled, path);
    }

    [Fact]
    public async Task 非语义版本格式被拒绝()
    {
        var bad = FakeHttp();
        var sha = Sha512Hex(Encoding.UTF8.GetBytes(ValidRulesJson));
        bad.Rewrite("manifest", JsonSerializer.Serialize(new RuleManifest(" ABC", "2026-09-06T11:00:00+00:00", "0.2.4", sha,
            Sign(RuleTrust.CanonicalPayload(" ABC", "2026-09-06T11:00:00+00:00", "0.2.4", sha))), ManifestJson));

        Assert.NotNull(await Update(bad));
    }

    [Fact]
    public void 诊断_测试密钥自洽与清单往返()
    {
        var sha = Sha512Hex(Encoding.UTF8.GetBytes(ValidRulesJson));
        var version = "1.0.0";
        var published = "2026-09-06T11:00:00+00:00";
        var minApp = "0.2.4";
        var canonical = RuleTrust.CanonicalPayload(version, published, minApp, sha);
        var signature = Sign(canonical);
        Assert.True(Ed25519Verify.Verify(Convert.FromBase64String(TestPublicKeyBase64),
            Encoding.UTF8.GetBytes(canonical), Convert.FromBase64String(signature)), "密钥对自洽");

        var json = JsonSerializer.Serialize(new RuleManifest(version, published, minApp, sha, signature), ManifestJson);
        var restored = JsonSerializer.Deserialize<RuleManifest>(json, ManifestJson);
        Assert.Equal(canonical, RuleTrust.CanonicalPayload(restored!.Version, restored.PublishedUtc, restored.MinAppVersion, restored.RulesSha512));
    }
}

/// <summary>按 URL 前缀匹配返回 canned 响应的假 HTTP；未命中返回 404。</summary>
public sealed class FakeHttp(Dictionary<string, byte[]> responses) : HttpMessageHandler
{
    public void Rewrite(string key, string content) => responses[key] = Encoding.UTF8.GetBytes(content);
    public void Rewrite(string key, byte[] content) => responses[key] = content;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        var key = responses.Keys.FirstOrDefault(url.Contains)
            ?? throw new KeyNotFoundException($"假 HTTP 未配置：{url}");
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(responses[key]),
        });
    }
}
