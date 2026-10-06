namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms.References;

/// <summary>
/// Provides a host (CPU) reference implementation of Lehmer's / Half-GCD algorithm for verification and unit testing of GPU kernels.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Truncates leading 64-bit words of large integers to compute Euclidean 2x2 transition matrices on CPU,
/// matching the exact matrix transformation logic executed across GPU threads in <c>GpuBigIntGcdKernels</c>.
/// </para>
/// <para>
/// <b>Execution Target:</b> Host-only (CPU) verification baseline.
/// </para>
/// </remarks>
internal static class HalfGcdCpuReference
{
    /// <summary>
    /// Computes the greatest common divisor (GCD) of two arbitrary-precision integers using Lehmer's 2x2 matrix reduction.
    /// </summary>
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
                GpuBigIntGcdKernels.ComputeLehmerMatrix(x, y, out var u0,
                    out var v0, out var u1, out var v1);

            if (!progress)
            {
                var r = a % b;
                a = b;
                b = r;
            }
            else
            {
                var newA = a * u0 + b * v0;
                var newB = a * u1 + b * v1;

                // Safety fallback
                if (newA < 0 || newB < 0 || newB > newA)
                {
                    var r = a % b;
                    a = b;
                    b = r;
                }
                else
                {
                    a = newA;
                    b = newB;
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

