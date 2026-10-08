using System.Numerics;
using System.Security.Cryptography;

namespace Kassyi.GSharp.Cryptography;

/// <summary>
/// Lightweight, self-contained Ed25519 implementation compliant with RFC 8032.
/// Provides key generation, signing, and verification without external dependencies.
/// </summary>
internal static class Ed25519
{
    public const int PublicKeySize = 32;
    public const int SecretKeySeedSize = 32;
    public const int SignatureSize = 64;

    // RFC 8032 Section 7.1 Test Vector 1 (Ed25519)
    private static readonly byte[] s_defaultDevKeySeed =
    [
        0x9d, 0x61, 0xb1, 0x9d, 0xef, 0xfd, 0x5a, 0x60,
        0xba, 0x84, 0x4a, 0xf4, 0x92, 0xec, 0x2c, 0xc4,
        0x44, 0x49, 0xc5, 0x69, 0x7b, 0x32, 0x69, 0x19,
        0x70, 0x3b, 0xac, 0x03, 0x1c, 0xae, 0x7f, 0x60
    ];

    private static readonly byte[] s_defaultDevPublicKey =
    [
        0xd7, 0x5a, 0x98, 0x01, 0x82, 0xb1, 0x0a, 0xb7,
        0xd5, 0x4b, 0xfe, 0xd3, 0xc9, 0x64, 0x07, 0x3a,
        0x0e, 0xe1, 0x72, 0xf3, 0xda, 0xa6, 0x23, 0x25,
        0xaf, 0x02, 0x1a, 0x68, 0xf7, 0x07, 0x51, 0x1a
    ];

    /// <summary>
    /// Gets the default development secret key seed (RFC 8032 Section 7.1 Test Vector 1).
    /// </summary>
    public static ReadOnlySpan<byte> DefaultDevKeySeed => s_defaultDevKeySeed;

    /// <summary>
    /// Gets the default development public key (RFC 8032 Section 7.1 Test Vector 1).
    /// </summary>
    public static ReadOnlySpan<byte> DefaultDevPublicKey => s_defaultDevPublicKey;

    private static readonly BigInteger s_p = BigInteger.Pow(2, 255) - 19;

    private static readonly BigInteger s_l = BigInteger.Pow(2, 252) +
                                             BigInteger.Parse("27742317777372353535851937790883648493");

    private static readonly BigInteger s_d = Mod(-121665 * ModInverse(121666, s_p), s_p);
    private static readonly BigInteger s_i = BigInteger.ModPow(2, (s_p - 1) / 4, s_p);

    private static readonly BigInteger s_by = Mod(4 * ModInverse(5, s_p), s_p);
    private static readonly BigInteger s_bx = RecoverX(s_by, 0);
    private static readonly Point s_basePoint = new(s_bx, s_by);

    private readonly struct Point(BigInteger x, BigInteger y)
    {
        public readonly BigInteger X = x;
        public readonly BigInteger Y = y;
    }

    private static BigInteger Mod(BigInteger x, BigInteger m)
    {
        var r = x % m;
        return r < 0 ? r + m : r;
    }

    private static BigInteger ModInverse(BigInteger a, BigInteger m) =>
        BigInteger.ModPow(Mod(a, m), m - 2, m);

    private static BigInteger RecoverX(BigInteger y, int sign)
    {
        if (y >= s_p)
        {
            return -1;
        }

        var x2 = Mod((y * y - 1) * ModInverse(s_d * y * y + 1, s_p), s_p);
        if (x2 == 0)
        {
            return sign != 0 ? -1 : 0;
        }

        var x = BigInteger.ModPow(x2, (s_p + 3) / 8, s_p);
        if (Mod(x * x - x2, s_p) != 0)
        {
            x = Mod(x * s_i, s_p);
        }

        if (Mod(x * x - x2, s_p) != 0)
        {
            return -1;
        }

        if ((x % 2 == 0 ? 0 : 1) != sign)
        {
            x = s_p - x;
        }

        return x;
    }

    private static Point PointAdd(Point p1, Point p2)
    {
        var x1 = p1.X;
        var y1 = p1.Y;
        var x2 = p2.X;
        var y2 = p2.Y;

        var dX1X2Y1Y2 = Mod(s_d * x1 * x2 * y1 * y2, s_p);
        var x3 = Mod((x1 * y2 + y1 * x2) * ModInverse(1 + dX1X2Y1Y2, s_p), s_p);
        var y3 = Mod((y1 * y2 + x1 * x2) * ModInverse(1 - dX1X2Y1Y2, s_p), s_p);

        return new Point(x3, y3);
    }

    private static Point ScalarMul(Point p, BigInteger e)
    {
        Point result = new(0, 1); // Neutral element
        var current = p;

        while (e > 0)
        {
            if ((e & 1) == 1)
            {
                result = PointAdd(result, current);
            }

            current = PointAdd(current, current);
            e >>= 1;
        }

        return result;
    }

    private static byte[] EncodePoint(Point p)
    {
        var bytes = To32BytesLittleEndian(p.Y);
        if ((p.X % 2) != 0)
        {
            bytes[31] |= 0x80;
        }

        return bytes;
    }

    private static bool TryDecodePoint(ReadOnlySpan<byte> enc, out Point p)
    {
        p = default;
        if (enc.Length != PublicKeySize)
        {
            return false;
        }

        var encCopy = enc.ToArray();
        var sign = (encCopy[31] >> 7) & 1;
        encCopy[31] &= 0x7F;

        var y = FromLittleEndian(encCopy);
        if (y >= s_p)
        {
            return false;
        }

        var x = RecoverX(y, sign);
        if (x < 0)
        {
            return false;
        }

        p = new Point(x, y);
        return true;
    }

    private static BigInteger FromLittleEndian(ReadOnlySpan<byte> bytes)
    {
        var unsigned = new byte[bytes.Length + 1];
        bytes.CopyTo(unsigned);
        return new BigInteger(unsigned);
    }

    private static byte[] To32BytesLittleEndian(BigInteger n)
    {
        var raw = n.ToByteArray();
        var result = new byte[32];
        var copyLen = Math.Min(raw.Length, 32);
        Array.Copy(raw, 0, result, 0, copyLen);
        return result;
    }

    private static byte[] Sha512(ReadOnlySpan<byte> data)
    {
        using var sha = SHA512.Create();
        return sha.ComputeHash([.. data]);
    }

    private static byte[] Sha512(ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> data)
    {
        var combined = new byte[prefix.Length + data.Length];
        prefix.CopyTo(combined.AsSpan(0, prefix.Length));
        data.CopyTo(combined.AsSpan(prefix.Length));
        using var sha = SHA512.Create();
        return sha.ComputeHash(combined);
    }

    private static byte[] Sha512(ReadOnlySpan<byte> p1, ReadOnlySpan<byte> p2, ReadOnlySpan<byte> p3)
    {
        var combined = new byte[p1.Length + p2.Length + p3.Length];
        p1.CopyTo(combined.AsSpan(0, p1.Length));
        p2.CopyTo(combined.AsSpan(p1.Length, p2.Length));
        p3.CopyTo(combined.AsSpan(p1.Length + p2.Length));
        using var sha = SHA512.Create();
        return sha.ComputeHash(combined);
    }

    public static byte[] GetPublicKey(ReadOnlySpan<byte> secretKeySeed)
    {
        if (secretKeySeed.Length != SecretKeySeedSize)
        {
            throw new ArgumentException($"Secret key seed must be {SecretKeySeedSize} bytes.", nameof(secretKeySeed));
        }

        var h = Sha512(secretKeySeed);
        h[0] &= 248;
        h[31] &= 127;
        h[31] |= 64;

        var s = FromLittleEndian(h.AsSpan(0, 32));
        var a = ScalarMul(s_basePoint, s);
        return EncodePoint(a);
    }

    public static byte[] Sign(ReadOnlySpan<byte> message, ReadOnlySpan<byte> secretKeySeed)
    {
        if (secretKeySeed.Length != SecretKeySeedSize)
        {
            throw new ArgumentException($"Secret key seed must be {SecretKeySeedSize} bytes.", nameof(secretKeySeed));
        }

        var pubKey = GetPublicKey(secretKeySeed);

        var h = Sha512(secretKeySeed);
        var sBytes = new byte[32];
        Array.Copy(h, 0, sBytes, 0, 32);
        sBytes[0] &= 248;
        sBytes[31] &= 127;
        sBytes[31] |= 64;
        var s = FromLittleEndian(sBytes);

        var prefix = h.AsSpan(32, 32);
        var rHash = Sha512(prefix, message);
        var r = Mod(FromLittleEndian(rHash), s_l);

        var rPoint = ScalarMul(s_basePoint, r);
        var rBytes = EncodePoint(rPoint);

        var kHash = Sha512(rBytes, pubKey, message);
        var k = Mod(FromLittleEndian(kHash), s_l);

        var sVal = Mod(r + k * s, s_l);
        var sFinalBytes = To32BytesLittleEndian(sVal);

        var signature = new byte[SignatureSize];
        Array.Copy(rBytes, 0, signature, 0, 32);
        Array.Copy(sFinalBytes, 0, signature, 32, 32);
        return signature;
    }

    public static bool Verify(ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> publicKey)
    {
        if (signature.Length != SignatureSize || publicKey.Length != PublicKeySize)
        {
            return false;
        }

        if (!TryDecodePoint(publicKey, out var aPoint))
        {
            return false;
        }

        var rBytes = signature.Slice(0, 32);
        var sBytes = signature.Slice(32, 32);

        var s = FromLittleEndian(sBytes);
        if (s >= s_l)
        {
            return false;
        }

        if (!TryDecodePoint(rBytes, out var rPoint))
        {
            return false;
        }

        var kHash = Sha512(rBytes, publicKey, message);
        var k = Mod(FromLittleEndian(kHash), s_l);

        var left = ScalarMul(s_basePoint, s);
        var right = PointAdd(rPoint, ScalarMul(aPoint, k));

        return left.X == right.X && left.Y == right.Y;
    }
}
