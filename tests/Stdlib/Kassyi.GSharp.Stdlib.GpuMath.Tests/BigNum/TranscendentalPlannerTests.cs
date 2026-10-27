#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Tests of the host-side parameter planner of the big-float Log / Exp (<see cref="TranscendentalPlanner"/>).
/// </summary>
/// <remarks>
/// <para>
/// The bounds come from the design (tmp/p3-lanes/628-log-exp-argument-reduction.md §4.3, §4.4, §10.1): the atanh series
/// <c>T = Σ_{k&lt;N} z^k / (2k+1)</c> stops when its remainder <c>ε'_R = z^N / ((2N+1)(1 − z)u)</c> is at most one unit
/// <c>u = 2^-W</c>, where <c>z = y²</c> is bounded by <c>1/25</c> (<c>|y| ≤ 1/5</c> for <c>m ∈ [3/4, 3/2)</c>) and,
/// near 1, by <c>2^(-2c) / 3</c> (<c>|u| &lt; 2^-c</c> and <c>|y| ≤ |u| / 1.75</c>). The exp polynomial stops when
/// <c>ε_R = 2ρ^(N+1) / ((N+1)!·u) ≤ 1</c>, <c>ρ = 1.39·2^-s</c>.
/// </para>
/// <para>
/// Error bounds (§10.1): atanh Horner <c>B = 7</c>, atanh Paterson–Stockmeyer <c>B = m + 6</c> (from
/// <c>F &lt; m + 0.6 + ε'_R</c>), exp Paterson–Stockmeyer <c>B = ceil(5.83·2^s) − 1</c> (from
/// <c>E &lt; 2^s·(ε_R + 4.83) − 1</c>).
/// </para>
/// </remarks>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Stdlib)]
public class TranscendentalPlannerTests
{
    private static GpuBigFloatTranscendentalOptions Defaults => new();

    public static TheoryData<int> AllResultWords()
    {
        var data = new TheoryData<int>();
        for (var words = 1; words <= 256; words++)
        {
            data.Add(words);
        }

        return data;
    }

    // Far from 1 (c = 1, z <= 1/25): N is the smallest count with eps'_R <= 1, checked exactly with rationals; the
    // block size is the cheapest one.
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(AllResultWords))]
    // mutant: TranscendentalPlanner.cs AtanhSquareBound `new BigInteger(25)` -> `new BigInteger(49)` (too few terms
    // for |y| = 1/5; also fails LogWorstY in GpuBigFloatTranscendentalTests)
    // mutant: TranscendentalPlanner.cs CoversRemainder `BigInteger.Pow(zDen, terms - 1)` -> `BigInteger.Pow(zDen, terms)`
    // mutant: TranscendentalPlanner.cs BestBlockSize `if (count < bestCount)` -> `if (count <= bestCount)`
    // Not killed: `(2 * terms + 1)` -> `(2 * terms + 3)` does not change N for any W tested.
    public void PlanLog_FarFromOne_TermsAreSmallestWithRemainderBelowOneUnit(int resultWords)
    {
        var plan = TranscendentalPlanner.PlanLog(32 * resultWords, 1, 1, Defaults);

        AssertSmallestTermCount(plan.Terms, plan.WorkingBits, 1, 25);
        Assert.True(plan.Terms > 8, "far from 1 the series has more terms than the Horner limit");
        AssertCheapestBlockSize(plan.BlockSize, plan.Terms - 1, plan.Terms);
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
        var plan = TranscendentalPlanner.PlanLog(32 * resultWords, 0, closenessBits, Defaults);

        AssertSmallestTermCount(plan.Terms, plan.WorkingBits, 1, 3 * (BigInteger.One << (2 * closenessBits)));
    }

    // m = 1 (the argument is a power of two) needs no series.
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void PlanLog_PowerOfTwo_HasNoTerms()
    {
        var plan = TranscendentalPlanner.PlanLog(128, 5, int.MaxValue, Defaults);

        Assert.Equal(0, plan.Terms);
    }

    // The design table (§10.1, atanh for W = p + 32): N, the Paterson–Stockmeyer m, its multiplications
    // m - 1 + floor((N - 1) / m) and B = m + 6.
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(1, 64, 13, 3, 6, 9)]
    [InlineData(3, 128, 27, 4, 9, 10)]
    [InlineData(7, 256, 54, 6, 13, 12)]
    [InlineData(15, 512, 109, 10, 19, 16)]
    [InlineData(31, 1024, 219, 13, 28, 19)]
    public void PlanLog_MatchesDesignTable(
        int resultWords, int workingBits, int terms, int blockSize, int multiplications, int errorBound)
    {
        var plan = TranscendentalPlanner.PlanLog(32 * resultWords, 1, 1, Defaults);

        Assert.Equal(workingBits, plan.WorkingBits);
        Assert.Equal(terms, plan.Terms);
        Assert.Equal(blockSize, plan.BlockSize);
        Assert.Equal(multiplications, plan.BlockSize - 1 + (plan.Terms - 1) / plan.BlockSize);
        Assert.Equal(errorBound, plan.ErrorBound);
    }

    // The form and its bound: Horner from the top (block size 0) gives B = 7 (F < 1.79 + eps'_R), Paterson–Stockmeyer
    // B = m + 6 (F < m + 0.6 + eps'_R); both with one guard word, ceil((ceil(log2(B + 1)) + 16) / 32) = 1.
    [Theory]
    [Scenario(TestScenario.Boundary)]
    // mutant: TranscendentalPlanner.cs `LogHornerErrorBound = 7` -> `6`
    // mutant: TranscendentalPlanner.cs LogPatersonStockmeyerErrorBound `blockSize + 6` -> `blockSize + 5`
    // mutant: TranscendentalPlanner.cs PlanLog `if (terms > options.AtanhHornerMaxTerms)` -> `if (terms >= ...)`
    // mutant: TranscendentalPlanner.cs GuardWordsFor `+ 16` -> `+ 48` (two guard words)
    // mutant: TranscendentalPlanner.cs PlanLog `options.SeriesBlockSize ?? BestBlockSize(...)` -> `BestBlockSize(...)`
    [InlineData(1, 1, 8, null, 3)] // N = 13 > 8: PS
    [InlineData(4, 1, int.MaxValue, null, 0)] // forced Horner
    [InlineData(4, 1, 0, 7, 7)] // forced PS with a fixed m
    [InlineData(4, 30, 8, null, 0)] // near 1: N = 3 <= 8, Horner
    [InlineData(4, 30, 3, null, 0)] // N = 3 = limit: still Horner
    [InlineData(4, 30, 2, null, 2)] // N = 3 > 2: PS
    [InlineData(4, 1000, 0, null, 2)] // N = 1, forced PS
    [InlineData(129, 1, 8, 4096, 4096)]
    public void PlanLog_FormAndErrorBound(int resultWords, int closenessBits, int hornerMaxTerms, int? blockSize, int expectedBlockSize)
    {
        var options = new GpuBigFloatTranscendentalOptions { AtanhHornerMaxTerms = hornerMaxTerms, SeriesBlockSize = blockSize };

        var plan = TranscendentalPlanner.PlanLog(32 * resultWords, -3, closenessBits, options);

        Assert.Equal(expectedBlockSize, plan.BlockSize);
        Assert.Equal(plan.BlockSize == 0 ? 7 : plan.BlockSize + 6, plan.ErrorBound);
        Assert.Equal(1, plan.GuardWords);
        Assert.Equal(32 * resultWords + 32, plan.WorkingBits);
    }

    // ln 2 for e·ln 2 carries W + ceil(log2 |e|) + 4 fraction bits, so that |e·L − e·ln 2| <= 2^-(W+4) (§4.3.1).
    [Theory]
    [Scenario(TestScenario.Boundary)]
    // mutant: TranscendentalPlanner.cs PlanLog `+ CeilingLog2((ulong)Math.Abs(e)) + 4` -> `+ 4`
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
        var plan = TranscendentalPlanner.PlanLog(128, e, 1, Defaults);

        Assert.Equal(plan.WorkingBits + ceilingLog2 + 4, plan.Ln2FractionBits);
    }

    // Exp: N is the smallest count with eps_R = 2 rho^(N+1) / ((N+1)! u) <= 1 for rho = 1.39 · 2^-s (r < 1.39 after
    // the ln 2 reduction), checked exactly with rationals; s >= 2; the block size is the cheapest one; and the plan
    // has the fewest weighted multiplications (m - 1 + floor(N / m) + s)·W of all s in [2, 64].
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(AllResultWords))]
    // mutant: TranscendentalPlanner.cs ExpRemainderCovered `BigInteger.Pow(139, terms + 1)` ->
    // `BigInteger.Pow(139, terms)` (one term too few)
    // mutant: TranscendentalPlanner.cs PlanExp `var multiplications = blockSize - 1 + terms / blockSize + halvings;` ->
    // `... + 2 * halvings;` (the squarings counted twice: a smaller s than the cheapest)
    // Not killed: starting the search at s = 0 instead of MinExpHalvingCount; s = 0 and 1 are never the cheapest, so
    // the planner never picks them. The limit s >= 2 that the bound needs is enforced on ExpHalvingCount (options test).
    public void PlanExp_TermsBlockAndHalvingsAreCheapest(int resultWords)
    {
        var plan = TranscendentalPlanner.PlanExp(32 * resultWords, 3.0, Defaults);

        Assert.InRange(plan.Halvings, 2, 64);
        AssertSmallestExpTermCount(plan.Terms, plan.WorkingBits, plan.Halvings);
        AssertCheapestBlockSize(plan.BlockSize, plan.Terms, plan.Terms);
        AssertExpErrorBoundAndGuard(plan);

        // Every other s costs at least as much (the planner's own cost of a fixed s).
        var cost = ExpCost(plan);
        for (var halvings = 2; halvings <= 64; halvings++)
        {
            var other = TranscendentalPlanner.PlanExp(
                32 * resultWords, 3.0, new GpuBigFloatTranscendentalOptions { ExpHalvingCount = halvings });
            Assert.True(ExpCost(other) >= cost, $"s = {halvings} is cheaper than the chosen s = {plan.Halvings}");
            Assert.True(ExpCost(other) > cost || halvings >= plan.Halvings, $"s = {halvings} ties and is smaller");
        }
    }

    // A fixed ExpHalvingCount or SeriesBlockSize is used as given; the term count and guard words follow it.
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(2, null)]
    [InlineData(3, null)]
    [InlineData(13, null)]
    [InlineData(14, null)]
    [InlineData(40, null)]
    [InlineData(200, null)]
    [InlineData(4096, null)]
    [InlineData(null, 2)]
    [InlineData(null, 3)]
    [InlineData(null, 100)]
    [InlineData(2, 4096)]
    public void PlanExp_FixedParameters(int? halvings, int? blockSize)
    {
        var options = new GpuBigFloatTranscendentalOptions { ExpHalvingCount = halvings, SeriesBlockSize = blockSize };

        var plan = TranscendentalPlanner.PlanExp(128, 3.0, options);

        if (halvings is { } s)
        {
            Assert.Equal(s, plan.Halvings);
        }

        if (blockSize is { } m)
        {
            Assert.Equal(m, plan.BlockSize);
        }

        AssertSmallestExpTermCount(plan.Terms, plan.WorkingBits, plan.Halvings);
        AssertExpErrorBoundAndGuard(plan);
    }

    // The design table (§10.1, exp for W = p + 32): s = 2, N, m, the multiplications m - 1 + floor(N / m) + s and
    // B = ceil(5.83 · 4) - 1 = 23.
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    // mutant: TranscendentalPlanner.cs ExpPatersonStockmeyerErrorBound `583 *` -> `100 *` (B = 3 at s = 2)
    // `583 *` -> `582 *` still gives 23 at s = 2; the bound check of PlanExp_FixedParameters and
    // PlanExp_TermsBlockAndHalvingsAreCheapest catches it at larger s.
    [InlineData(1, 64, 15, 4, 8)]
    [InlineData(3, 128, 26, 4, 11)]
    [InlineData(7, 256, 45, 6, 14)]
    [InlineData(15, 512, 79, 8, 18)]
    [InlineData(31, 1024, 141, 11, 24)]
    public void PlanExp_MatchesDesignTable(int resultWords, int workingBits, int terms, int blockSize, int multiplications)
    {
        var plan = TranscendentalPlanner.PlanExp(32 * resultWords, 3.0, Defaults);

        Assert.Equal(2, plan.Halvings);
        Assert.Equal(workingBits, plan.WorkingBits);
        Assert.Equal(terms, plan.Terms);
        Assert.Equal(blockSize, plan.BlockSize);
        Assert.Equal(multiplications, plan.BlockSize - 1 + plan.Terms / plan.BlockSize + plan.Halvings);
        Assert.Equal(new BigInteger(23), plan.ErrorBound);
    }

    // The choices that the precision tests rely on. Stage 3 (term recurrence, bound growing with N) chose s = 8, 27
    // and 57 with 1, 2 and 3 guard words here; Paterson–Stockmeyer, whose bound does not grow with N, chooses fewer
    // halvings and keeps one guard word.
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    // mutant: TranscendentalPlanner.cs PlanExp `if (cost < bestCost)` -> `if (cost <= bestCost)` (the larger s of a tie)
    [InlineData(4, 2, 31, 4, 1)]
    [InlineData(31, 2, 141, 11, 1)]
    [InlineData(129, 5, 359, 18, 1)]
    public void PlanExp_ChoosesHalvingsBlockAndGuard(int resultWords, int halvings, int terms, int blockSize, int guardWords)
    {
        var plan = TranscendentalPlanner.PlanExp(32 * resultWords, 3.0, Defaults);

        Assert.Equal(halvings, plan.Halvings);
        Assert.Equal(terms, plan.Terms);
        Assert.Equal(blockSize, plan.BlockSize);
        Assert.Equal(guardWords, plan.GuardWords);
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
        var plan = TranscendentalPlanner.PlanExp(128, x, Defaults);

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
        var error = Assert.Throws<OverflowException>(() => TranscendentalPlanner.PlanExp(128, x, Defaults));

        Assert.Contains(word, error.Message, StringComparison.Ordinal);
    }

    // The working precision of an Exp does not depend on its argument; Pow plans its Log from it before the argument
    // y·L exists.
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    // mutant: TranscendentalPlanner.cs ExpWorkingBits `resultBits + 32 * series.GuardWords` -> `resultBits + 32`
    // (ExpForcedHalving s = 40, 200: more than one guard word)
    [InlineData(1, null, 0)]
    [InlineData(4, null, 0)]
    [InlineData(4, null, 3)]
    [InlineData(31, null, 0)]
    [InlineData(129, null, 2)]
    [InlineData(4, 40, 0)]
    [InlineData(4, 200, 1)]
    public void ExpWorkingBits_MatchesThePlan(int resultWords, int? halvings, int extraGuardWords)
    {
        var options = new GpuBigFloatTranscendentalOptions { ExpHalvingCount = halvings };

        var workingBits = TranscendentalPlanner.ExpWorkingBits(32 * resultWords, options, extraGuardWords);

        foreach (var x in new[] { -1000.5, 1e-30, 1.0, 3.0e8 })
        {
            Assert.Equal(TranscendentalPlanner.PlanExp(32 * resultWords, x, options, extraGuardWords).WorkingBits, workingBits);
        }
    }

    // Pow's Log runs at the Exp precision plus 64 bits: |y·ln x| < 2^32 and 32 more bits keep the argument error D at
    // one unit.
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    // mutant: TranscendentalPlanner.cs `PowLogExtraBits = 64` -> `0`
    public void PowLogResultBits_AddsTwoWords()
    {
        Assert.Equal(224, TranscendentalPlanner.PowLogResultBits(160));
        Assert.Equal(96, TranscendentalPlanner.PowLogResultBits(32));
    }

    // D = ceil(|y| · B_L · 2^(U_L + W + 2)), the argument error of Pow in units of the Exp approximation.
    [Theory]
    [Scenario(TestScenario.Boundary)]
    // mutant: TranscendentalPlanner.cs PowArgumentErrorBound ceiling `quotient + 1` -> `quotient` (5.25 -> 5)
    // mutant: TranscendentalPlanner.cs PowArgumentErrorBound `scaled << (int)shift` -> `scaled` (280 -> 35)
    // mutant: TranscendentalPlanner.cs PowArgumentErrorBound below one unit `BigInteger.One` -> `BigInteger.Zero`
    // mutant: TranscendentalPlanner.cs PowArgumentErrorBound `+ expWorkingBits + 2` -> `+ expWorkingBits` (D / 4):
    // 21 / 4, 1, 7, 280
    [InlineData("3", 0L, "7", -100L, 96, "6")] // 21 / 4 = 5.25
    [InlineData("4", 0L, "1", -100L, 96, "1")] // 4 / 4 = 1, no rounding
    [InlineData("1", 0L, "7", -98L, 96, "7")] // shift 0
    [InlineData("5", 3L, "7", -98L, 96, "280")] // shift 3: 5 · 7 · 8
    [InlineData("1", -1000000L, "7", -98L, 96, "1")] // far below one unit
    [InlineData("1048577", 0L, "7", -150L, 96, "1")] // 7 · (2^20 + 1) / 2^52 < 1
    public void PowArgumentErrorBound_IsTheCeiling(
        string ySignificand, long yExponent, string logErrorBound, long logUnitExponent, int expWorkingBits, string expected)
    {
        var bound = TranscendentalPlanner.PowArgumentErrorBound(
            BigInteger.Parse(ySignificand), yExponent, BigInteger.Parse(logErrorBound), logUnitExponent, expWorkingBits);

        Assert.Equal(BigInteger.Parse(expected), bound);
    }

    // The Exp range checks carry the name of the operation; PlanExp reduces by the same k.
    [Theory]
    [Scenario(TestScenario.SadPath)]
    // mutant: TranscendentalPlanner.cs OutOfRange `$"{operation} overflow: ..."` -> `"Exp overflow: ..."`
    [InlineData(5e9, "Pow overflow")]
    [InlineData(-5e9, "Pow underflow")]
    [InlineData(1.5e9, "Pow overflow")]
    [InlineData(-1.5e9, "Pow underflow")]
    public void ExpReduction_OutOfRange_NamesTheOperation(double x, string words)
    {
        var error = Assert.Throws<OverflowException>(() => TranscendentalPlanner.ExpReduction(128, x, "Pow"));

        Assert.StartsWith(words, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(1.0)]
    [InlineData(-1000.25)]
    [InlineData(1.48e9)]
    public void ExpReduction_IsTheReductionOfThePlan(double x)
    {
        Assert.Equal(TranscendentalPlanner.PlanExp(128, x, Defaults).Reduction, TranscendentalPlanner.ExpReduction(128, x, "Exp"));
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(2147483648L, "Pow overflow")]
    [InlineData(-2147483649L, "Pow underflow")]
    public void ToResultExponent_OutOfRange_NamesTheOperation(long exponent, string words)
    {
        var error = Assert.Throws<OverflowException>(() => TranscendentalPlanner.ToResultExponent(exponent, "Pow"));

        Assert.StartsWith(words, error.Message, StringComparison.Ordinal);
        Assert.Equal(int.MaxValue, TranscendentalPlanner.ToResultExponent(int.MaxValue, "Pow"));
        Assert.Equal(int.MinValue, TranscendentalPlanner.ToResultExponent(int.MinValue, "Pow"));
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

    // SeriesBlockSize accepts null and [2, 4096]; AtanhHornerMaxTerms accepts any value from 0.
    [Theory]
    [Scenario(TestScenario.SadPath)]
    // mutant: GpuBigFloatTranscendentalOptions.cs `ThrowIfLessThan(blockSize, MinSeriesBlockSize, ...)` removed
    // mutant: GpuBigFloatTranscendentalOptions.cs AtanhHornerMaxTerms `ThrowIfNegative(value)` removed
    [InlineData(1, 0)]
    [InlineData(4097, 0)]
    [InlineData(2, -1)]
    public void Options_SeriesParametersOutOfRange_Throw(int blockSize, int hornerMaxTerms)
    {
        var options = new GpuBigFloatTranscendentalOptions();

        if (blockSize < 2 || blockSize > 4096)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => options.SeriesBlockSize = blockSize);
            Assert.Null(options.SeriesBlockSize);
        }

        if (hornerMaxTerms < 0)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => options.AtanhHornerMaxTerms = hornerMaxTerms);
            Assert.Equal(8, options.AtanhHornerMaxTerms);
        }
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Options_Limits_AreAccepted()
    {
        var options = new GpuBigFloatTranscendentalOptions { ExpHalvingCount = 2, SeriesBlockSize = 2, AtanhHornerMaxTerms = 0 };
        Assert.Equal(2, options.ExpHalvingCount);
        Assert.Equal(2, options.SeriesBlockSize);
        Assert.Equal(0, options.AtanhHornerMaxTerms);
        options.ExpHalvingCount = 4096;
        options.SeriesBlockSize = 4096;
        options.AtanhHornerMaxTerms = int.MaxValue;
        Assert.Equal(4096, options.ExpHalvingCount);
        Assert.Equal(4096, options.SeriesBlockSize);
        Assert.Equal(int.MaxValue, options.AtanhHornerMaxTerms);
        options.ExpHalvingCount = null;
        options.SeriesBlockSize = null;
        Assert.Null(options.ExpHalvingCount);
        Assert.Null(options.SeriesBlockSize);
        Assert.Equal(8, new GpuBigFloatTranscendentalOptions().AtanhHornerMaxTerms);
    }

    // (m - 1 + floor(N / m) + s)·W, the planner's cost of an Exp plan.
    private static long ExpCost(ExpPlan plan) =>
        (long)(plan.BlockSize - 1 + plan.Terms / plan.BlockSize + plan.Halvings) * plan.WorkingBits;

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

    // B + 1 >= 5.83 · 2^s (E < 2^s (eps_R + 4.83) - 1 with eps_R <= 1), and gw = ceil((bits(B) + 16) / 32),
    // W = p + 32 gw.
    private static void AssertExpErrorBoundAndGuard(ExpPlan plan)
    {
        Assert.True((plan.ErrorBound + 1) * 100 >= 583 * (BigInteger.One << plan.Halvings), $"B = {plan.ErrorBound} is below the bound");
        var guardWords = ((int)plan.ErrorBound.GetBitLength() + 16 + 31) / 32;
        Assert.Equal(guardWords, plan.GuardWords);
        Assert.Equal(plan.ResultBits + 32 * guardWords, plan.WorkingBits);
    }

    // m minimizes m - 1 + floor(D / m) over [2, max(2, floor(2 sqrt(N)))], the smallest m on a tie.
    private static void AssertCheapestBlockSize(int blockSize, int degree, int terms)
    {
        var last = Math.Max(2, (int)Math.Floor(2 * Math.Sqrt(terms)));
        Assert.InRange(blockSize, 2, last);
        var count = blockSize - 1 + degree / blockSize;
        for (var other = 2; other <= last; other++)
        {
            var otherCount = other - 1 + degree / other;
            Assert.True(otherCount > count || (otherCount == count && other >= blockSize),
                $"m = {other} needs {otherCount} multiplications, the chosen m = {blockSize} needs {count}");
        }
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
