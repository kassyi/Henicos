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

    // Exp (stage 3, term recurrence): N is the smallest count with eps_R = 2 rho^(N+1) / ((N+1)! u) <= 1 for
    // rho = 1.39 · 2^-s (r < 1.39 after the ln 2 reduction), checked exactly with rationals, and s >= 2.
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(AllResultWords))]
    // mutant: TranscendentalPlanner.cs ExpRemainderCovered `BigInteger.Pow(139, terms + 1)` ->
    // `BigInteger.Pow(139, terms)` (one term too few)
    // Not killed: starting the search at s = 0 instead of MinExpHalvingCount; s = 0 and 1 are never the cheapest, so
    // the planner never picks them. The limit s >= 2 that the bound needs is enforced on ExpHalvingCount (options test).
    public void PlanExp_TermsAreSmallestWithRemainderBelowOneUnit(int resultWords)
    {
        var plan = TranscendentalPlanner.PlanExp(32 * resultWords, 3.0, new GpuBigFloatTranscendentalOptions());

        Assert.InRange(plan.Halvings, 2, 64);
        AssertSmallestExpTermCount(plan.Terms, plan.WorkingBits, plan.Halvings);
    }

    // A fixed ExpHalvingCount is used as given, and the term count and guard words follow it.
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(40)]
    [InlineData(200)]
    [InlineData(4096)]
    public void PlanExp_FixedHalvingCount(int halvings)
    {
        var options = new GpuBigFloatTranscendentalOptions { ExpHalvingCount = halvings };

        var plan = TranscendentalPlanner.PlanExp(128, 3.0, options);

        Assert.Equal(halvings, plan.Halvings);
        AssertSmallestExpTermCount(plan.Terms, plan.WorkingBits, plan.Halvings);
        AssertExpErrorBoundAndGuard(plan);
    }

    // The bound of the term recurrence (§4.4.1, Fable): E < 2^s (N + 0.6 ln(N + 1) + eps_R + 1.7) - 1 with
    // eps_R <= 1, and the guard words ceil((bits(B) + 16) / 32) that keep the rounding check failure below 2^-15.
    [Theory]
    [Scenario(TestScenario.Boundary)]
    // mutant: TranscendentalPlanner.cs ExpRecurrenceErrorBound `<< (halvings - 30)` -> `<< (halvings - 31)`
    // mutant: TranscendentalPlanner.cs PlanExp guard loop `if (needed <= guardWords)` -> `if (true)` (gw stays 1)
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(31)]
    [InlineData(129)]
    public void PlanExp_ErrorBoundCoversTheRecurrence(int resultWords)
    {
        var plan = TranscendentalPlanner.PlanExp(32 * resultWords, 3.0, new GpuBigFloatTranscendentalOptions());

        AssertExpErrorBoundAndGuard(plan);
    }

    // The cost model of stage 3: (N + s)·W is smallest, the smaller s on a tie. For a 4-word result it picks s = 8,
    // N = 15, W = 160. Stage 4 replaces the recurrence by Paterson–Stockmeyer, whose bound does not grow with N,
    // and the choice becomes s = 2 (design §10.1 table); this expectation changes then.
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    // mutant: TranscendentalPlanner.cs PlanExp `if (cost < bestCost)` -> `if (cost <= bestCost)` (the larger s of a tie)
    public void PlanExp_FourWords_ChoosesEightHalvings()
    {
        var plan = TranscendentalPlanner.PlanExp(128, 3.0, new GpuBigFloatTranscendentalOptions());

        Assert.Equal(8, plan.Halvings);
        Assert.Equal(15, plan.Terms);
        Assert.Equal(160, plan.WorkingBits);
    }

    // k = floor(x / ln 2) - 1 puts r = x - k ln 2 into (0.69, 1.39), and ln 2 carries W - s + bits(|k|) + 2 fraction
    // bits so that |k|·|L - ln 2| <= 2^-(W - s + 2).
    [Theory]
    [Scenario(TestScenario.Boundary)]
    // mutant: TranscendentalPlanner.cs PlanExp `Math.Floor(xApprox / Ln2) - 1` -> `Math.Floor(xApprox / Ln2) + 1`
    // mutant: TranscendentalPlanner.cs PlanExp `+ BitLength(reduction) + 2` -> `+ 2`
    [InlineData(1e-30)]
    [InlineData(0.5)]
    [InlineData(0.6931471805599453)]
    [InlineData(3.0)]
    [InlineData(-1.5)]
    [InlineData(-2e-30)]
    [InlineData(320.0)]
    [InlineData(1048576.0)]
    [InlineData(-1048576.0)]
    public void PlanExp_ReductionAndLn2Bits(double x)
    {
        var plan = TranscendentalPlanner.PlanExp(128, x, new GpuBigFloatTranscendentalOptions());

        var r = x - plan.Reduction * Math.Log(2);
        Assert.InRange(r, 0.69, 1.39);
        var magnitude = (ulong)Math.Abs(plan.Reduction);
        var bits = magnitude == 0 ? 0 : 64 - BitOperations.LeadingZeroCount(magnitude);
        Assert.Equal(plan.WorkingBits - plan.Halvings + bits + 2, plan.Ln2FractionBits);
    }

    // A result exponent outside int fails before any work: overflow for large x, underflow (named in the message)
    // for very negative x.
    [Theory]
    [Scenario(TestScenario.SadPath)]
    // mutant: TranscendentalPlanner.cs PlanExp `if (reduction + 1L - resultBits > int.MaxValue)` throw removed: 1.5e9
    // mutant: TranscendentalPlanner.cs PlanExp `if (!(Math.Abs(xApprox) < ArgumentLimit))` throw removed: -1e300
    // (k saturates to long.MinValue, k - 1 wraps around and the wrong exception message comes out)
    [InlineData(1.5e9, "overflow")]
    [InlineData(-1.5e9, "underflow")]
    [InlineData(4294967296.0, "overflow")]
    [InlineData(1e300, "overflow")]
    [InlineData(-1e300, "underflow")]
    public void PlanExp_ExponentOutOfRange_Throws(double x, string word)
    {
        var error = Assert.Throws<OverflowException>(() =>
            TranscendentalPlanner.PlanExp(128, x, new GpuBigFloatTranscendentalOptions()));

        Assert.Contains(word, error.Message, StringComparison.Ordinal);
    }

    // ExpHalvingCount accepts null and [2, 4096] and rejects the rest.
    [Theory]
    [Scenario(TestScenario.SadPath)]
    // mutant: GpuBigFloatTranscendentalOptions.cs `ThrowIfLessThan(halvings, MinExpHalvingCount, ...)` removed
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(4097)]
    public void Options_ExpHalvingCountOutOfRange_Throws(int halvings)
    {
        var options = new GpuBigFloatTranscendentalOptions();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.ExpHalvingCount = halvings);
        Assert.Null(options.ExpHalvingCount);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Options_ExpHalvingCountLimits_AreAccepted()
    {
        var options = new GpuBigFloatTranscendentalOptions { ExpHalvingCount = 2 };
        Assert.Equal(2, options.ExpHalvingCount);
        options.ExpHalvingCount = 4096;
        Assert.Equal(4096, options.ExpHalvingCount);
        options.ExpHalvingCount = null;
        Assert.Null(options.ExpHalvingCount);
    }

    // eps_R = 2 rho^(N+1) / ((N+1)! 2^-W) <= 1 with rho = 139 / (100 · 2^s):
    // 2 · 139^(N+1) · 2^W <= (N+1)! · (100 · 2^s)^(N+1); and it fails for N - 1.
    private static void AssertSmallestExpTermCount(int terms, int workingBits, int halvings)
    {
        static bool Covers(int n, int w, int s)
        {
            BigInteger factorial = 1;
            for (var i = 2; i <= n + 1; i++)
            {
                factorial *= i;
            }

            return 2 * BigInteger.Pow(139, n + 1) << w <= factorial * BigInteger.Pow(100 * (BigInteger.One << s), n + 1);
        }

        Assert.True(terms >= 1);
        Assert.True(Covers(terms, workingBits, halvings), $"{terms} terms leave a remainder above 2^-{workingBits}");
        Assert.True(terms == 1 || !Covers(terms - 1, workingBits, halvings), $"{terms - 1} terms already suffice");
    }

    // B >= 2^s (N + 0.6 ln(N + 1) + 2.7) - 1 (eps_R <= 1), and gw = ceil((bits(B) + 16) / 32), W = p + 32 gw.
    private static void AssertExpErrorBoundAndGuard(ExpPlan plan)
    {
        var n = plan.Terms;
        var factor = n + 0.6 * Math.Log(n + 1) + 2.7;

        // Compare B + 1 >= 2^s · factor without overflowing a double: scale the factor to an integer from below.
        var scaledFactor = (BigInteger)Math.Floor(factor * (1 << 20));
        Assert.True((plan.ErrorBound + 1) << 20 >= scaledFactor << plan.Halvings, $"B = {plan.ErrorBound} is below the bound");
        var guardWords = ((int)plan.ErrorBound.GetBitLength() + 16 + 31) / 32;
        Assert.Equal(guardWords, plan.GuardWords);
        Assert.Equal(plan.ResultBits + 32 * guardWords, plan.WorkingBits);
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
