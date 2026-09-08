using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Kleaner.Core;

/// <summary>官方规则清单。signature 覆盖 CanonicalPayload(version, publishedUtc, minAppVersion, rulesSha512)。</summary>
public sealed record RuleManifest(
    string Version,
    string PublishedUtc,
    string MinAppVersion,
    string RulesSha512,
    string? Signature = null);

/// <summary>
/// 官方规则更新信任根：固定的发布地址与内嵌公钥。公钥对应的私钥由项目所有者离线保管，
/// 绝不入仓库或客户端；任何「用户输入 URL/摘要」的更新途径都不构成官方发布（ADR 依据见工单 12）。
/// </summary>
public static class RuleTrust
{
    public const string OfficialManifestUrl =
        "https://raw.githubusercontent.com/huiyeo/kleaner/rules-channel/rules-manifest.json";

    public const string OfficialRulesUrl =
        "https://raw.githubusercontent.com/huiyeo/kleaner/rules-channel/rules.v1.json";

    /// <summary>Ed25519 原始公钥（base64）。换钥=发新应用版本，旧钥立即失效。</summary>
    public const string OfficialPublicKeyBase64 = "6X6OrBIoxw2MnCY54tZthu5UmBedldrk2yVm1Y9V7yA=";

    /// <summary>清单发布时间允许的时钟偏差：发布时间比本地时钟超前超过该值即拒绝。</summary>
    public static readonly TimeSpan PublishedFutureTolerance = TimeSpan.FromHours(24);

    /// <summary>签名覆盖的规范字节序列；任何字段变动都会使签名失效。摘要统一小写十六进制。</summary>
    public static string CanonicalPayload(string version, string publishedUtc, string minAppVersion, string rulesSha512Hex) =>
        $"kleaner-rules-manifest v1\n{version}\n{publishedUtc}\n{minAppVersion}\n{rulesSha512Hex.ToLowerInvariant()}";

    /// <summary>严格语义版本解析；其余格式一律拒绝，保证单调比较有意义。</summary>
    public static bool TryParseSemver(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Trim().Split('.');
        if (parts.Length is < 2 or > 3) return false;
        var numbers = new List<int>();
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var n) || n < 0) return false;
            if (part.Length > 1 && part[0] == '0') return false; // 前导零不是合法语义版本
            numbers.Add(n);
        }
        while (numbers.Count < 3) numbers.Add(0);
        version = new Version(numbers[0], numbers[1], numbers[2]);
        return true;
    }
}

/// <summary>清单校验结论的纯逻辑：签名、摘要、单调性、应用版本门槛与发布时钟，全部可独立测试。</summary>
public static class RuleManifestVerifier
{
    public static (bool Accepted, string? Error) Verify(
        RuleManifest? manifest,
        byte[] rulesPayload,
        string? currentAppVersion,
        string? lastAcceptedVersion,
        string publicKeyBase64,
        DateTimeOffset nowUtc)
    {
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Signature))
            return (false, "清单缺失或未签名，拒绝接受");
        if (!RuleTrust.TryParseSemver(manifest.Version, out var manifestVersion))
            return (false, $"清单版本格式非法：{manifest.Version}");
        if (!RuleTrust.TryParseSemver(manifest.MinAppVersion, out var minApp))
            return (false, $"清单最低应用版本格式非法：{manifest.MinAppVersion}");

        var rulesSha = Convert.ToHexString(SHA512.HashData(rulesPayload)).ToLowerInvariant();
        // 摘要核对先于签名：规则负载在传输中被改时给出更直接的原因。
        if (!string.Equals(rulesSha, manifest.RulesSha512?.ToLowerInvariant(), StringComparison.Ordinal))
            return (false, "规则包 SHA512 摘要与清单不符，已拒绝");

        var canonical = RuleTrust.CanonicalPayload(manifest.Version, manifest.PublishedUtc, manifest.MinAppVersion, rulesSha);
        try
        {
            var publicKeyBytes = Convert.FromBase64String(publicKeyBase64);
            var signatureBytes = Convert.FromBase64String(manifest.Signature);
            // .NET BCL 未提供 Ed25519，使用自带 RFC 8032 仅验证实现（见 Ed25519VerifyTests 的官方向量与 openssl 交叉验证）。
            if (!Ed25519Verify.Verify(publicKeyBytes, Encoding.UTF8.GetBytes(canonical), signatureBytes))
                return (false, "清单签名校验失败，可能被篡改或非官方发布，已拒绝");
        }
        catch (FormatException)
        {
            return (false, "清单签名不是合法的 base64，已拒绝");
        }

        if (lastAcceptedVersion is not null &&
            RuleTrust.TryParseSemver(lastAcceptedVersion, out var accepted) &&
            manifestVersion <= accepted)
            return (false, $"清单版本 {manifest.Version} 不高于已接受的 {lastAcceptedVersion}，疑似降级，已拒绝");

        if (currentAppVersion != null &&
            RuleTrust.TryParseSemver(currentAppVersion, out var current) &&
            minApp > current)
            return (false, $"清单要求应用版本不低于 {manifest.MinAppVersion}，当前 {currentAppVersion}，请先升级应用");

        if (DateTimeOffset.TryParse(manifest.PublishedUtc, out var published) &&
            published > nowUtc + RuleTrust.PublishedFutureTolerance)
            return (false, $"清单发布时间 {manifest.PublishedUtc} 在未来，时钟异常或清单伪造，已拒绝");

        return (true, null);
    }
}
