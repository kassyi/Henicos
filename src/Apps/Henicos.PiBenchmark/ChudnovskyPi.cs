using System.Numerics;

namespace Henicos.PiBenchmark;

/// <summary>Computes high-precision Pi digits using the Chudnovsky binary splitting algorithm on CPU.</summary>
public class ChudnovskyPi
{
    private struct ChudnovskyResult
    {
        public BigInteger PolynomialP, PolynomialQ, PolynomialT;
    }

    public static string ComputePi(int digits)
    {
        // 14 digits per term approx
        var n = digits / 14 + 1;

        var sw = Stopwatch.StartNew();
        var pqt = Compute(0, n);

        BigInteger chudnovskyConstant = 426880;

        var factorQ = pqt.PolynomialQ;
        var factorT = pqt.PolynomialT;

        // Add 20 extra digits for precision during division
        var extraDigits = 20;
        var totalDigits = digits + extraDigits;

        // Calculate Sqrt(10005 * 10^(2*totalDigits))
        var sqrtBase = 10005 * BigInteger.Pow(10, 2 * totalDigits);
        var sqrt10005 = Sqrt(sqrtBase);

        var piExt = (chudnovskyConstant * sqrt10005 * factorQ) / factorT;
        var pi = piExt / BigInteger.Pow(10, extraDigits);
        sw.Stop();

        Console.WriteLine($"Computed {digits} digits in {sw.ElapsedMilliseconds} ms");
        return pi.ToString();
    }

    private static ChudnovskyResult Compute(int a, int b)
    {
        if (a + 1 == b)
        {
            if (a == 0)
            {
                return new ChudnovskyResult
                {
                    PolynomialP = 1,
                    PolynomialQ = 1,
                    PolynomialT = 13591409
                };
            }

            BigInteger numeratorP = -(BigInteger)(6L * a - 5) * (2L * a - 1) * (6L * a - 1);
            BigInteger denominatorQ = (BigInteger)10939058860032000L * a * a * a;
            var sumT = numeratorP * (13591409 + (BigInteger)545140134L * a);
            return new ChudnovskyResult
            {
                PolynomialP = numeratorP,
                PolynomialQ = denominatorQ,
                PolynomialT = sumT
            };
        }

        var m = (a + b) / 2;
        var l = Compute(a, m);
        var r = Compute(m, b);

        return new ChudnovskyResult
        {
            PolynomialP = l.PolynomialP * r.PolynomialP,
            PolynomialQ = l.PolynomialQ * r.PolynomialQ,
            PolynomialT = l.PolynomialT * r.PolynomialQ + l.PolynomialP * r.PolynomialT
        };
    }

    private static BigInteger Sqrt(BigInteger n)
    {
        if (n == 0)
        {
            return 0;
        }

        if (n > 0)
        {
            var bitLength = (int)Math.Ceiling(BigInteger.Log(n, 2));
            var root = BigInteger.One << (bitLength / 2);

            while (true)
            {
                var next = (root + n / root) >> 1;
                if (next == root || next == root + 1)
                {
                    if (root * root > n)
                    {
                        return root - 1;
                    }

                    return root;
                }

                root = next;
            }
        }

        throw new ArithmeticException("NaN");
    }
}
