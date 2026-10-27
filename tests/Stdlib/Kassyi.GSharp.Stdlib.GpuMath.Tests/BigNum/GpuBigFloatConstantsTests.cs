#nullable enable
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Constants;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Tests of the host-side ln 2 constant (<see cref="GpuBigFloatConstants"/>) against the independent interval oracle.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Stdlib)]
public class GpuBigFloatConstantsTests
{
    // ln 2 = 0.69314..., the first 100 digits after the point (OEIS A002162).
    private const string Ln2Digits =
        "6931471805599453094172321214581765680755001343602552541206800094933936219696947156058633269964186875";

    // L = floor(ln 2 · 2^F) exactly: e^(L / 2^F) <= 2 < e^((L + 1) / 2^F), checked with the Exp oracle (e^t is
    // increasing, and e^t = 2 has no rational solution, so both comparisons are strict).
    [Theory]
    [Scenario(TestScenario.Boundary)]
    // mutant: GpuBigFloatConstants.cs ComputeLn2Fixed `- 2 * AtanhFixed(4801, ...)` -> `+ 2 * AtanhFixed(4801, ...)`
    // mutant: GpuBigFloatConstants.cs SplitAtanh leaf `2 * first + 1` -> `2 * first + 3`
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(64)]
    [InlineData(100)]
    [InlineData(333)]
    [InlineData(1024)]
    [InlineData(4096)]
    public void GetLn2Fixed_IsFloorOfLn2(int fractionBits)
    {
        var ln2 = GpuBigFloatConstants.GetLn2Fixed(fractionBits);

        var two = new Dyadic(2, 0);
        Assert.True(BigFloatIntervalOracle.CompareExp(ln2, fractionBits, two, fractionBits + 64) < 0,
            $"e^(L / 2^{fractionBits}) > 2: L is too large");
        Assert.True(BigFloatIntervalOracle.CompareExp(ln2 + 1, fractionBits, two, fractionBits + 64) > 0,
            $"e^((L + 1) / 2^{fractionBits}) < 2: L is too small");
    }

    // The uncached computation is exactly floor(ln 2 · 2^F) for every F up to 320. The cached entry point computes
    // only at the precisions that grow the cache, so the rounding check of the computation is tested here: with 8
    // guard bits, about one F in ten has an error interval that straddles an integer of the result.
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ComputeLn2Fixed_IsFloorOfLn2_ForEveryPrecisionUpTo320()
    {
        var two = new Dyadic(2, 0);
        for (var fractionBits = 0; fractionBits <= 320; fractionBits++)
        {
            var ln2 = GpuBigFloatConstants.ComputeLn2Fixed(fractionBits);

            // mutant: GpuBigFloatConstants.cs ComputeLn2Fixed `var high = (sum + 52) >> shift;` ->
            // `var high = sum >> shift;` (accepts the lower integer when ln 2 lies just above it)
            Assert.True(BigFloatIntervalOracle.CompareExp(ln2, fractionBits, two, fractionBits + 64) < 0,
                $"F = {fractionBits}: e^(L / 2^F) > 2, L is too large");
            Assert.True(BigFloatIntervalOracle.CompareExp(ln2 + 1, fractionBits, two, fractionBits + 64) > 0,
                $"F = {fractionBits}: e^((L + 1) / 2^F) < 2, L is too small");
        }
    }

    // The value agrees with the 100 known decimal digits of ln 2 (OEIS A002162).
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetLn2Fixed_MatchesKnownDigits()
    {
        const int FractionBits = 400;

        var ln2 = GpuBigFloatConstants.GetLn2Fixed(FractionBits);

        // floor(L / 2^400 · 10^100) is the 100-digit truncation, since L / 2^400 is within 2^-400 of ln 2.
        var digits = ln2 * BigInteger.Pow(10, 100) >> FractionBits;
        Assert.Equal(BigInteger.Parse(Ln2Digits), digits);
    }

    // Small, large and small requests agree: a smaller request is the cached value shifted right, and the larger one
    // recomputes the cache.
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetLn2Fixed_CacheGrowth_KeepsValues()
    {
        var small = GpuBigFloatConstants.GetLn2Fixed(64);
        var large = GpuBigFloatConstants.GetLn2Fixed(4160);
        var smallAgain = GpuBigFloatConstants.GetLn2Fixed(64);

        // mutant: GpuBigFloatConstants.cs GetLn2Fixed `s_ln2Fixed >> (s_ln2FractionBits - fractionBits)` ->
        // `s_ln2Fixed >> (s_ln2FractionBits - fractionBits - 1)`
        Assert.Equal(small, smallAgain);
        Assert.Equal(small, large >> (4160 - 64));
    }

    // A negative number of fraction bits is rejected.
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void GetLn2Fixed_NegativeBits_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GpuBigFloatConstants.GetLn2Fixed(-1));
    }

    // N terms leave a remainder of at most 2^-G: (q^2 - 1) q^(2N - 1) >= 2^G, and N is the smallest such count.
    [Theory]
    [Scenario(TestScenario.Boundary)]
    // mutant: GpuBigFloatConstants.cs AtanhTermCount `return terms;` -> `return terms - 1;` (one term too few)
    // mutant: GpuBigFloatConstants.cs CoversRemainder `2 * terms - 1` -> `2 * terms + 1`
    [InlineData(26, 1)]
    [InlineData(26, 8)]
    [InlineData(26, 40)]
    [InlineData(26, 4104)]
    [InlineData(4801, 1)]
    [InlineData(4801, 72)]
    [InlineData(4801, 4104)]
    [InlineData(8749, 40)]
    [InlineData(8749, 4104)]
    public void AtanhTermCount_IsSmallestCountCoveringTheRemainder(int q, int fractionBits)
    {
        var terms = GpuBigFloatConstants.AtanhTermCount(q, fractionBits);

        var limit = BigInteger.One << fractionBits;
        var factor = (BigInteger)q * q - 1;
        Assert.True(factor * BigInteger.Pow(q, 2 * terms - 1) >= limit, $"{terms} terms leave more than 2^-{fractionBits}");
        Assert.True(terms == 1 || factor * BigInteger.Pow(q, 2 * terms - 3) < limit, $"{terms - 1} terms already suffice");
    }

    // The binary splitting gives floor(S_N · 2^F) of the exact partial sum, summed here term by term as a rational.
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    // mutant: GpuBigFloatConstants.cs SplitAtanh `leftT * rightB * rightQ + leftB * rightT` ->
    // `leftT * rightB * rightQ + leftB * rightT * rightQ`
    [InlineData(26, 1)]
    [InlineData(26, 2)]
    [InlineData(26, 3)]
    [InlineData(26, 12)]
    [InlineData(4801, 5)]
    [InlineData(8749, 7)]
    public void AtanhReciprocalFixed_IsFloorOfPartialSum(int q, int terms)
    {
        const int FractionBits = 300;

        var actual = GpuBigFloatConstants.AtanhReciprocalFixed(q, terms, FractionBits);

        BigInteger num = 0;
        BigInteger den = 1;
        for (var k = 0; k < terms; k++)
        {
            var termDen = (2 * k + 1) * BigInteger.Pow(q, 2 * k + 1);
            num = num * termDen + den;
            den *= termDen;
        }

        Assert.Equal((num << FractionBits) / den, actual);
    }
}
