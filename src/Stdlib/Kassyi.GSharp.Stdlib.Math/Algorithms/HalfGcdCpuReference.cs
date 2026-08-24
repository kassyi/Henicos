using System.Numerics;

namespace Kassyi.GSharp.Stdlib.Math.Algorithms;

public static class HalfGcdCpuReference
{
    public static BigInteger Gcd(BigInteger a, BigInteger b)
    {
        if (a < b)
        {
            (a, b) = (b, a);
        }

        while (!b.IsZero)
        {
            int bitsA = GetBitLength(a);
            int bitsB = GetBitLength(b);

            if (bitsA - bitsB > 64 || bitsA <= 64)
            {
                var r = a % b;
                a = b;
                b = r;
                continue;
            }

            int shift = bitsA - 64;
            if (shift < 0)
            {
                shift = 0;
            }

            ulong x = (ulong)(a >> shift);
            ulong y = (ulong)(b >> shift);

            long u0 = 1, v0 = 0;
            long u1 = 0, v1 = 1;

            bool progress = false;

            while (y != 0)
            {
                // To guarantee the quotient is exactly the same as full precision,
                // we must check if (x)/(y+1) == (x+1)/(y).
                // Actually, since x and y are truncated, the true values A and B satisfy:
                // x * 2^s <= A < (x+1) * 2^s
                // y * 2^s <= B < (y+1) * 2^s
                // Max quotient is (x+1) / y (if y > 0)
                // Min quotient is x / (y+1)

                if (y + 1 == 0)
                {
                    break; // overflow protection
                }

                ulong qMin = x / (y + 1);
                ulong qMax = (x + 1) / y;

                if (qMin != qMax)
                {
                    break;
                }

                ulong q = qMin;
                if (q == 0)
                {
                    break;
                }

                ulong r = x - q * y;

                long u2 = u0 - (long)q * u1;
                long v2 = v0 - (long)q * v1;

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
        int bitLength = (bytes.Length - 1) * 8;
        byte first = bytes[0];
        while (first > 0)
        {
            bitLength++;
            first >>= 1;
        }

        return bitLength;
    }
}
