using System.Security.Cryptography;
using System.Text;

namespace Kleaner.Core;

/// <summary>规则在线更新：下载 → SHA512 校验 → 语义校验 → 写入用户目录覆盖。校验任何一步失败都拒绝应用。</summary>
public static class RuleUpdateService
{
    public static bool VerifySha512(byte[] payload, string expectedHex)
    {
        var actual = Convert.ToHexString(SHA512.HashData(payload));
        var expected = expectedHex.Trim().Replace(" ", string.Empty);
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    public static string LocalOverridePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Kleaner", "rules", "rules.v1.json");

    /// <summary>候选加载顺序：用户目录覆盖（更新通道下发）→ 应用内置规则。</summary>
    public static (string Path, RuleSet Set) LoadEffective(string bundledPath)
    {
        var local = LocalOverridePath();
        if (File.Exists(local))
            return (local, RuleSetLoader.LoadFromFile(local));
        return (bundledPath, RuleSetLoader.LoadFromFile(bundledPath));
    }

    public static async Task<string?> CheckAndUpdateAsync(string url, string expectedSha512, HttpClient? http = null)
    {
        http ??= new HttpClient();
        byte[] payload;
        try
        {
            payload = await http.GetByteArrayAsync(url);
        }
        catch (Exception ex)
        {
            return $"下载失败：{ex.Message}";
        }

        if (!VerifySha512(payload, expectedSha512))
            return "SHA512 校验失败，已拒绝应用本次更新";

        RuleSet set;
        try
        {
            set = RuleSetLoader.LoadFromJson(Encoding.UTF8.GetString(payload));
        }
        catch (Exception ex)
        {
            return $"规则解析失败：{ex.Message}";
        }

        var errors = RuleSetLoader.Validate(set);
        if (errors.Count > 0)
            return "规则校验未通过：" + string.Join("；", errors);

        var target = LocalOverridePath();
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllBytesAsync(target, payload);
        return null;
    }
}
