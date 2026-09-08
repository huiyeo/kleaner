using Kleaner.Core;

namespace Kleaner.Core.Tests;

/// <summary>Ed25519 验证实现以 RFC 8032 官方向量为准，并以 openssl 产出的真实签名交叉验证。</summary>
public sealed class Ed25519VerifyTests
{
    private static byte[] Hex(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }

    [Fact]
    public void RFC8032向量一验证通过且重签名一致()
    {
        const string sk = "9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60";
        const string pk = "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a";
        // 本向量经 Node WebCrypto 权威实现复核为 VALID 后固化。
        const string officialSignature = "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b";

        Assert.True(Ed25519Verify.Verify(Hex(pk), [], Hex(officialSignature)));
        var ownSignature = Convert.ToHexString(Ed25519Verify.SignData(Hex(sk), [])).ToLowerInvariant();
        Assert.Equal(officialSignature, ownSignature);

        var tampered = Hex(officialSignature);
        tampered[0] ^= 0x01;
        Assert.False(Ed25519Verify.Verify(Hex(pk), [], tampered));
        Assert.False(Ed25519Verify.Verify(Hex(pk), [0x01], Hex(officialSignature)));
    }

    [Fact]
    public void openssl签出的真实签名验证通过()
    {
        // 密钥对与签名均由 openssl 3.5.6 生成：pkeyutl -sign -rawin，消息 "payload"。
        const string publicKeyHex = "48aea5aa3686be231f0f71f1a144a03e00160fa14a5aa0feb084085d6e2d2af0";
        const string signatureHex = "216e35bac8f12b5f087a14f8d52f88220d45e2e34a6c097d0f9e4fbc8bd1f01739e241e6f093fa2ac120e9c020ce7ea7c9d61162c089194d4c5ce04054be5a0c";
        Assert.True(Ed25519Verify.Verify(
            Hex(publicKeyHex),
            "payload"u8.ToArray(),
            Hex(signatureHex)));
        var tampered = Hex(signatureHex);
        tampered[1] ^= 0x80;
        Assert.False(Ed25519Verify.Verify(Hex(publicKeyHex), "payload"u8.ToArray(), tampered));
    }

    [Fact]
    public void 畸形长度与随机签名拒绝()
    {
        var pk = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        Assert.False(Ed25519Verify.Verify(pk, [1, 2, 3], []));
        Assert.False(Ed25519Verify.Verify([], [], []));
        Assert.False(Ed25519Verify.Verify(pk, [1], System.Security.Cryptography.RandomNumberGenerator.GetBytes(64)));
    }
}
