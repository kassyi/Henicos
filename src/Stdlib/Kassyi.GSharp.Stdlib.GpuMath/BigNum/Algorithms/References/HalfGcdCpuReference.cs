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
        a = BigInteger.Abs(a);
        b = BigInteger.Abs(b);

        if (a < b)
        {
            (a, b) = (b, a);
        }

        while (!b.IsZero)
        {
            var bitsA = (int)a.GetBitLength();
            var bitsB = (int)b.GetBitLength();

            if (bitsA - bitsB <= 64 && bitsA > 64)
            {
                var shift = bitsA - 64;
                var x = (ulong)(a >> shift);
                var y = (ulong)(b >> shift);

                if (GpuBigIntGcdKernels.ComputeLehmerMatrix(x, y, out var u0, out var v0, out var u1, out var v1))
                {
                    var newA = a * u0 + b * v0;
                    var newB = a * u1 + b * v1;

                    if (newA >= 0 && newB >= 0 && newB <= newA)
                    {
                        a = newA;
                        b = newB;
                        continue;
                    }
                }
            }

            (a, b) = (b, a % b);
        }

        return a;
    }
}

