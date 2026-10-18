namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>Computes high-precision Pi digits using the Chudnovsky binary splitting algorithm on CPU.</summary>
public static partial class ChudnovskyAlgorithm
{
    /// <summary>Represents the intermediate terms P, Q, and T in the binary splitting algorithm.</summary>
    public struct ChudnovskyPqt
    {
        /// <summary>The P term.</summary>
        public BigInteger P;
        /// <summary>The Q term.</summary>
        public BigInteger Q;
        /// <summary>The T term.</summary>
        public BigInteger T;
    }

    /// <summary>Computes Pi up to the specified number of digits.</summary>
    /// <param name="digits">The number of digits to compute.</param>
    /// <returns>A string representation of Pi.</returns>
    public static string ComputePi(int digits)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(digits);

        // 14 digits per term approx
        var n = (int)Math.Ceiling(digits / DigitsPerTerm) + 1;

        var sw = Stopwatch.StartNew();
        var pqt = ComputeSubtree(0, n);

        BigInteger chudnovskyConstant = 426880;

        var factorQ = pqt.Q;
        var factorT = pqt.T;

        // Add extra digits for precision during division
        var totalDigits = digits + ExtraDigits;

        // Calculate Sqrt(10005 * 10^(2*totalDigits))
        var sqrtBase = 10005 * BigInteger.Pow(10, 2 * totalDigits);
        var sqrt10005 = sqrtBase.Sqrt();

        var piExt = (chudnovskyConstant * sqrt10005 * factorQ) / factorT;
        var pi = piExt / BigInteger.Pow(10, ExtraDigits);
        sw.Stop();

        Logger.Info(nameof(ChudnovskyAlgorithm), $"Computed {digits} digits in {sw.ElapsedMilliseconds} ms");
        return pi.ToString();
    }

    /// <summary>Computes the subtree of the binary splitting algorithm for the range [a, b).</summary>
    /// <param name="a">The start index of the range.</param>
    /// <param name="b">The end index of the range.</param>
    /// <returns>The computed P, Q, and T values for the subtree.</returns>
    public static ChudnovskyPqt ComputeSubtree(int a, int b)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(a);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(b, a);

        if (a + 1 == b)
        {
            if (a == 0)
            {
                return new ChudnovskyPqt
                {
                    P = 1,
                    Q = 1,
                    T = 13591409
                };
            }

            var numeratorP = -(BigInteger)(6L * a - 5) * (2L * a - 1) * (6L * a - 1);
            var denominatorQ = (BigInteger)10939058860032000L * a * a * a;
            var sumT = numeratorP * (13591409 + (BigInteger)545140134L * a);
            return new ChudnovskyPqt
            {
                P = numeratorP,
                Q = denominatorQ,
                T = sumT
            };
        }

        var m = (a + b) / 2;
        var l = ComputeSubtree(a, m);
        var r = ComputeSubtree(m, b);

        return new ChudnovskyPqt
        {
            P = l.P * r.P,
            Q = l.Q * r.Q,
            T = l.T * r.Q + l.P * r.T
        };
    }
}
