namespace Kassyi.GSharp.Stdlib.Math.Algorithms;

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

            long u0 = 1, v0 = 0;
            long u1 = 0, v1 = 1;

            var progress = false;

            while (y != 0)
            {
                // [WHY] Validates that the quotient computed from truncated single-word precision matches the full-precision quotient.
                // Truncated bounds satisfy: x * 2^s <= A < (x+1) * 2^s and y * 2^s <= B < (y+1) * 2^s.
                // If min quotient (x / (y + 1)) equals max quotient ((x + 1) / y), the single-word quotient q is mathematically exact.
                if (y + 1 == 0)
                {
                    break; // overflow protection
                }

                var qMin = x / (y + 1);
                var qMax = (x + 1) / y;

                if (qMin != qMax)
                {
                    break;
                }

                var q = qMin;
                if (q == 0)
                {
                    break;
                }

                var r = x - q * y;

                var u2 = u0 - (long)q * u1;
                var v2 = v0 - (long)q * v1;

                x = y;
                y = r;
                u0 = u1;
                u1 = u2;
                v0 = v1;
                v1 = v2;
                progress = true;
            }

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
