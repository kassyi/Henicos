using System.Numerics;

namespace Kassyi.GSharp.Cryptography;

/// <summary>
/// Field arithmetic modulo <c>p = 2^255 - 19</c> and point decoding for Ed25519 (RFC 8032 Section 5.1).
/// </summary>
/// <remarks>
/// Kept free of hashing so the netstandard2.0 source generator can link it without the signing code.
/// <list type="bullet">
///   <item><description><see cref="P"/>: the field prime <c>2^255 - 19</c>.</description></item>
///   <item><description><see cref="D"/>: the curve constant <c>d = -121665 / 121666 mod p</c>.</description></item>
///   <item><description><see cref="I"/>: a square root of <c>-1</c>, <c>2^((p - 1) / 4) mod p</c>.</description></item>
/// </list>
/// </remarks>
internal static class Ed25519Field
{
    public static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;
    public static readonly BigInteger D = Mod(-121665 * ModInverse(121666, P), P);
    public static readonly BigInteger I = BigInteger.ModPow(2, (P - 1) / 4, P);

    public static BigInteger Mod(BigInteger x, BigInteger m)
    {
        var r = x % m;
        return r < 0 ? r + m : r;
    }

    public static BigInteger ModInverse(BigInteger a, BigInteger m) =>
        BigInteger.ModPow(Mod(a, m), m - 2, m);

    /// <summary>
    /// Recovers <c>x</c> from <c>y</c> and the sign bit using the square-root method for <c>p ≡ 5 (mod 8)</c>
    /// (RFC 8032 Section 5.1.3). Returns <c>-1</c> when <c>y</c> is non-canonical or <c>x²</c> has no square root.
    /// </summary>
    public static BigInteger RecoverX(BigInteger y, int sign)
    {
        if (y >= P)
        {
            return -1;
        }

        var x2 = Mod((y * y - 1) * ModInverse(D * y * y + 1, P), P);
        if (x2 == 0)
        {
            return sign != 0 ? -1 : 0;
        }

        var x = BigInteger.ModPow(x2, (P + 3) / 8, P);
        if (Mod(x * x - x2, P) != 0)
        {
            x = Mod(x * I, P);
        }

        if (Mod(x * x - x2, P) != 0)
        {
            return -1;
        }

        if ((x % 2 == 0 ? 0 : 1) != sign)
        {
            x = P - x;
        }

        return x;
    }

    /// <summary>
    /// Decodes a 32-byte point encoding into its affine coordinates (RFC 8032 Section 5.1.3).
    /// </summary>
    public static bool TryDecodePoint(ReadOnlySpan<byte> enc, out BigInteger x, out BigInteger y)
    {
        x = default;
        y = default;
        if (enc.Length != Ed25519PublicKeyValidator.PublicKeySize)
        {
            return false;
        }

        var encCopy = enc.ToArray();
        var sign = (encCopy[31] >> 7) & 1;
        encCopy[31] &= 0x7F;

        y = FromLittleEndian(encCopy);
        if (y >= P)
        {
            return false;
        }

        x = RecoverX(y, sign);
        return x >= 0;
    }

    public static BigInteger FromLittleEndian(ReadOnlySpan<byte> bytes)
    {
        var unsigned = new byte[bytes.Length + 1];
        bytes.CopyTo(unsigned);
        return new BigInteger(unsigned);
    }
}
