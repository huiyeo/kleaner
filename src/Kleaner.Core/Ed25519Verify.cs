using System.Numerics;
using System.Security.Cryptography;

namespace Kleaner.Core;

/// <summary>
/// Ed25519 签名验证（RFC 8032），仅验证、无私钥路径。BCL 至今未提供 Ed25519，
/// 为保持 Kleaner.Core 零外部依赖而自带本实现；正确性由 RFC 8032 官方向量与
/// openssl 产出的真实签名交叉验证（见 Ed25519VerifyTests 与 RuleUpdateTrustTests）。
/// BigInteger 为非常量时间运算——验证侧不接触任何秘密，无侧信道暴露面。
/// </summary>
internal static class Ed25519Verify
{
    private static readonly BigInteger Q = (BigInteger.One << 255) - 19;
    private static readonly BigInteger L = BigInteger.Parse("7237005577332262213973186563042994240857116359379907606001950938285454250989");
    private static readonly BigInteger D = BigInteger.Parse("37095705934669439343138083508754565189542113879843219016388785533085940283555");
    private static readonly BigInteger SqrtM1 = BigInteger.Parse("19681161376707505956807079304988542015446066515923890162744021073123829784752");
    private static readonly (BigInteger X, BigInteger Y) BasePoint = (
        BigInteger.Parse("15112221349535400772501151409588531511454012693041857206046113283949847762202"),
        BigInteger.Parse("46316835694926478169428394003475163141307993866256225615783033603165251855960"));

    private static BigInteger Mod(BigInteger value) =>
        ((value % Q) + Q) % Q;

    private static BigInteger Inverse(BigInteger value) =>
        BigInteger.ModPow(Mod(value), Q - 2, Q);

    private static byte[] EncodePoint((BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) point)
    {
        var inverseZ = Inverse(point.Z);
        var x = Mod(point.X * inverseZ);
        var y = Mod(point.Y * inverseZ);
        var bytes = y.ToByteArray(isUnsigned: true, isBigEndian: false);
        Array.Resize(ref bytes, 32);
        if (x % 2 == 1)
            bytes[31] |= 0x80;
        return bytes;
    }

    private static (BigInteger X, BigInteger Y, BigInteger Z, BigInteger T)? DecodePoint(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length != 32) return null;
        var buffer = encoded.ToArray();
        var sign = (buffer[31] & 0x80) != 0;
        buffer[31] &= 0x7F;
        var y = new BigInteger(buffer);
        if (y >= Q) return null;

        var ySquared = Mod(y * y);
        var u = Mod(ySquared - 1);
        var v = Mod(D * ySquared + 1);
        var x = Mod(u * Inverse(v));
        x = BigInteger.ModPow(x, (Q + 3) / 8, Q);
        if (Mod(x * x) != Mod(u * Inverse(v)))
            x = Mod(x * SqrtM1);
        if (Mod(x * x) != Mod(u * Inverse(v)))
            return null; // 不在曲线上
        if (x == 0 && sign)
            return null;
        if (x % 2 != (sign ? 1 : 0))
            x = Q - x;
        return (x, y, BigInteger.One, Mod(x * y));
    }

    // a = -1 的扭爱德华兹曲线统一加法公式（add-2008-hwcd）：
    // A=(Y1-X1)(Y2-X2) B=(Y1+X1)(Y2+X2) C=2d·T1·T2 D=2Z1Z2
    // E=B-A F=D-C G=D+C H=B+A → X3=E·F Y3=G·H T3=E·H Z3=F·G；对加倍同样成立。
    private static (BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) Add(
        (BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) a,
        (BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) b)
    {
        var coefficientA = Mod((a.Y - a.X) * (b.Y - b.X));
        var coefficientB = Mod((a.Y + a.X) * (b.Y + b.X));
        var coefficientC = Mod(2 * a.T * D % Q * b.T);
        var coefficientD = Mod(2 * a.Z * b.Z);
        var e = Mod(coefficientB - coefficientA);
        var f = Mod(coefficientD - coefficientC);
        var g = Mod(coefficientD + coefficientC);
        var h = Mod(coefficientB + coefficientA);
        return (Mod(e * f), Mod(g * h), Mod(f * g), Mod(e * h));
    }

    private static (BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) ScalarMul(
        BigInteger scalar, (BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) point)
    {
        scalar %= L;
        if (scalar.Sign < 0) scalar += L;
        var result = (BigInteger.Zero, BigInteger.One, BigInteger.One, BigInteger.Zero);
        var addend = point;
        while (scalar > 0)
        {
            if (!scalar.IsEven) result = Add(result, addend);
            addend = Add(addend, addend);
            scalar >>= 1;
        }
        return result;
    }

    // BigInteger(byte[]) 按「有符号」小端解释：哈希末字节最高位为 1 时会变负数，
    // 使 mod L 的结果整体偏移——哈希与标量一律走无符号解释。
    private static BigInteger UnsignedLittleEndian(ReadOnlySpan<byte> bytes)
    {
        var unsigned = new byte[bytes.Length + 1];
        bytes.CopyTo(unsigned);
        return new BigInteger(unsigned);
    }

    private static BigInteger ReduceL(ReadOnlySpan<byte> bytes) =>
        UnsignedLittleEndian(bytes) % L;

    /// <summary>验证 Ed25519 签名：公钥 32 字节、签名 64 字节（R‖S，S 必须规约）。任何解码失败都返回 false。</summary>
    public static bool Verify(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature)
    {        if (publicKey.Length != 32 || signature.Length != 64) return false;
        var publicKeyPoint = DecodePoint(publicKey);
        if (publicKeyPoint is null) return false;
        var rPoint = DecodePoint(signature[..32]);
        if (rPoint is null) return false;

        var s = new BigInteger(signature[32..]);
        if (s.Sign < 0 || s >= L) return false; // S 必须规约，非规约签名直接拒绝

        var hashInput = new byte[32 + 32 + message.Length];
        signature[..32].CopyTo(hashInput);
        publicKey.CopyTo(hashInput.AsSpan(32));
        message.CopyTo(hashInput.AsSpan(64));
        // RFC 8032：k = SHA512(R‖A‖M) 解释为无符号小端整数后对群阶 L 取模。
        var k = ReduceL(SHA512.HashData(hashInput));

        // 群方程 [S]B == R + [k]A：计算 R + [k]A 后与 [S]B 的编码比较（两侧都取规范编码）。
        var expected = Add(rPoint.Value, ScalarMul(k, publicKeyPoint.Value));
        var expectedEncoded = EncodePoint(expected);
        var sEncoded = EncodePoint(ScalarMul(s, (BasePoint.X, BasePoint.Y, BigInteger.One, Mod(BasePoint.X * BasePoint.Y))));
        return expectedEncoded.AsSpan().SequenceEqual(sEncoded);
    }

    internal static byte[] RoundTripForDebug(ReadOnlySpan<byte> encoded)
    {
        var point = DecodePoint(encoded);
        return point is null ? [] : EncodePoint(point.Value);
    }

    internal static bool DecodedVsConstructedForDebug(ReadOnlySpan<byte> seed)
    {
        var hash = SHA512.HashData(seed);
        var clamped = (byte[])hash[..32].Clone();
        clamped[0] &= 248;
        clamped[31] &= 127;
        clamped[31] |= 64;
        var a = new BigInteger(clamped);
        var constructed = ScalarMul(a, (BasePoint.X, BasePoint.Y, BigInteger.One, Mod(BasePoint.X * BasePoint.Y)));
        var decoded = DecodePoint(EncodePoint(constructed))!.Value;
        var k = (BigInteger.One << 200) + 12345;
        return EncodePoint(ScalarMul(k, decoded)).AsSpan().SequenceEqual(EncodePoint(ScalarMul(k, constructed)));
    }

    internal static byte[] VerifySumForDebug(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature)
    {
        var publicKeyPoint = DecodePoint(publicKey)!.Value;
        var rPoint = DecodePoint(signature[..32])!.Value;
        var s = new BigInteger(signature[32..]);
        var hashInput = new byte[64 + message.Length];
        signature[..32].CopyTo(hashInput);
        publicKey.CopyTo(hashInput.AsSpan(32));
        message.CopyTo(hashInput.AsSpan(64));
        var k = (new BigInteger(SHA512.HashData(hashInput)) % L + L) % L;
        return EncodePoint(Add(ScalarMul(s, (BasePoint.X, BasePoint.Y, BigInteger.One, Mod(BasePoint.X * BasePoint.Y))),
            ScalarMul(k, publicKeyPoint)));
    }

    internal static byte[] PublicKeyFromSeedForDebug(ReadOnlySpan<byte> seed)
    {
        var hash = SHA512.HashData(seed);
        var clamped = (byte[])hash[..32].Clone();
        clamped[0] &= 248;
        clamped[31] &= 127;
        clamped[31] |= 64;
        var a = new BigInteger(clamped);
        return EncodePoint(ScalarMul(a, (BasePoint.X, BasePoint.Y, BigInteger.One, Mod(BasePoint.X * BasePoint.Y))));
    }

    internal static byte[] ScalarMulForDebug(BigInteger scalar, BigInteger secondScalar)
    {
        var bPoint = (BasePoint.X, BasePoint.Y, BigInteger.One, Mod(BasePoint.X * BasePoint.Y));
        var publicKey = Convert.FromBase64String(RuleTrust.OfficialPublicKeyBase64);
        _ = publicKey; // 占位避免误用；本方法仅为验证路径调试。
        var aPoint = DecodePoint(Convert.FromHexString("d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a"))!.Value;
        return EncodePoint(Add(ScalarMul(scalar, bPoint), ScalarMul(secondScalar, aPoint)));
    }

    internal static BigInteger DecodeForDebug(ReadOnlySpan<byte> encoded)
    {
        var buffer = encoded.ToArray();
        buffer[31] &= 0x7F;
        return new BigInteger(buffer);
    }

    /// <summary>
    /// RFC 8032 签名镜像，仅测试程序集用于构造签名样例（生产签名走 openssl，见 scripts/sign-rules.ps1）。
    /// 私钥为 32 字节种子；实现不刻意防护时序，绝不用于生产签名。
    /// </summary>
    internal static byte[] SignData(ReadOnlySpan<byte> privateKeySeed32, ReadOnlySpan<byte> message)
    {
        var hash = SHA512.HashData(privateKeySeed32);
        var clamped = (byte[])hash[..32].Clone();
        clamped[0] &= 248;
        clamped[31] &= 127;
        clamped[31] |= 64;
        var a = new BigInteger(clamped);
        var publicKey = EncodePoint(ScalarMul(a, (BasePoint.X, BasePoint.Y, BigInteger.One, Mod(BasePoint.X * BasePoint.Y))));

        var rSeed = new byte[32 + message.Length];
        hash[32..64].CopyTo(rSeed);
        message.CopyTo(rSeed.AsSpan(32));
        var r = ReduceL(SHA512.HashData(rSeed));
        var rEncoded = EncodePoint(ScalarMul(r, (BasePoint.X, BasePoint.Y, BigInteger.One, Mod(BasePoint.X * BasePoint.Y))));

        var kSeed = new byte[64 + message.Length];
        rEncoded.CopyTo(kSeed);
        publicKey.CopyTo(kSeed.AsSpan(32));
        message.CopyTo(kSeed.AsSpan(64));
        var k = ReduceL(SHA512.HashData(kSeed));
        var s = (r + k * a) % L;

        var signature = new byte[64];
        rEncoded.CopyTo(signature);
        s.ToByteArray(isUnsigned: true, isBigEndian: false).CopyTo(signature.AsSpan(32));
        return signature;
    }
}
