#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Tests of the host implementation of the big-float Log / Exp / Pow (<see cref="BigFloatTranscendentalMath"/>) that
/// need no GPU. The GPU and CPU providers are compared in <see cref="GpuBigFloatTranscendentalTests"/> and
/// <see cref="GpuBigFloatPowTests"/>.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Stdlib)]
public class BigFloatTranscendentalMathTests
{
    // The ZivRetry inputs lie next to a p-bit boundary, closer than the error bound of the first approximation: the
    // rounding check rejects it, retries with more guard words, and the result is RZ_p of the exact value (oracle).
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ZivRetryInputs.Names), MemberType = typeof(ZivRetryInputs))]
    // mutant: BigFloatTranscendentalMath.cs TryTruncate `ErrorBound <= guard && guard + ErrorBound < limit` ->
    // `ErrorBound <= guard` (upper half dropped): LogBelow, LogAbove, ExpBelow, ExpAbove (for Above the accepted
    // truncation is one ulp low)
    // mutant: the same line -> `guard + ErrorBound < limit` (lower half dropped): LogNegativeBelow, LogNegativeAbove,
    // LogDistanceAbove, ExpDistanceAbove (accepted at the first attempt whatever the sign of its error)
    public void ZivRetry_RetriesAndIsRz(string caseName)
    {
        var (kind, sig, exp, negative) = ZivRetryInputs.Cases[caseName];
        var options = new GpuBigFloatTranscendentalOptions();

        int attempts;
        if (kind == "Log")
        {
            var (s, e, n) = BigFloatTranscendentalMath.Log(sig, exp, 128, options, out attempts);
            Assert.True(BigFloatIntervalOracle.IsRzOfLog(sig, exp, s, e, n, 128), $"{caseName}: ({s:X}, {e}, {n}) is not RZ(ln x)");
        }
        else
        {
            var (s, e) = BigFloatTranscendentalMath.Exp(sig, exp, negative, 128, options, out attempts);
            var (num, denShift) = BigFloatIntervalOracle.ToRatio(sig, exp);
            Assert.Equal(BigFloatIntervalOracle.ExpRz(negative ? -num : num, denShift, 128), (s, (long)e));
        }

        Assert.True(attempts >= 2, $"{caseName}: the first approximation was accepted");
    }

    // The Pow inputs of PowRetryInputs: the check retries, and the result is RZ_p(x^y) (oracle).
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(PowRetryInputs.Names), MemberType = typeof(PowRetryInputs))]
    // mutant: BigFloatTranscendentalMath.cs PowApproximation `exp with { ErrorBound = exp.ErrorBound + argumentBound }`
    // -> `exp with { ErrorBound = 0 }` (no bound: the first approximation is accepted): every case
    public void Pow_ZivRetry_RetriesAndIsRz(string caseName)
    {
        var input = PowRetryInputs.Cases[caseName];
        var p = 32 * input.Words;

        var (sig, exp) = BigFloatTranscendentalMath.Pow(
            input.XSignificand, input.XExponent, input.YSignificand, input.YExponent, input.YNegative, p,
            new GpuBigFloatTranscendentalOptions(), out var attempts);

        var expected = BigFloatIntervalOracle.PowRz(
            input.XSignificand, input.XExponent, input.YSignificand, input.YExponent, input.YNegative, p);
        Assert.Equal(expected, (sig, (long)exp));
        Assert.True(attempts >= 2, $"{caseName}: the first approximation was accepted");
    }

    // The exact check, one input (or more) per branch: x = 1 and y = 0 (any sign), an integer y with x = 2^b or an odd
    // part up to exactly p bits, a negative integer y, and y = n / 2^k with the root of a power of two or of a perfect
    // square or fourth power. "-" marks a power that is not a p-bit value; the expected exact value is odd · 2^twos.
    [Theory]
    [Scenario(TestScenario.Boundary)]
    // mutant: BigFloatTranscendentalMath.cs TryExactPow `if (ySignificand.IsZero || (xOdd.IsOne && xTwos == 0))` ->
    // `if (ySignificand.IsZero)` (x = 1 left to the checks): 1^(2^-100) (k = 100 has no root check; x = 1 would reach
    // the approximation, where ln 1 = 0)
    // mutant: the same line -> `if (xOdd.IsOne && xTwos == 0)` (y = 0 left to the checks): 3^-0, read as a negative
    // integer y with an odd part 3
    // mutant: BigFloatTranscendentalMath.cs TryExactPow `twos = (BigInteger)xTwos * y` -> `(BigInteger)xTwos` (integer
    // y, odd a): 0.75^2; `twos = (BigInteger)xTwos * (yOdd << (int)yTwos)` -> `(BigInteger)xTwos` (x = 2^b): 2^1000,
    // 0.5^3
    // mutant: BigFloatTranscendentalMath.cs TryExactPow `odd.GetBitLength() > resultBits` -> `>= resultBits` after
    // `BigInteger.Pow(xOdd, y)`: 3^20 at p = 32; after `BigInteger.Pow(rootOdd, (int)yOdd)`: (3^80)^0.5 at p = 64
    // mutant: BigFloatTranscendentalMath.cs TryExactPow `if (yNegative || yTop > 31 || ...)` -> `if (yTop > 31 || ...)`
    // (a negative integer y with a > 1): 3^-1, 6^-1
    // mutant: BigFloatTranscendentalMath.cs TryExactPow root of 2^b `if (xTwos % (1L << (int)k) != 0)` -> `if (false)`:
    // 2^0.5
    // mutant: BigFloatTranscendentalMath.cs TryExactPow `if (k > 62)` -> `if (false)`: 2^(2^-64) (1L << 64 is 1 in C#, so
    // every b would pass)
    // mutant: BigFloatTranscendentalMath.cs TryExactPow odd b `if ((rootTwos & 1) != 0)` -> `if (false)`: 18^0.5
    // mutant: BigFloatTranscendentalMath.cs TryExactPow perfect square `if (root * root != rootOdd)` -> `if (false)`:
    // 12^0.5
    // mutant: BigFloatTranscendentalMath.cs TryExactPow `for (var i = 0; i < k; i++)` -> `i < k - 1`: 81^0.125 (claims
    // 3), 81^0.75, 36^0.5, 64^±0.5
    // mutant: BigFloatTranscendentalMath.cs TryExactPow `else if (yNegative || yOdd.GetBitLength() > 31 || ...)` without
    // `yNegative`: 9^-0.5
    // mutant: BigFloatTranscendentalMath.cs TryExactPow `(1L << (int)k) > (long)xOdd.GetBitLength()` ->
    // `(1L << (int)(k + 1)) > ...` (rejects a fourth power of 7 bits): 81^0.75
    // For k <= 62 the length check alone rejects early, so an odd part of more than 2^30 bits still reaches the square
    // roots (the 256 MB operand of such a test is not built here). Not killed: the early rejection removed (the square
    // roots reject the same inputs). The early rejection `(bits(a) - 1)·y >= resultBits` (integer y) is a guard of the
    // run time: without it 3^(2^30), a power of 1.7·10^9 bits, is computed (the run of 3^(2^30) hangs instead of
    // failing).
    [InlineData("1", 0, "1", -100, false, 128, "1", 0)]
    [InlineData("1099511627776", -40, "3", 0, false, 128, "1", 0)]
    [InlineData("3", 0, "0", 0, false, 128, "1", 0)]
    [InlineData("3", 0, "0", 0, true, 128, "1", 0)]
    [InlineData("2", 0, "1000", 0, false, 128, "1", 1000)]
    [InlineData("1", -1, "3", 0, false, 128, "1", -3)]
    [InlineData("3", 0, "20", 0, false, 32, "3^20", 0)]
    [InlineData("3", 0, "21", 0, false, 32, "-", 0)]
    [InlineData("3", -2, "2", 0, false, 128, "9", -4)]
    [InlineData("3", 0, "1", 40, false, 128, "-", 0)]
    [InlineData("3", 0, "1", 30, false, 128, "-", 0)]
    [InlineData("2", 0, "3", 0, true, 128, "1", -3)]
    [InlineData("3", 0, "1", 0, true, 128, "-", 0)]
    [InlineData("6", 0, "1", 0, true, 128, "-", 0)]
    [InlineData("64", 0, "1", -1, false, 128, "1", 3)]
    [InlineData("64", 0, "1", -1, true, 128, "1", -3)]
    [InlineData("1", -6, "3", -1, false, 128, "1", -9)]
    [InlineData("2", 0, "1", -1, false, 128, "-", 0)]
    [InlineData("2", 0, "1", -64, false, 128, "-", 0)]
    [InlineData("36", 0, "1", -1, false, 128, "3", 1)]
    [InlineData("18", 0, "1", -1, false, 128, "-", 0)]
    [InlineData("12", 0, "1", -1, false, 128, "-", 0)]
    [InlineData("81", 0, "3", -2, false, 128, "27", 0)]
    [InlineData("81", 0, "1", -3, false, 128, "-", 0)]
    [InlineData("9", 0, "1", -1, true, 128, "-", 0)]
    [InlineData("3^80", 0, "1", -1, false, 64, "3^40", 0)]
    [InlineData("3^82", 0, "1", -1, false, 64, "-", 0)]
    [InlineData("3", 0, "1", -40, false, 128, "-", 0)]
    public void TryExactPow_DecidesEachBranch(
        string xSignificand, int xExponent, string ySignificand, int yExponent, bool yNegative, int p,
        string expectedOdd, int expectedTwos)
    {
        var exact = BigFloatTranscendentalMath.TryExactPow(
            Parse(xSignificand), xExponent, Parse(ySignificand), yExponent, yNegative, p, out var significand,
            out var exponent);

        if (expectedOdd == "-")
        {
            Assert.False(exact, "the power was taken for a p-bit value");
            return;
        }

        Assert.True(exact, "the exact power was not found");
        var expected = BigFloatIntervalOracle.RoundToBits(new Dyadic(Parse(expectedOdd), expectedTwos), p);
        Assert.Equal(expected, new Dyadic(significand, exponent));
    }

    // An exact power whose exponent does not fit int throws, named Pow, as an overflow above 1 and an underflow below.
    [Theory]
    [Scenario(TestScenario.SadPath)]
    // mutant: BigFloatTranscendentalMath.cs TryExactPow `negative: resultExponent < 0` -> `resultExponent > 0`: every
    // throwing case
    [InlineData("2", 0, "2147483775", false, "Pow overflow")]
    [InlineData("2", 0, "2147483522", true, "Pow underflow")]
    [InlineData("1", -1, "2147483775", false, "Pow underflow")]
    [InlineData("2", 0, "1", true, null)]
    public void TryExactPow_ExponentOutOfRange_Throws(string xSignificand, int xExponent, string ySignificand, bool yNegative, string? words)
    {
        if (words is null)
        {
            // Control: 2^-1 is in range.
            Assert.True(BigFloatTranscendentalMath.TryExactPow(
                Parse(xSignificand), xExponent, Parse(ySignificand), 0, yNegative, 128, out _, out _));
            return;
        }

        var error = Assert.Throws<OverflowException>(() => BigFloatTranscendentalMath.TryExactPow(
            Parse(xSignificand), xExponent, Parse(ySignificand), 0, yNegative, 128, out _, out _));
        Assert.StartsWith(words, error.Message, StringComparison.Ordinal);
    }

    // The argument error D covers what the bound of L carries into y·L: with L to 64 bits only (a far shorter Log than
    // Pow plans), the Exp bound alone no longer covers x^y, the Exp bound plus D does.
    [Theory]
    [Scenario(TestScenario.Boundary)]
    // mutant: TranscendentalPlanner.cs PowArgumentErrorBound `ySignificand * logErrorBound` -> `logErrorBound`:
    // y = 2^20 + 1, y = 5·2^10
    // mutant: TranscendentalPlanner.cs PowArgumentErrorBound `yExponent + logUnitExponent` -> `-yExponent + logUnitExponent`:
    // y = 5·2^10
    // mutant: TranscendentalPlanner.cs PowArgumentErrorBound `+ expWorkingBits + 2` -> `+ 2` (D in units of 2^-W too
    // large): every case; `return scaled << checked((int)shift)` -> `return scaled`: every case
    // Not killed here: a D smaller by a small factor (`+ expWorkingBits + 2` -> `+ expWorkingBits`); D exceeds the actual
    // error by a factor of 4 to 56 (the bound of e^(y·L) against the power of two above it, B_L against the actual Log
    // error). TranscendentalPlannerTests.PowArgumentErrorBound_IsTheCeiling kills it.
    [InlineData("3", 0, "1048577", 0, false)]
    [InlineData("3", 0, "5", 10, false)]
    [InlineData("3", 0, "3", -10, true)]
    [InlineData("1", -1000, "12345", -14, false)]
    public void PowArgumentErrorBound_CoversTheErrorOfAShortLog(
        string xSignificand, int xExponent, string ySignificand, int yExponent, bool yNegative)
    {
        var x = Parse(xSignificand);
        var y = Parse(ySignificand);
        var options = new GpuBigFloatTranscendentalOptions();
        var log = BigFloatTranscendentalMath.LogApproximation(x, xExponent, 64, options);
        var argument = y * log.Significand;
        var argumentExponent = checked((int)(yExponent + log.Exponent));
        var exp = BigFloatTranscendentalMath.ExpApproximation(argument, argumentExponent, yNegative != log.IsNegative, 128, options);

        var argumentBound = TranscendentalPlanner.PowArgumentErrorBound(
            y, yExponent, log.ErrorBound, log.UnitExponent, exp.WorkingBits);

        Assert.False(
            BigFloatIntervalOracle.IsPowWithin(
                x, xExponent, y, yExponent, yNegative, exp.Significand, exp.Exponent, exp.ErrorBound, exp.UnitExponent, 256),
            "the short Log is within the Exp bound already; the test does not exercise D");
        Assert.True(
            BigFloatIntervalOracle.IsPowWithin(
                x, xExponent, y, yExponent, yNegative, exp.Significand, exp.Exponent, exp.ErrorBound + argumentBound,
                exp.UnitExponent, 256),
            $"x^y is more than B_E + D = {exp.ErrorBound} + {argumentBound} units from e^(y·L)");
    }

    // "a^n" or a decimal integer.
    private static BigInteger Parse(string value)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var power = value.IndexOf('^', StringComparison.Ordinal);
        return power < 0
            ? BigInteger.Parse(value, culture)
            : BigInteger.Pow(BigInteger.Parse(value[..power], culture), int.Parse(value[(power + 1)..], culture));
    }
}
