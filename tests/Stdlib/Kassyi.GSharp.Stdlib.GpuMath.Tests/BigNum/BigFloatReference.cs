namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Independent <see cref="BigInteger"/> reference for big-float values <c>sign * sig * 2^exp</c>.
/// </summary>
/// <remarks>
/// Values are compared as exact rationals (numerator / denominator), never through <see cref="double"/>.
/// The transcendental references use fixed-point arithmetic with <see cref="FractionBits"/> fraction bits,
/// far more than the 128 bits that the tests check, so their own truncation error does not matter.
/// </remarks>
internal static class BigFloatReference
{
    /// <summary>Fraction bits of the fixed-point references.</summary>
    public const int FractionBits = 400;

    /// <summary>
    /// Returns whether <c>sign * sig * 2^exp</c> equals <c>num / den</c> exactly.
    /// </summary>
    public static bool EqualsRational(BigInteger sig, int exp, bool isNegative, BigInteger num, BigInteger den)
    {
        var signedSig = isNegative ? -sig : sig;
        // sig * 2^exp * den == num  <=>  sig * den * 2^max(exp, 0) == num * 2^max(-exp, 0)
        var left = signedSig * den << Math.Max(exp, 0);
        var right = num << Math.Max(-exp, 0);
        return left == right;
    }

    /// <summary>
    /// Formats <c>sign * sig * 2^exp</c> for an assertion message.
    /// </summary>
    public static string Describe(BigInteger sig, int exp, bool isNegative) =>
        $"{(isNegative ? "-" : "+")}0x{sig:X} * 2^{exp}";

    /// <summary>
    /// Returns <c>floor(sig * 2^(exp + FractionBits))</c>.
    /// </summary>
    public static BigInteger ToFixed(BigInteger sig, int exp)
    {
        var shift = exp + FractionBits;
        return shift >= 0 ? sig << shift : sig >> -shift;
    }

    /// <summary>
    /// Returns <c>floor(sqrt(n))</c> (Newton iteration on integers).
    /// </summary>
    public static BigInteger IntegerSqrt(BigInteger n)
    {
        if (n.IsZero)
        {
            return 0;
        }

        var x = BigInteger.One << (int)((n.GetBitLength() + 1) / 2);
        while (true)
        {
            var next = (x + n / x) >> 1;
            if (next >= x)
            {
                return x;
            }

            x = next;
        }
    }

    /// <summary>
    /// Returns <c>sqrt(v)</c> for a positive integer <paramref name="v"/> in fixed point.
    /// </summary>
    public static BigInteger SqrtFixed(uint v) => IntegerSqrt(new BigInteger(v) << (2 * FractionBits));

    /// <summary>
    /// Returns <c>1 / sqrt(v)</c> for a positive integer <paramref name="v"/> in fixed point.
    /// </summary>
    public static BigInteger InverseSqrtFixed(uint v) => (BigInteger.One << (2 * FractionBits)) / SqrtFixed(v);

    /// <summary>
    /// Returns <c>e^x</c> for an integer <paramref name="x"/> in fixed point (Taylor series of e^|x|; e^-|x| = 1 / e^|x|).
    /// </summary>
    public static BigInteger ExpFixed(int x)
    {
        if (x < 0)
        {
            return (BigInteger.One << (2 * FractionBits)) / ExpFixed(-x);
        }

        var one = BigInteger.One << FractionBits;
        var term = one;
        var sum = one;
        for (var k = 1; !term.IsZero; k++)
        {
            term = term * x / k;
            sum += term;
        }

        return sum;
    }

    /// <summary>
    /// Returns <c>ln 2 = sum_{k>=1} 1 / (k * 2^k)</c> in fixed point.
    /// </summary>
    public static BigInteger Ln2Fixed()
    {
        var one = BigInteger.One << FractionBits;
        BigInteger sum = 0;
        for (var k = 1; k <= FractionBits + 8; k++)
        {
            sum += one / (k * (BigInteger.One << k));
        }

        return sum;
    }

    /// <summary>
    /// Returns whether <paramref name="actual"/> is within a relative error of <c>2^-bits</c> of <paramref name="expected"/>
    /// (both in the same fixed point).
    /// </summary>
    public static bool IsWithinRelative(BigInteger actual, BigInteger expected, int bits) =>
        BigInteger.Abs(actual - expected) << bits <= BigInteger.Abs(expected);
}
