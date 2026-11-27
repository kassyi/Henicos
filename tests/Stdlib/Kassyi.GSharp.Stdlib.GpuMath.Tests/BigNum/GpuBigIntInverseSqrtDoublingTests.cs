#nullable enable
using Xunit.Abstractions;
using Geometry = Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.InverseSqrtDoublingReference.Geometry;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

// Properties checked here (property-test-design §1):
// - Every stage of the precision doubling (its width, Y and D) and x~ agree bit for bit with the golden values of the
//   reference model and with its BigInteger port, for the initial guess and for a first Y above the root (D < 0).
// - Proved bounds: |D| < 2^(32 dw - 1) in every stage, |Y_K - floor(Y*)| <= 1 and |x~ - floor(x*)| <= 1.
// - The approximate inverse square root returns x~ - 1 on the doubling path, and SqrtFloor and Sqrt stay exact.
// - The same in CPU fallback mode. The switch itself is tested in GpuBigIntSqrtDoublingThresholdTests.
// Every expected value is computed on the host from the operand the test builds, never from a value read back.
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Stdlib)]
public class GpuBigIntInverseSqrtDoublingTests(ITestOutputHelper output)
{
    // |x~ - floor(x*)| <= 1 and |Y_K - floor(Y*)| <= 1: the bound the derivation proves for an operand of at least two
    // words, from an initial guess with x0 <= x* and a relative error of at most 5*2^-32 (provable).
    private const int ProvenBound = 1;

    private static readonly string[] s_normalizedKinds =
    [
        "Random", "PowerOfFour", "TopBitOnly", "TopBitOnlyMinusOne", "AllOnes", "TopWordOnly", "JustAboveLowest",
    ];

    // The golden cases run on the GPU give the same width and Y in every stage and the same x~ as the Python model.
    // Only an equality per stage shows a wrong intermediate stage; the final x~ alone can hide it.
    // mutant: GpuBigIntDivRem.InverseSqrtDoubling.cs:57 `words - words / 2 + 1` -> `words - words / 2`
    // mutant: GpuBigIntDivRem.InverseSqrtDoubling.cs:172 `-(32 * residualWords + 1)` -> `-(32 * residualWords +
    //         (words == finalWords ? 1 : 2))` (an intermediate stage)
    // mutant: GpuBigIntDivRem.InverseSqrtDoubling.cs:182 `Math.Max(2, words - words / 2)` -> `... - 1`
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(InverseSqrtDoublingReference.GoldenCases), MemberType = typeof(InverseSqrtDoublingReference))]
    public void InverseSqrtDoubling_GoldenCase_MatchesEveryStageBitForBit(int index, string label, int rootWords)
    {
        CudaCapability.EnsureGpuAvailable();
        var golden = InverseSqrtDoublingReference.Golden[index];
        var g = new Geometry(rootWords);
        // The golden case was computed with the margin SqrtFloor uses for this root.
        Assert.Equal(golden.MarginWords, g.M);
        using var ctx = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);
        using var operand = GpuBigUInt.FromBigInteger(ctx, golden.A, g.W);

        var stages = RunDeviceStages(operand, g, InverseSqrtDoublingReference.FirstY(golden.A, g), out var xTilde);

        // The same schedule, and in every stage the same Y as the reference model.
        Assert.Equal(golden.Stages.Count, stages.Count);
        for (var k = 0; k < stages.Count; k++)
        {
            Assert.True(golden.Stages[k].Words == stages[k].Words && golden.Stages[k].Y == stages[k].Y,
                $"{label} n={rootWords}: stage {k} (p = {stages[k].Words}) differs from the golden value");
        }

        // x~ = floor(Y_K / 2^(32 p_K - E)), computed from the last stage Y_K.
        Assert.Equal(golden.XTilde, xTilde);
    }

    /// <summary>The normalized operands: the kind and the root words <c>n</c> (the operand has <c>n + 1</c>).</summary>
    public static TheoryData<string, int> NormalizedInputs()
    {
        var data = new TheoryData<string, int>();
        foreach (var kind in s_normalizedKinds)
        {
            foreach (var n in new[] { 1, 2, 3, 7, 31, 32, 33, 127, 1000, 5000 })
            {
                data.Add(kind, n);
            }
        }

        return data;
    }

    // R: every stage and x~ agree bit for bit with the BigInteger model from the same initial guess.
    // TD: |D| < 2^(32 dw - 1) in every stage, the width the offset representation needs, on the model's D, which is not
    //     cut to dw words (the D read back from dw words is in range by construction). TD bounds the derivation; a
    //     device mutant shows in R.
    // T1Y: the last Y is within ProvenBound of floor(Y*), Y* = 2^(B + 32 p_K - E) / sqrt(A): an error in an
    //      intermediate stage that the final shift hides still shows here.
    // T1: x~ is within ProvenBound of floor(x*), x* = 2^B / sqrt(A).
    // mutant: GpuBigIntDivRem.InverseSqrtDoubling.cs:57 `words - words / 2 + 1` -> `words - words / 2` (R, T1Y, T1)
    // mutant: GpuBigIntDivRem.InverseSqrtDoubling.cs:172 `-(32 * residualWords + 1)` -> `-(32 * residualWords +
    //         (words == finalWords ? 1 : 2))` (R, T1Y, T1)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(NormalizedInputs))]
    public void InverseSqrtDoubling_NormalizedOperand_FollowsReferenceWithinOneOfFloor(string kind, int rootWords)
    {
        CudaCapability.EnsureGpuAvailable();
        var g = new Geometry(rootWords);
        var a = BuildNormalizedOperand(kind, g.W);
        var firstY = InverseSqrtDoublingReference.FirstY(a, g);
        using var ctx = new GpuWorkspace((g.W + g.M) * 8, GpuExecutionEngine.GSharpAot);
        using var operand = GpuBigUInt.FromBigInteger(ctx, a, g.W);

        var stages = RunDeviceStages(operand, g, firstY, out var x);
        var reference = InverseSqrtDoublingReference.Run(a, g, firstY, out var referenceX);

        // R: the same stages, Y and D, and the same x~ as the model.
        AssertStagesFollowTheModel(reference, stages, $"{kind} n={rootWords}");
        var smallestSpare = long.MaxValue;
        for (var k = 0; k < reference.Count; k++)
        {
            // TD: the model's D fits in dw words with its offset, dw = max(2, ceil(p / 2)).
            var residualBits = 32L * Math.Max(2, (reference[k].Words + 1) / 2) - 1;
            var magnitudeBits = BigInteger.Abs(reference[k].D).GetBitLength();
            Assert.True(magnitudeBits <= residualBits,
                $"{kind} n={rootWords}: |D| of stage {k} has {magnitudeBits} bits, the bound {residualBits}");
            smallestSpare = Math.Min(smallestSpare, residualBits - magnitudeBits);
        }

        Assert.Equal(referenceX, x);
        output.WriteLine($"{kind} n={rootWords}: smallest spare bits of D = {smallestSpare}");

        // T1Y: the last stage runs at p_K = w + 1 and its Y is within the proven bound of floor(Y*).
        Assert.Equal(g.FinalWords, stages[^1].Words);
        var yStar = InverseSqrtDoublingReference.FloorScaledInverseSqrt(a, g.YExponent(g.FinalWords));
        Assert.True(BigInteger.Abs(stages[^1].Y - yStar) <= ProvenBound,
            $"{kind} n={rootWords}: Y_K - floor(Y*) = {stages[^1].Y - yStar}");

        // T1: x~ is within the proven bound of floor(x*); floor(Y*) shifted down is floor(x*), checked directly where
        // the oracle is cheap.
        var floorX = g.XTilde(yStar);
        if (rootWords <= 127)
        {
            Assert.Equal(InverseSqrtDoublingReference.FloorScaledInverseSqrt(a, g.B), floorX);
        }

        Assert.True(BigInteger.Abs(x - floorX) <= ProvenBound, $"{kind} n={rootWords}: x~ - floor(x*) = {x - floorX}");
    }

    /// <summary>Operands for a first Y above the root: the kind and the root words.</summary>
    public static TheoryData<string, int> NegativeResidualInputs()
    {
        var data = new TheoryData<string, int>();
        foreach (var kind in new[] { "Random", "AllOnes" })
        {
            foreach (var n in new[] { 1, 2, 3, 7, 31, 32, 33, 127, 1000 })
            {
                data.Add(kind, n);
            }
        }

        return data;
    }

    // A first Y 2^24 above floor(Y*) at 2 words makes the first D negative, which the initial guess (never above the
    // root) does not reach: the offset representation of D and of Yt·D must still give the model's stages bit for bit.
    // The start is outside the premise of the proof, so x~ is held to ApproximateInverseSqrtMaxError only.
    // mutant: GpuBigIntDivRem.InverseSqrtDoubling.cs:151 and :168 D unsigned: `residualShift + 32 * residualWords - 1`
    //         -> `64 * words + 60 - 32 * padWords` and `ShiftAndResize(yTop, 32 * residualWords - 1, offset)` ->
    //         `offset.Clear()` (the first stage differs)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(NegativeResidualInputs))]
    public void InverseSqrtDoubling_FirstGuessAboveRoot_CorrectsThroughNegativeD(string kind, int rootWords)
    {
        CudaCapability.EnsureGpuAvailable();
        var g = new Geometry(rootWords);
        var a = BuildNormalizedOperand(kind, g.W);
        var firstY = InverseSqrtDoublingReference.FloorScaledInverseSqrt(a, g.YExponent(2)) + (BigInteger.One << 24);
        // The test entry takes a first Y of two words below 2^64; this one is below 2^63.
        Assert.True(firstY < BigInteger.One << 63, $"{kind} n={rootWords}: first Y {firstY:X} is not below 2^63");
        using var ctx = new GpuWorkspace((g.W + g.M) * 8, GpuExecutionEngine.GSharpAot);
        using var operand = GpuBigUInt.FromBigInteger(ctx, a, g.W);

        var stages = RunDeviceStages(operand, g, firstY, out var x);
        var reference = InverseSqrtDoublingReference.Run(a, g, firstY, out var referenceX);

        // The input reaches the negative branch: the first D is below zero.
        Assert.True(stages[0].D.Sign < 0, $"{kind} n={rootWords}: the first D {stages[0].D} is not negative");

        // Every stage and x~ agree with the model, signed D included.
        AssertStagesFollowTheModel(reference, stages, $"{kind} n={rootWords}");
        Assert.Equal(referenceX, x);

        // The doubling still converges: x~ lies within ApproximateInverseSqrtMaxError of floor(x*).
        var floorX = g.XTilde(InverseSqrtDoublingReference.FloorScaledInverseSqrt(a, g.YExponent(g.FinalWords)));
        Assert.True(BigInteger.Abs(x - floorX) <= GpuBigIntDivRem.ApproximateInverseSqrtMaxError,
            $"{kind} n={rootWords}: x~ - floor(x*) = {x - floorX}");
    }

#if DEBUG
    // Debug builds check |D| < 2^(32 dw - 1) on the device. With Y = 0 the first stage has D = 2^64, which does not fit
    // in dw = 2 words with the offset: the stage readback throws OverflowException, and the error is reported once.
    // mutant: GpuBigIntDivRem.InverseSqrtDoubling.cs:156 `CheckFitsBits(...)` removed (no exception)
    // mutant: GpuBigIntDivRem.InverseSqrtDoubling.cs:182 `Math.Max(2, words - words / 2)` -> `... - 1` (the golden
    //         and normalized inputs throw)
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void InverseSqrtDoubling_FirstGuessZero_ReportsResidualOutOfRange()
    {
        CudaCapability.EnsureGpuAvailable();
        var g = new Geometry(7);
        var a = BuildNormalizedOperand("Random", g.W);
        using var ctx = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);
        using var operand = GpuBigUInt.FromBigInteger(ctx, a, g.W);

        Assert.Throws<OverflowException>(
            () => GpuBigIntDivRem.InverseSqrtDoublingStagesForTest(operand, g.FinalWords, BigInteger.Zero));
        ctx.ThrowIfDeviceErrors();
    }
#endif

    // A = (2^(16w) - 1)^2 + delta makes D negative in a stage wider than two words from the ordinary initial guess (the
    // model gives D = -1 or -(2^32 + 1) in the last or an intermediate stage); the first-Y test reaches D < 0 only in
    // the first stage. The stages follow the model, x~ is within one of the floor, and SqrtFloor of an operand with
    // these top words is the floor.
    // mutant: GpuBigIntDivRem.InverseSqrtDoubling.cs:151 `residualShift + 32 * residualWords - 1` ->
    //         `64 * words + 60 - 32 * padWords` (D without its offset: the negative D differs)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(5, 2)]
    [InlineData(5, 3)]
    [InlineData(7, 3)]
    [InlineData(8, 2)]
    [InlineData(33, 2)]
    [InlineData(46, 2)]
    [InlineData(46, 3)]
    public void InverseSqrtDoubling_OperandJustAboveSquareOfAllOnes_HasNegativeResidualInAWideStage(int operandWords,
        int delta)
    {
        CudaCapability.EnsureGpuAvailable();
        var g = new Geometry(operandWords - 1);
        var a = NegativeResidualOperand(operandWords, delta);
        var firstY = InverseSqrtDoublingReference.FirstY(a, g);
        var label = $"w={operandWords} delta={delta}";
        using var ctx = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);
        ctx.SqrtPrecisionDoublingThresholdWords = 0;
        using var operand = GpuBigUInt.FromBigInteger(ctx, a, g.W);

        var stages = RunDeviceStages(operand, g, firstY, out var x);
        var reference = InverseSqrtDoublingReference.Run(a, g, firstY, out var referenceX);

        // The input reaches what it is for: the model has a negative D in a stage of more than two words.
        Assert.Contains(reference, static s => s.D.Sign < 0 && s.Words > 2);
        // Every stage, signed D included, and x~ agree with the model.
        AssertStagesFollowTheModel(reference, stages, label);
        Assert.Equal(referenceX, x);

        // x~ is within one of floor(x*).
        var floorX = InverseSqrtDoublingReference.FloorScaledInverseSqrt(a, g.B);
        output.WriteLine($"{label}: x~ - floor = {x - floorX}");
        Assert.True(BigInteger.Abs(x - floorX) <= ProvenBound, $"{label}: x~ - floor(x*) = {x - floorX}");

        // SqrtFloor of an operand with these top words and all ones below is the floor.
        var n = g.RootWords;
        var full = (a << (32 * (n - 1))) | ((BigInteger.One << (32 * (n - 1))) - 1);
        using var fullOperand = GpuBigUInt.FromBigInteger(ctx, full, 2 * n);
        using var root = new GpuBigUInt(ctx, n);
        GpuBigIntDivRem.SqrtFloor(fullOperand, root);
        Assert.Equal(GpuBigIntSqrtTests.FloorSqrt(full), root.ToBigInteger());
        ctx.ThrowIfDeviceErrors();
    }

    // The operand whose top 61 bits are 1073758207^2 (bit length 32w - 1, zeros below) has the worst initial guess an
    // exact search found: relative error 2^-29.67809, just inside the provable bound 5*2^-32 (2^-29.67807) that the
    // proof assumes. The stages follow the model, the model's D keeps its spare bits, and x~ stays within one of the
    // floor.
    // mutant: GpuBigIntDivRem.InverseSqrtDoubling.cs:104 word 0 and :105 word 1 swapped (every stage differs)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(46)]
    public void InverseSqrtDoubling_WorstInitialGuess_StaysWithinOneOfFloor(int operandWords)
    {
        CudaCapability.EnsureGpuAvailable();
        var g = new Geometry(operandWords - 1);
        var r = new BigInteger(1073758207);
        var a = (r * r) << (32 * operandWords - 62);
        Assert.Equal(32L * operandWords - 1, a.GetBitLength());

        // The input is the witness it is meant to be: x0 <= x* and 2^-29.7 < e <= 5*2^-32 for the relative error
        // e = 1 - x0·sqrt(a) / 2^B = numerator / 2^B, with sqrt(a) = r·2^(16w - 31) exactly.
        var x0 = InverseSqrtDoublingReference.InitialGuess(a, g.B);
        var numerator = (BigInteger.One << g.B) - x0 * (r << (16 * operandWords - 31));
        var relativeError = (double)((numerator << 64) >> g.B) / Math.Pow(2, 64);
        output.WriteLine($"w={operandWords}: initial guess relative error = 2^{Math.Log2(relativeError):F5}");
        Assert.True(numerator.Sign > 0, "the initial guess is above the root");
        Assert.True(numerator << 32 <= 5 * (BigInteger.One << g.B), "the initial guess is worse than 5*2^-32");
        Assert.True(relativeError > Math.Pow(2, -29.7), "the initial guess is not worse than 2^-29.7");

        var firstY = InverseSqrtDoublingReference.FirstY(a, g);
        using var ctx = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);
        using var operand = GpuBigUInt.FromBigInteger(ctx, a, g.W);
        var stages = RunDeviceStages(operand, g, firstY, out var x);
        var reference = InverseSqrtDoublingReference.Run(a, g, firstY, out var referenceX);

        // Every stage and x~ agree with the model.
        AssertStagesFollowTheModel(reference, stages, $"w={operandWords}");
        Assert.Equal(referenceX, x);

        // The model's D, not cut to dw words, stays below 2^(32 dw - 1).
        foreach (var (words, _, d) in reference)
        {
            var spare = 32L * Math.Max(2, (words + 1) / 2) - 1 - BigInteger.Abs(d).GetBitLength();
            Assert.True(spare >= 0, $"w={operandWords}: |D| of the stage of {words} words does not fit");
        }

        // x~ is within one of floor(x*).
        var floorX = InverseSqrtDoublingReference.FloorScaledInverseSqrt(a, g.B);
        Assert.True(BigInteger.Abs(x - floorX) <= ProvenBound, $"w={operandWords}: x~ - floor(x*) = {x - floorX}");
        ctx.ThrowIfDeviceErrors();
    }

    /// <summary>
    /// Operands and final working widths for <see cref="GpuBigIntDivRem.InverseSqrtDoublingStagesForTest"/>.
    /// </summary>
    public static TheoryData<string, int, int> AnyFinalWidthInputs()
    {
        var data = new TheoryData<string, int, int>();
        var widths = new HashSet<int>();
        foreach (var kind in s_normalizedKinds)
        {
            foreach (var n in new[] { 1, 2, 3, 7, 31, 32, 33, 46, 47, 127 })
            {
                widths.Clear();
                widths.Add(3);
                widths.Add(Math.Max(3, n / 2 + 1));
                widths.Add(n + 2);
                foreach (var finalWords in widths)
                {
                    data.Add(kind, n, finalWords);
                }
            }
        }

        return data;
    }

    // A-P1: for any final width p_K >= 3, every stage agrees bit for bit with RunTo, and |Y_K - floor(Y*(p_K))| <= 2
    // (the integer form of the 5/4 + 2^-21 bound for stages of at least three words).
    // mutant: GpuBigIntDivRem.InverseSqrtDoubling.cs:250 `+ finalWords + 1` -> `+ finalWords`
    //         (ArgumentOutOfRangeException)
    // mutant: GpuBigIntDivRem.InverseSqrtDoubling.cs:104 word 0 and :105 word 1 swapped (first stage differs)
    // mutant: GpuBigIntDivRem.InverseSqrtDoubling.cs:113 `y.NumWords` -> `operand.NumWords + 1` (finalWords < n + 2)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(AnyFinalWidthInputs))]
    public void InverseSqrtDoublingStages_AnyFinalWidth_MatchTheModel(string kind, int rootWords, int finalWords)
    {
        CudaCapability.EnsureGpuAvailable();
        var g = new Geometry(rootWords);
        var a = BuildNormalizedOperand(kind, g.W);
        var firstY = InverseSqrtDoublingReference.FirstY(a, g);
        using var ctx = new GpuWorkspace(Math.Max(4096, (g.W + finalWords) * 8), GpuExecutionEngine.GSharpAot);
        using var operand = GpuBigUInt.FromBigInteger(ctx, a, g.W);

        var stages = GpuBigIntDivRem.InverseSqrtDoublingStagesForTest(operand, finalWords, firstY);
        var reference = InverseSqrtDoublingReference.RunTo(a, g.W, firstY, finalWords, out var referenceLastY);

        // Every stage (width, Y and signed D) agrees bit for bit with RunTo.
        AssertStagesFollowTheModel(reference, stages, $"{kind} n={rootWords} p_K={finalWords}");
        Assert.Equal(finalWords, stages[^1].Words);
        Assert.Equal(referenceLastY, stages[^1].Y);

        // The last stage stays within 2 of floor(Y*(p_K)) for every p_K >= 3.
        var yStar = InverseSqrtDoublingReference.FloorScaledInverseSqrt(a, g.YExponent(finalWords));
        Assert.True(BigInteger.Abs(stages[^1].Y - yStar) <= 2,
            $"{kind} n={rootWords} p_K={finalWords}: Y_K - floor(Y*) = {stages[^1].Y - yStar}");
        ctx.ThrowIfDeviceErrors();
    }

    // A final width below 3 is outside the schedule's domain and throws ArgumentOutOfRangeException.
    // mutant: GpuBigIntDivRem.InverseSqrtDoubling.cs:38 `ThrowIfLessThan(finalWords, 3)` ->
    //         `ThrowIfLessThan(finalWords, 2)`
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void InverseSqrtDoublingStages_FinalWidthBelowThree_ThrowsArgumentOutOfRangeException()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        using var operand = GpuBigUInt.FromBigInteger(ctx, BuildNormalizedOperand("Random", 2), 2);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => GpuBigIntDivRem.InverseSqrtDoublingStagesForTest(operand, 2, BigInteger.One));
    }

    // A first Y of 2^64 does not fit in the top two words of y and throws ArgumentOutOfRangeException.
    // mutant: GpuBigIntDivRem.InverseSqrtDoubling.cs:87 `firstY.GetBitLength() > 64` -> `firstY.GetBitLength() > 65`
    //         (the conversion at :105 throws OverflowException instead)
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void InverseSqrtDoublingStages_FirstYAbove64Bits_ThrowsArgumentOutOfRangeException()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        using var operand = GpuBigUInt.FromBigInteger(ctx, BuildNormalizedOperand("Random", 2), 2);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => GpuBigIntDivRem.InverseSqrtDoublingStagesForTest(operand, 3, BigInteger.One << 64));
    }

    /// <summary>An operand of <c>2n</c> words for a root of <c>n</c> words.</summary>
    internal static BigInteger BuildRootOperand(string kind, int n)
    {
        var bits = 64 * n;
        var max = (BigInteger.One << (32 * n)) - 1;
        return kind switch
        {
            "AllOnes" => (BigInteger.One << bits) - 1,
            "Lowest" => BigInteger.One << (bits - 2),
            "OddLengthAllOnes" => (BigInteger.One << (bits - 1)) - 1,
            "SquareOfMax" => max * max,
            "SquareOfMaxMinusOne" => max * max - 1,
            "NegativeResidualTop" => (NegativeResidualOperand(n + 1, 2) << (32 * (n - 1)))
                | ((BigInteger.One << (32 * (n - 1))) - 1),
            "Random" => GpuBigIntSqrtTests.RandomWithBitLength(new Random(4409 + n), bits),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown root operand."),
        };
    }

    /// <summary><c>(2^(16w) - 1)^2 + delta</c>, an operand of <c>w</c> words with bit length <c>32w</c>.</summary>
    internal static BigInteger NegativeResidualOperand(int operandWords, int delta)
    {
        var r = (BigInteger.One << (16 * operandWords)) - 1;
        return r * r + delta;
    }

    /// <summary>
    /// Runs <see cref="GpuBigIntDivRem.InverseSqrtDoublingStagesForTest"/> to <c>g.FinalWords</c> and computes
    /// <c>x~ = floor(Y_K / 2^(32 p_K - E))</c>.
    /// </summary>
    internal static List<GpuBigIntDivRem.InverseSqrtDoublingStage> RunDeviceStages(GpuBigUInt operand, Geometry g,
        BigInteger firstY, out BigInteger xTilde)
    {
        var stages = GpuBigIntDivRem.InverseSqrtDoublingStagesForTest(operand, g.FinalWords, firstY);
        xTilde = g.XTilde(stages[^1].Y);
        return stages;
    }

    /// <summary>Asserts that every stage (width, <c>Y</c> and signed <c>D</c>) equals the model's.</summary>
    internal static void AssertStagesFollowTheModel(List<(int Words, BigInteger Y, BigInteger D)> reference,
        List<GpuBigIntDivRem.InverseSqrtDoublingStage> stages, string label)
    {
        Assert.Equal(reference.Count, stages.Count);
        for (var k = 0; k < stages.Count; k++)
        {
            Assert.True(
                reference[k].Words == stages[k].Words && reference[k].Y == stages[k].Y && reference[k].D == stages[k].D,
                $"{label}: stage {k} (p = {stages[k].Words}) differs from the model; D model {reference[k].D}, "
                + $"device {stages[k].D}");
        }
    }

    // A normalized operand of operandWords words (bit length 32w or 32w - 1); every kind draws the same random values
    // from the seed 1709 + 53w.
    internal static BigInteger BuildNormalizedOperand(string kind, int operandWords)
    {
        var bits = 32 * operandWords;
        var random = new Random(1709 + 53 * operandWords);
        var full = GpuBigIntSqrtTests.RandomWithBitLength(random, bits);
        var top = GpuBigIntSqrtTests.RandomWithBitLength(random, 32);
        var low = GpuBigIntSqrtTests.RandomWithBitLength(random, bits - 4);
        return kind switch
        {
            "Random" => full,
            "PowerOfFour" => BigInteger.One << (bits - 2),
            "TopBitOnly" => BigInteger.One << (bits - 1),
            "TopBitOnlyMinusOne" => (BigInteger.One << (bits - 1)) - 1,
            "AllOnes" => (BigInteger.One << bits) - 1,
            "TopWordOnly" => top << (bits - 32),
            "JustAboveLowest" => (BigInteger.One << (bits - 2)) + low,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown normalized operand."),
        };
    }
}

/// <summary>The precision doubling in CPU fallback mode.</summary>
[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Stdlib)]
public class GpuBigIntInverseSqrtDoublingCpuFallbackTests
{
    // In CPU fallback mode the switch is taken as well, and every step of the doubling runs through the BigInteger
    // branches of the host helpers. SqrtFloorCorrection then writes the exact root, so the stages are compared with the
    // model here, and Sqrt is the floor.
    // mutant: GpuBigIntDivRem.InverseSqrtDoubling.cs:172 `-(32 * residualWords + 1)` -> `-(32 * residualWords + 2)`
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(45)]
    [InlineData(200)]
    public void Sqrt_CpuFallbackWithThresholdZero_RunsTheDoublingOfTheModel(int rootWords)
    {
        using var fallback = CpuFallbackScope.Enter();
        var g = new Geometry(rootWords);
        foreach (var kind in new[] { "AllOnes", "Lowest", "Random", "NegativeResidualTop" })
        {
            var a = GpuBigIntInverseSqrtDoublingTests.BuildRootOperand(kind, rootWords);
            var top = a >> (32 * (rootWords - 1));
            var firstY = InverseSqrtDoublingReference.FirstY(top, g);
            using var ctx = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot)
            {
                SqrtPrecisionDoublingThresholdWords = 0,
            };
            using var operandTop = GpuBigUInt.FromBigInteger(ctx, top, g.W);

            var stages = GpuBigIntInverseSqrtDoublingTests.RunDeviceStages(operandTop, g, firstY, out var xTilde);
            var reference = InverseSqrtDoublingReference.Run(top, g, firstY, out var referenceX);

            // Every stage and x~ agree with the model.
            GpuBigIntInverseSqrtDoublingTests.AssertStagesFollowTheModel(reference, stages, $"{kind} n={rootWords}");
            Assert.Equal(referenceX, xTilde);

            // Sqrt through the doubling is the floor.
            using var operand = GpuBigUInt.FromBigInteger(ctx, a, 2 * rootWords);
            using var result = new GpuBigUInt(ctx, 2 * rootWords);
            GpuBigIntDivRem.Sqrt(operand, result);
            GpuBigIntSqrtTests.AssertIsFloorSquareRoot(a, result.ToBigInteger(), $"{kind} r={rootWords} (CPU)");
        }
    }
}
