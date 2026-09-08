using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kleaner.Core;

/// <summary>规则在线更新：官方签名清单 → 签名/摘要/单调性校验 → 语义校验 → 原子替换。
/// 任何一步失败都拒绝应用并保持本地现状；本地覆盖损坏时回退最后已知良好副本，再回退内置规则。</summary>
public static class RuleUpdateService
{
    public static bool VerifySha512(byte[] payload, string expectedHex)
    {
        var actual = Convert.ToHexString(SHA512.HashData(payload));
        var expected = expectedHex.Trim().Replace(" ", string.Empty);
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    public static string LocalOverridePath(string? stateDirectory = null) =>
        Path.Combine(LocalRulesDirectory(stateDirectory), "rules.v1.json");

    public static string LocalRulesDirectory(string? stateDirectory = null) =>
        stateDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Kleaner", "rules");

    private static string LastGoodPath(string? stateDirectory) =>
        Path.Combine(LocalRulesDirectory(stateDirectory), "rules.last-good.json");

    private static string StatePath(string? stateDirectory) =>
        Path.Combine(LocalRulesDirectory(stateDirectory), "manifest-state.json");

    /// <summary>设置页展示用的一句话状态；不构成安全结论，只描述本地记录。</summary>
    public static string DescribeLocalState(string? stateDirectory = null)
    {
        try
        {
            var path = StatePath(stateDirectory);
            if (!File.Exists(path))
                return "尚未应用过官方规则更新（使用内置规则库）。";
            var state = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path));
            var version = state.TryGetProperty("lastAcceptedVersion", out var v) ? v.GetString() : null;
            return version is null ? "本地规则更新状态缺失。" : $"已接受的官方清单版本：{version}。";
        }
        catch
        {
            return "本地规则更新状态缺失。";
        }
    }

    /// <summary>候选加载顺序：用户目录覆盖（官方通道下发）→ 最后已知良好 → 应用内置规则。</summary>
    public static (string Path, RuleSet Set) LoadEffective(string bundledPath, string? stateDirectory = null)
    {
        foreach (var candidate in new[]
                 {
                     LocalOverridePath(stateDirectory),
                     LastGoodPath(stateDirectory),
                     bundledPath,
                 })
        {
            if (candidate is null || !File.Exists(candidate)) continue;
            try
            {
                return (candidate, RuleSetLoader.LoadFromFile(candidate));
            }
            catch
            {
                // 单个候选损坏不阻塞启动：继续回退到下一个来源，损坏文件留待官方通道覆盖。
            }
        }
        return (bundledPath, RuleSetLoader.LoadFromFile(bundledPath));
    }

    /// <summary>从官方源检查并应用规则更新；返回 null 表示成功（含「已是最新」），否则为面向用户的原因。</summary>
    public static async Task<string?> UpdateFromOfficialAsync(
        string? currentAppVersion,
        HttpClient? http = null,
        string? manifestUrl = null,
        string? rulesUrl = null,
        string? expectedPublicKey = null,
        Func<DateTimeOffset>? utcNow = null,
        string? stateDirectory = null)
    {
        manifestUrl ??= RuleTrust.OfficialManifestUrl;
        rulesUrl ??= RuleTrust.OfficialRulesUrl;
        expectedPublicKey ??= RuleTrust.OfficialPublicKeyBase64;
        var now = utcNow?.Invoke() ?? DateTimeOffset.UtcNow;
        var ownHttp = http is null;
        try
        {
            http ??= new HttpClient();
            byte[] manifestBytes;
            byte[] rulesPayload;
            try
            {
                manifestBytes = await http.GetByteArrayAsync(manifestUrl);
                rulesPayload = await http.GetByteArrayAsync(rulesUrl);
            }
            catch (Exception ex)
            {
                return $"下载失败：{ex.Message}";
            }

            RuleManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<RuleManifest>(manifestBytes,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            }
            catch (JsonException ex)
            {
                return $"清单不是合法 JSON：{ex.Message}";
            }

            var lastAcceptedVersion = ReadLastAcceptedVersion(stateDirectory);
            var (accepted, error) = RuleManifestVerifier.Verify(
                manifest, rulesPayload, currentAppVersion, lastAcceptedVersion, expectedPublicKey, now);
            if (!accepted)
                return error;

            // 语义校验是被签名摘要之外的最后一道闸：签名只证明官方发布，不证明内容合法。
            try
            {
                var errors = RuleSetLoader.Validate(RuleSetLoader.LoadFromJson(Encoding.UTF8.GetString(rulesPayload)));
                if (errors.Count > 0)
                    return "规则校验未通过：" + string.Join("；", errors);
            }
            catch (Exception ex)
            {
                return $"规则解析失败：{ex.Message}";
            }

            // 与已应用的规则完全一致时无需重写（重复检查幂等）。
            var overridePath = LocalOverridePath(stateDirectory);
            if (File.Exists(overridePath) &&
                File.ReadAllBytes(overridePath).AsSpan().SequenceEqual(rulesPayload))
                return null;

            AtomicWrite(overridePath, rulesPayload);
            AtomicWrite(LastGoodPath(stateDirectory), rulesPayload);
            var state = JsonSerializer.Serialize(new
            {
                lastAcceptedVersion = manifest!.Version,
                rulesSha512 = manifest.RulesSha512,
                acceptedUtc = now,
            });
            AtomicWrite(StatePath(stateDirectory), Encoding.UTF8.GetBytes(state));
            return null;
        }
        finally
        {
            if (ownHttp) http?.Dispose();
        }
    }

    private static string? ReadLastAcceptedVersion(string? stateDirectory)
    {
        try
        {
            var path = StatePath(stateDirectory);
            if (!File.Exists(path)) return null;
            var state = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path));
            return state.TryGetProperty("lastAcceptedVersion", out var v) ? v.GetString() : null;
        }
        catch
        {
            // 状态文件损坏按「无已接受版本」处理：签名与摘要仍是接受的必要条件，
            // 降级保护暂时失效但不会放行伪造内容。
            return null;
        }
    }

    // 同目录临时文件 + 落盘刷新 + 覆盖替换；替换失败不留半截文件。
    private static void AtomicWrite(string path, byte[] payload)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(payload);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try { File.Delete(temporary); } catch { }
            }
        }
    }
}
