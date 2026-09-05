
namespace Kassyi.GSharp.Stdlib.Math.BigInt.Algorithms;

/// <summary>CPU reference implementation of Lehmer's / Half-GCD algorithm for validation before GPU kernel execution.</summary>
public static class HalfGcdCpuReference
{
    /// <summary>Computes the greatest common divisor using Lehmer's 2x2 matrix reduction on truncated leading words.</summary>
    /// <param name="a">The first operand.</param>
    /// <param name="b">The second operand.</param>
    /// <returns>The greatest common divisor of <paramref name="a"/> and <paramref name="b"/>.</returns>
    public static BigInteger Gcd(BigInteger a, BigInteger b)
    {
        if (a < b)
        {
            (a, b) = (b, a);
        }

        while (!b.IsZero)
        {
            var bitsA = GetBitLength(a);
            var bitsB = GetBitLength(b);

            if (bitsA - bitsB > 64 || bitsA <= 64)
            {
                var r = a % b;
                a = b;
                b = r;
                continue;
            }

            var shift = bitsA - 64;
            if (shift < 0)
            {
                shift = 0;
            }

            var x = (ulong)(a >> shift);
            var y = (ulong)(b >> shift);

            var progress =
                GpuBigIntGcdKernels.ComputeLehmerMatrix(x, y, out long u0,
                    out long v0, out long u1, out long v1);

            if (!progress)
            {
                var r = a % b;
                a = b;
                b = r;
            }
            else
            {
                var new_a = a * u0 + b * v0;
                var new_b = a * u1 + b * v1;

                // Safety fallback
                if (new_a < 0 || new_b < 0 || new_b > new_a)
                {
                    var r = a % b;
                    a = b;
                    b = r;
                }
                else
                {
                    a = new_a;
                    b = new_b;
                }
            }
        }

        return a;
    }

    private static int GetBitLength(BigInteger b)
    {
        if (b.IsZero)
        {
            return 0;
        }

        if (b.Sign < 0)
        {
            b = -b;
        }

        var bytes = b.ToByteArray(isUnsigned: true, isBigEndian: true);
        var bitLength = (bytes.Length - 1) * 8;
        var first = bytes[0];
        while (first > 0)
        {
            bitLength++;
            first >>= 1;
        }

        return bitLength;
    }
}

