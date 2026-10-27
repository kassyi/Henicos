#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Tests of the host-side parameter planner of the big-float Log / Exp (<see cref="TranscendentalPlanner"/>).
/// </summary>
/// <remarks>
/// The bounds come from the design (tmp/p3-lanes/628-log-exp-argument-reduction.md §4.3, §10.1): the atanh series
/// <c>T = Σ_{k&lt;N} z^k / (2k+1)</c> stops when its remainder <c>ε'_R = z^N / ((2N+1)(1 − z)u)</c> is at most one unit
/// <c>u = 2^-W</c>, where <c>z = y²</c> is bounded by <c>1/25</c> (<c>|y| ≤ 1/5</c> for <c>m ∈ [3/4, 3/2)</c>) and,
/// near 1, by <c>2^(-2c) / 3</c> (<c>|u| &lt; 2^-c</c> and <c>|y| ≤ |u| / 1.75</c>).
/// </remarks>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Stdlib)]
public class TranscendentalPlannerTests
{
    public static TheoryData<int> AllResultWords()
    {
        var data = new TheoryData<int>();
        for (var words = 1; words <= 256; words++)
        {
            data.Add(words);
        }

        return data;
    }

    // Far from 1 (c = 1, z <= 1/25): N is the smallest count with eps'_R <= 1, checked exactly with rationals.
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(AllResultWords))]
    // mutant: TranscendentalPlanner.cs AtanhSquareBound `new BigInteger(25)` -> `new BigInteger(49)` (too few terms
    // for |y| = 1/5; also fails LogWorstY in GpuBigFloatTranscendentalTests)
    // mutant: TranscendentalPlanner.cs CoversRemainder `BigInteger.Pow(zDen, terms - 1)` -> `BigInteger.Pow(zDen, terms)`
    // Not killed: `(2 * terms + 1)` -> `(2 * terms + 3)` does not change N for any W tested.
    public void PlanLog_FarFromOne_TermsAreSmallestWithRemainderBelowOneUnit(int resultWords)
    {
        var plan = TranscendentalPlanner.PlanLog(32 * resultWords, 1, 1);

        AssertSmallestTermCount(plan.Terms, plan.WorkingBits, 1, 25);
    }

    // Near 1 (|u| < 2^-c, c >= 2): z <= 2^(-2c) / 3 = 1 / (3 · 4^c).
    [Theory]
    [Scenario(TestScenario.Boundary)]
    // mutant: TranscendentalPlanner.cs AtanhSquareBound `3 * (BigInteger.One << (2 * closenessBits))` ->
    // `(BigInteger.One << (2 * closenessBits))` (the looser bound 4^-c takes more terms than needed)
    [InlineData(1, 2)]
    [InlineData(4, 2)]
    [InlineData(4, 3)]
    [InlineData(4, 10)]
    [InlineData(32, 10)]
    [InlineData(32, 100)]
    [InlineData(4, 1000)]
    public void PlanLog_NearOne_TermsAreSmallestWithRemainderBelowOneUnit(int resultWords, int closenessBits)
    {
        var plan = TranscendentalPlanner.PlanLog(32 * resultWords, 0, closenessBits);

        AssertSmallestTermCount(plan.Terms, plan.WorkingBits, 1, 3 * (BigInteger.One << (2 * closenessBits)));
    }

    // m = 1 (the argument is a power of two) needs no series.
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void PlanLog_PowerOfTwo_HasNoTerms()
    {
        var plan = TranscendentalPlanner.PlanLog(128, 5, int.MaxValue);

        Assert.Equal(0, plan.Terms);
    }

    // The term counts of the design table (§10.1, atanh N for W = p + 32).
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(1, 64, 13)]
    [InlineData(3, 128, 27)]
    [InlineData(7, 256, 54)]
    [InlineData(15, 512, 109)]
    [InlineData(31, 1024, 219)]
    public void PlanLog_MatchesDesignTable(int resultWords, int workingBits, int terms)
    {
        var plan = TranscendentalPlanner.PlanLog(32 * resultWords, 1, 1);

        Assert.Equal(workingBits, plan.WorkingBits);
        Assert.Equal(terms, plan.Terms);
    }

    // Horner from the top: F < 1.79 + eps'_R <= 2.79, B = ceil(F + 4.01) = 7 ulp of the W-bit approximation, and
    // one guard word: ceil((ceil(log2(B + 1)) + 16) / 32) = 1 (§4.3.1).
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    // mutant: TranscendentalPlanner.cs `LogHornerErrorBound = 7` -> `6`
    // mutant: TranscendentalPlanner.cs GuardWordsFor `+ 16` -> `+ 48` (two guard words)
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(129)]
    public void PlanLog_HornerBoundAndGuard(int resultWords)
    {
        var plan = TranscendentalPlanner.PlanLog(32 * resultWords, -3, 1);

        Assert.Equal(7, plan.ErrorBound);
        Assert.Equal(1, plan.GuardWords);
        Assert.Equal(32 * resultWords + 32, plan.WorkingBits);
    }

    // ln 2 for e·ln 2 carries W + ceil(log2 |e|) + 4 fraction bits, so that |e·L − e·ln 2| <= 2^-(W+4) (§4.3.1).
    [Theory]
    [Scenario(TestScenario.Boundary)]
    // mutant: TranscendentalPlanner.cs PlanLog `+ CeilingLog2(magnitude) + 4` -> `+ 4`
    [InlineData(1L, 0)]
    [InlineData(-1L, 0)]
    [InlineData(2L, 1)]
    [InlineData(3L, 2)]
    [InlineData(-1000L, 10)]
    [InlineData(1024L, 10)]
    [InlineData(1025L, 11)]
    [InlineData(4294967295L, 32)]
    public void PlanLog_Ln2FractionBits(long e, int ceilingLog2)
    {
        var plan = TranscendentalPlanner.PlanLog(128, e, 1);

        Assert.Equal(plan.WorkingBits + ceilingLog2 + 4, plan.Ln2FractionBits);
    }

    // eps'_R = z^N / ((2N + 1)(1 - z) 2^-W) <= 1 with z = zNum / zDen, that is
    // zNum^N · 2^W <= (2N + 1)(zDen - zNum) zDen^(N - 1); and it fails for N - 1.
    private static void AssertSmallestTermCount(int terms, int workingBits, BigInteger zNum, BigInteger zDen)
    {
        static bool Covers(int n, int w, BigInteger zn, BigInteger zd) =>
            BigInteger.Pow(zn, n) << w <= (2 * n + 1) * (zd - zn) * BigInteger.Pow(zd, n - 1);

        Assert.True(terms >= 1);
        Assert.True(Covers(terms, workingBits, zNum, zDen), $"{terms} terms leave a remainder above 2^-{workingBits}");
        Assert.True(terms == 1 || !Covers(terms - 1, workingBits, zNum, zDen), $"{terms - 1} terms already suffice");
    }
}
