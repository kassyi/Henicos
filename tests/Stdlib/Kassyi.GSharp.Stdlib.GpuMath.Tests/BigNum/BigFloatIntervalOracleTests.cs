#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Self-tests of <see cref="BigFloatIntervalOracle"/>, the independent reference of the big-float Exp / Log tests.
/// </summary>
/// <remarks>
/// The references here do not go through the oracle's argument reduction: exact rational Taylor sums with a remainder
/// bound, the digits of e (OEIS A001113) and <c>Math.Exp</c>.
/// </remarks>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Stdlib)]
public class BigFloatIntervalOracleTests
{
    // e = 2.71828..., the first 100 digits after the point (OEIS A001113).
    private const string EDigits =
        "7182818284590452353602874713526624977572470936999595749669676277240766303535475945713821785251664274";

    // ln 2 = 0.69314..., the first 100 digits after the point (OEIS A002162).
    private const string Ln2Digits =
        "6931471805599453094172321214581765680755001343602552541206800094933936219696947156058633269964186875";

    // The interval agrees with Math.Exp to its accuracy: Math.Exp is within a few ulp (2^-52 ~ 2.2e-16) of e^x,
    // so both bounds are within 1e-15 relative of it.
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(-20.5)]
    [InlineData(-1.0)]
    [InlineData(-9.313225746154785E-10)] // -2^-30
    [InlineData(9.094947017729282E-13)] // 2^-40
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.75)]
    [InlineData(10.0)]
    [InlineData(100.0)]
    [InlineData(700.0)]
    public void ExpInterval_AgreesWithMathExp(double x)
    {
        var (num, denShift) = ToRatio(x);

        var (lo, hi) = BigFloatIntervalOracle.ExpInterval(num, denShift, 200);

        var expected = Math.Exp(x);
        // mutant: BigFloatIntervalOracle.cs ExpIntervalOfPositive `var shift = denShift + halvings;` ->
        // `var shift = denShift + halvings + 1;` (the series sums e^(q/2^(j+1)))
        Assert.True(Math.Abs(ToDouble(lo) - expected) <= 1e-15 * expected, $"lo {ToDouble(lo):R} vs {expected:R}");
        Assert.True(Math.Abs(ToDouble(hi) - expected) <= 1e-15 * expected, $"hi {ToDouble(hi):R} vs {expected:R}");
        Assert.True(lo <= hi);
    }

    // The interval contains e^q: it contains a much narrower interval around e^q, built from the exact rational
    // Taylor sum and its remainder bound without argument reduction.
    [Theory]
    [Scenario(TestScenario.Boundary)]
    // mutant: BigFloatIntervalOracle.cs ExpIntervalOfPositive `hiTerm = CeilingDivide(hiTerm * num, divisor)` ->
    // `hiTerm = hiTerm * num / divisor` (the upper bound loses the rounding of every term)
    // mutant: BigFloatIntervalOracle.cs ExpInterval `ReciprocalUp(lo, bits)` -> `ReciprocalUp(hi, bits)` (e^-q)
    // Not killed: `hi = RoundUp(hi * hi, workBits)` -> `RoundDown` and `hiSum += 1` -> `hiSum += 0`; the ceiled
    // series terms leave more than the one unit that either change removes.
    [InlineData(1, 0)] // e
    [InlineData(-1, 0)] // 1/e
    [InlineData(3, 3)] // e^(3/8)
    [InlineData(5, 0)] // e^5: 11 squarings
    [InlineData(-5, 0)]
    [InlineData(1, 40)] // e^(2^-40): no squaring
    public void ExpInterval_ContainsExactSeries(int numerator, int denShift)
    {
        var (lo, hi) = BigFloatIntervalOracle.ExpInterval(numerator, denShift, 400);

        var (refLo, refHi) = ExactSeriesBounds(numerator, denShift, 1000);
        Assert.True(lo <= refLo, $"lo {lo} is above e^q (>= {refLo})");
        Assert.True(refHi <= hi, $"hi {hi} is below e^q (<= {refHi})");
    }

    // The interval for e lies inside the 100-digit decimal interval [D, D + 1] / 10^100 of OEIS A001113.
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ExpInterval_MatchesKnownDigitsOfE()
    {
        var (lo, hi) = BigFloatIntervalOracle.ExpInterval(1, 0, 400);

        var digits = BigInteger.Parse("2" + EDigits);
        var scale = BigInteger.Pow(10, 100);
        Assert.True(lo >= Ratio(digits, scale), $"lo {lo} is below the digits of e");
        Assert.True(hi <= Ratio(digits + 1, scale), $"hi {hi} is above the digits of e");
    }

    // e^a · e^b and e^(a+b) overlap (an identity, independent of any reference value).
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(3, -2, 1)]
    [InlineData(1000, 7, 0)]
    [InlineData(-123456789, 12, 20)]
    [InlineData(5, -5, 0)]
    public void ExpInterval_ProductOfExponentials(int left, int right, int denShift)
    {
        var (leftLo, leftHi) = BigFloatIntervalOracle.ExpInterval(left, denShift, 300);
        var (rightLo, rightHi) = BigFloatIntervalOracle.ExpInterval(right, denShift, 300);
        var (sumLo, sumHi) = BigFloatIntervalOracle.ExpInterval(left + (BigInteger)right, denShift, 300);

        Assert.True(leftLo * rightLo <= sumHi && sumLo <= leftHi * rightHi, "e^a e^b and e^(a+b) do not overlap");
    }

    // ExpRz gives RZ_64(e), checked against the rational reference.
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ExpRz_OfOne_IsTruncatedE()
    {
        var (sig, exp) = BigFloatIntervalOracle.ExpRz(1, 0, 64);

        var (refLo, refHi) = ExactSeriesBounds(1, 0, 1000);
        var expected = BigFloatIntervalOracle.RoundToBits(refLo, 64);
        Assert.Equal(expected, BigFloatIntervalOracle.RoundToBits(refHi, 64));
        Assert.Equal(expected, new Dyadic(sig, exp));
    }

    // exp(0) is exactly 1, RZ'd to p bits.
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ExpRz_OfZero_IsOne()
    {
        var (sig, exp) = BigFloatIntervalOracle.ExpRz(0, 0, 32);

        Assert.Equal(BigInteger.One << 31, sig);
        Assert.Equal(-31, exp);
    }

    // IsRzOfLog accepts RZ_p(ln 2) and RZ_p(ln 1/2) and rejects their neighbours; ln 2 here comes from the digits of
    // OEIS A002162, not from the oracle.
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(64)]
    [InlineData(200)]
    public void IsRzOfLog_AcceptsOnlyTruncatedLn2(int p)
    {
        var ln2 = Ln2Rz(p);
        var pred = BigFloatIntervalOracle.PredecessorTowardZero(ln2.Mantissa, ln2.Exponent, p);
        var succ = BigFloatIntervalOracle.SuccessorAwayFromZero(ln2.Mantissa, ln2.Exponent, p);

        // ln 2 > 0 (x = 2) and ln 1/2 < 0 (x = 2^-1).
        foreach (var (xExp, negative) in new[] { (1L, false), (-1L, true) })
        {
            // mutant: BigFloatIntervalOracle.cs IsRzOfLog `CompareExp(num, ...) < 0 && CompareExp(nextNum, ...) > 0` ->
            // `< 0 || ...` (the neighbours pass)
            Assert.True(BigFloatIntervalOracle.IsRzOfLog(1, xExp, ln2.Mantissa, ln2.Exponent, negative, p));
            Assert.False(BigFloatIntervalOracle.IsRzOfLog(1, xExp, pred.Significand, pred.Exponent, negative, p));
            Assert.False(BigFloatIntervalOracle.IsRzOfLog(1, xExp, succ.Significand, succ.Exponent, negative, p));
            Assert.False(BigFloatIntervalOracle.IsRzOfLog(1, xExp, ln2.Mantissa, ln2.Exponent, !negative, p));

            // A faithful result is RZ or the next value away from zero.
            Assert.True(BigFloatIntervalOracle.IsFaithfulLog(1, xExp, succ.Significand, succ.Exponent, negative, p));
            Assert.False(BigFloatIntervalOracle.IsFaithfulLog(1, xExp, pred.Significand, pred.Exponent, negative, p));
        }
    }

    // ln 1 = 0 is the only input with a zero result.
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void IsRzOfLog_ZeroOnlyForOne()
    {
        Assert.True(BigFloatIntervalOracle.IsRzOfLog(4, -2, 0, 0, false, 32));
        Assert.False(BigFloatIntervalOracle.IsRzOfLog(5, -2, 0, 0, false, 32));
        Assert.False(BigFloatIntervalOracle.IsRzOfLog(4, -2, BigInteger.One << 31, -31, false, 32));
    }

    // RZ_p(ln 2) from the 100 digits of OEIS A002162 (enough for p <= 300).
    private static Dyadic Ln2Rz(int p)
    {
        var digits = BigInteger.Parse(Ln2Digits);
        var scale = BigInteger.Pow(10, 100);

        // ln 2 lies in [D, D + 1] / 10^100; both ends truncate to the same p bits for the p used here.
        var low = BigFloatIntervalOracle.RoundToBits(Ratio(digits, scale), p);
        var high = BigFloatIntervalOracle.RoundToBits(Ratio(digits + 1, scale), p);
        Assert.Equal(low, high);
        return low;
    }

    // The dyadic floor(num / den · 2^400) · 2^-400 (exact enough for the comparisons above).
    private static Dyadic Ratio(BigInteger num, BigInteger den) => new((num << 400) / den, -400);

    // Bounds of e^q, q = numerator / 2^denShift, from the exact rational sum of 300 Taylor terms; the remainder is
    // below |q|^301 / 301! / (1 - |q| / 302) < 2^-1000 for |q| <= 5.
    private static (Dyadic Lo, Dyadic Hi) ExactSeriesBounds(BigInteger numerator, int denShift, int bits)
    {
        var den = BigInteger.One << denShift;
        BigInteger sumNum = 0;
        BigInteger sumDen = 1;
        BigInteger termNum = 1;
        BigInteger termDen = 1;
        for (var k = 0; k <= 300; k++)
        {
            if (k > 0)
            {
                termNum *= numerator;
                termDen *= den * k;
            }

            sumNum = sumNum * termDen + termNum * sumDen;
            sumDen *= termDen;
            var gcd = BigInteger.GreatestCommonDivisor(sumNum, sumDen);
            sumNum /= gcd;
            sumDen /= gcd;
        }

        var scaled = (sumNum << bits) / sumDen;
        return (new Dyadic(scaled - 2, -bits), new Dyadic(scaled + 2, -bits));
    }

    private static (BigInteger Num, int DenShift) ToRatio(double x)
    {
        var bits = BitConverter.DoubleToInt64Bits(x);
        var negative = bits < 0;
        var exponent = (int)((bits >> 52) & 0x7FF);
        var mantissa = bits & 0xFFFFFFFFFFFFFL;
        var significand = exponent == 0 ? mantissa : mantissa | (1L << 52);
        var power = (exponent == 0 ? 1 : exponent) - 1075;
        var (num, denShift) = BigFloatIntervalOracle.ToRatio(significand, power);
        return (negative ? -num : num, denShift);
    }

    private static double ToDouble(Dyadic value)
    {
        var excess = Math.Max(0, (int)value.Mantissa.GetBitLength() - 64);
        return Math.ScaleB((double)(value.Mantissa >> excess), (int)(value.Exponent + excess));
    }
}
