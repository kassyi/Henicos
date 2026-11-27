#nullable enable
using Xunit.Abstractions;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

// Properties checked here (property-test-design §1):
// - K-P1: every doubling stage (p, Y, D) and every intermediate value (y, s0, d_hi, d', delta, r'') and the final root
//   agree bit for bit with the golden set and with the BigInteger reference model.
// - K-P2: 0 <= floor(2^P / sqrt(A)) - y <= 3 (never above the root).
// - K-P3: buffer bounds: s0 <= floor(sqrt(A)), 0 <= d_hi = A - s0^2 < 2^(16n + 21), d' and delta fit stepWords words.
// - K-P4: floor(sqrt(a)) - r'' in {0, 1}, so SqrtFloorCorrection needs at most one upward step.
// - K-P5: root == floor(sqrt(a)) and r^2 <= a < (r + 1)^2.
// - K-P6: SqrtFloor increments InverseSqrtDoublingRuns by 1 iff n > SqrtPrecisionDoublingThresholdWords.
// Every expected value is computed on the host from the operand the test holds, never from a value read back.
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Stdlib)]
public class GpuBigIntSqrtKarpMarksteinTests(ITestOutputHelper output)
{
    // K-P1: every stage, intermediate value and final root on the GPU matches the Python golden set bit for bit.
    // mutant: GpuBigIntDivRem.SqrtKarpMarkstein.cs:116 `GpuBigIntMath.Sub(y, 2u, y);` -> `GpuBigIntMath.Sub(y, 0u, y);`
    // mutant: GpuBigIntDivRem.SqrtKarpMarkstein.cs:170 `-31` -> `-32`
    // mutant: GpuBigIntDivRem.SqrtKarpMarkstein.cs:172 `BorrowWords(w.InverseWords, w.StepWords)` ->
    //         `BorrowWords(w.InverseWords - 1, w.StepWords)`
    // mutant: GpuBigIntDivRem.SqrtKarpMarkstein.cs:220 `16 * RootWords - 16` -> `16 * RootWords - 15`
    // mutant: GpuBigIntDivRem.SqrtKarpMarkstein.cs:96 `CorrectSqrtFloor(operand, root);` removed
    // mutant: GpuBigIntDivRem.SqrtKarpMarkstein.cs:214 `(RootWords + 1) / 2` -> `(RootWords + 1) / 2 - 1`
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(SqrtKarpMarksteinReference.GoldenCases), MemberType = typeof(SqrtKarpMarksteinReference))]
    public void SqrtFloorKarpMarkstein_GoldenCase_MatchesEveryValueBitForBit(int index, string label, int rootWords)
    {
        CudaCapability.EnsureGpuAvailable();
        var golden = SqrtKarpMarksteinReference.Golden[index];
        using var ctx = new GpuWorkspace(Math.Max(4096, rootWords * 16), GpuExecutionEngine.GSharpAot);
        using var operand = GpuBigUInt.FromBigInteger(ctx, golden.A, 2 * rootWords);
        using var root = new GpuBigUInt(ctx, rootWords);

        var trace = GpuBigIntDivRem.SqrtFloorKarpMarksteinForTest(operand, root);
        var actual = ToReferenceTrace(trace, root.ToBigInteger());

        SqrtKarpMarksteinReference.AssertTraceEquals(golden.Trace, actual, $"{label} n={rootWords}");
        ctx.ThrowIfDeviceErrors();
    }

    /// <summary>
    /// Structured operands for <see cref="SqrtFloorKarpMarkstein_SqrtFloorOperand_FollowsTheModelOntoTheFloor"/>.
    /// </summary>
    public static TheoryData<string, int> SqrtFloorInputs()
    {
        var data = new TheoryData<string, int>();
        foreach (var kind in GpuBigIntSqrtTests.SqrtFloorOperandKinds)
        {
            foreach (var n in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 15, 16, 17, 31, 32, 33, 46, 47, 64, 65, 127, 128 })
            {
                data.Add(kind, n);
            }
        }

        return data;
    }

    // K-P1..K-P5 across structured operands from n = 1 to n = 128.
    // mutant: GpuBigIntDivRem.SqrtKarpMarkstein.cs:205 `RootWords / 2` -> `RootWords / 2 + 1`
    // mutant: GpuBigIntDivRem.SqrtKarpMarkstein.cs:202 `Math.Max(3, RootWords / 2 + 1)` -> `Math.Max(3, RootWords / 2)`
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(SqrtFloorInputs))]
    public void SqrtFloorKarpMarkstein_SqrtFloorOperand_FollowsTheModelOntoTheFloor(string kind, int rootWords) =>
        AssertKarpMarksteinMatchesModel(kind, rootWords);

    /// <summary>Wide operands for the Stress Karp-Markstein test.</summary>
    public static TheoryData<string, int> WideSqrtFloorInputs()
    {
        var data = new TheoryData<string, int>();
        foreach (var kind in new[] { "Random", "SquareMinus1", "Highest" })
        {
            foreach (var n in new[] { 1000, 1001, 4096, 4097 })
            {
                data.Add(kind, n);
            }
        }

        return data;
    }

    // K-P1..K-P5 for wide roots (1000..4097 words), Stress category.
    [SkippableTheory]
    [Category(TestCategory.Stress)]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(WideSqrtFloorInputs))]
    public void SqrtFloorKarpMarkstein_WideSqrtFloorOperand_FollowsTheModelOntoTheFloor(string kind, int rootWords) =>
        AssertKarpMarksteinMatchesModel(kind, rootWords);

    private void AssertKarpMarksteinMatchesModel(string kind, int rootWords)
    {
        CudaCapability.EnsureGpuAvailable();
        var a = GpuBigIntSqrtTests.BuildSqrtFloorOperand(kind, rootWords);
        var expected = SqrtKarpMarksteinReference.Run(a, rootWords);
        using var ctx = new GpuWorkspace(Math.Max(4096, rootWords * 16), GpuExecutionEngine.GSharpAot);
        using var operand = GpuBigUInt.FromBigInteger(ctx, a, 2 * rootWords);
        using var root = new GpuBigUInt(ctx, rootWords);

        var trace = GpuBigIntDivRem.SqrtFloorKarpMarksteinForTest(operand, root);
        var actual = ToReferenceTrace(trace, root.ToBigInteger());
        var label = $"{kind} n={rootWords}";

        // K-P1: every stage and every value of the tail agree with the model.
        SqrtKarpMarksteinReference.AssertTraceEquals(expected, actual, label);

        // K-P2..K-P4: y bound (<= 3), buffer bounds, and floor(sqrt(a)) - r'' in {0, 1}.
        var yGap = SqrtKarpMarksteinReference.AssertTraceProperties(a, rootWords, actual, label);
        output.WriteLine($"{label}: floor(2^P / sqrt(A)) - y = {yGap}");

        // K-P5: the root is the floor and r^2 <= a < (r + 1)^2.
        GpuBigIntSqrtTests.AssertIsFloorSquareRoot(a, actual.Root, label);
        ctx.ThrowIfDeviceErrors();
    }

    /// <summary>Threshold, root word count and operand kind for testing the SqrtFloor switch.</summary>
    public static TheoryData<int, int, string> ThresholdSwitchInputs()
    {
        (int Threshold, int RootWords)[] pairs =
        [
            (0, 1), (0, 2), (0, 3),
            (7, 7), (7, 8),
            (45, 45), (45, 46),
            (int.MaxValue, 46),
        ];
        var data = new TheoryData<int, int, string>();
        foreach (var (threshold, rootWords) in pairs)
        {
            foreach (var kind in new[] { "Square", "SquareMinus1", "Highest" })
            {
                data.Add(threshold, rootWords, kind);
            }
        }

        return data;
    }

    // K-P5, K-P6: SqrtFloor gives the floor and increments InverseSqrtDoublingRuns iff n > threshold.
    // mutant: GpuBigIntDivRem.Sqrt.cs:147 `root.NumWords > workspace.SqrtPrecisionDoublingThresholdWords` -> `>=`
    // mutant: GpuBigIntDivRem.Sqrt.cs:150 `workspace.CountInverseSqrtDoubling();` removed
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ThresholdSwitchInputs))]
    public void SqrtFloor_RootWordsAroundTheThreshold_IsFloorAndCountsTheKarpMarksteinPath(int thresholdWords,
        int rootWords, string kind)
    {
        CudaCapability.EnsureGpuAvailable();
        var a = GpuBigIntSqrtTests.BuildSqrtFloorOperand(kind, rootWords);
        using var ctx = new GpuWorkspace(Math.Max(4096, rootWords * 16), GpuExecutionEngine.GSharpAot);
        ctx.SqrtPrecisionDoublingThresholdWords = thresholdWords;
        using var operand = GpuBigUInt.FromBigInteger(ctx, a, 2 * rootWords);
        using var root = new GpuBigUInt(ctx, rootWords);
        var runsBefore = ctx.InverseSqrtDoublingRuns;

        GpuBigIntDivRem.SqrtFloor(operand, root);
        var actualRoot = root.ToBigInteger();

        // K-P5: r^2 <= a < (r + 1)^2.
        GpuBigIntSqrtTests.AssertIsFloorSquareRoot(a, actualRoot, $"{kind} n={rootWords} threshold={thresholdWords}");
        // K-P6: the Karp-Markstein path is counted iff n > thresholdWords.
        Assert.Equal(rootWords > thresholdWords ? 1 : 0, ctx.InverseSqrtDoublingRuns - runsBefore);
        ctx.ThrowIfDeviceErrors();
    }

    // An operand whose word count is not 2 * root.NumWords (narrower or wider) throws ArgumentException.
    // mutant: GpuBigIntDivRem.Sqrt.cs:179 `operand.NumWords != 2 * n` -> `operand.NumWords < 2 * n` (fails on 9 vs 4)
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(7, 4)]
    [InlineData(9, 4)]
    public void SqrtFloorKarpMarkstein_OperandNotTwiceTheRoot_ThrowsArgumentException(int operandWords, int rootWords)
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        var value = (BigInteger.One << (32 * operandWords)) - 1;
        using var operand = GpuBigUInt.FromBigInteger(ctx, value, operandWords);
        using var root = new GpuBigUInt(ctx, rootWords);

        var ex = Assert.Throws<ArgumentException>(() => GpuBigIntDivRem.SqrtFloorKarpMarksteinForTest(operand, root));
        Assert.Contains("SqrtFloor needs an operand of twice the root words", ex.Message, StringComparison.Ordinal);
    }

    // An operand with bit length below 64n - 1 violates the normalization precondition and throws ArgumentException.
    // mutant: GpuBigIntDivRem.SqrtKarpMarkstein.cs:104 `actualBits < 32 * w.OperandWords - 1` ->
    //         `actualBits < 32 * w.OperandWords - 2`
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void SqrtFloorKarpMarkstein_SubnormalOperand_ThrowsArgumentException()
    {
        CudaCapability.EnsureGpuAvailable();
        const int RootWords = 4;
        var subnormal = (BigInteger.One << (64 * RootWords - 2)) - 1;
        using var ctx = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        using var operand = GpuBigUInt.FromBigInteger(ctx, subnormal, 2 * RootWords);
        using var root = new GpuBigUInt(ctx, RootWords);

        var ex = Assert.Throws<ArgumentException>(() => GpuBigIntDivRem.SqrtFloorKarpMarksteinForTest(operand, root));
        Assert.Contains("normalized operand", ex.Message, StringComparison.Ordinal);
    }

    // An overestimated y makes s0 exceed sqrt(A) on a perfect square, so d_hi = A - s0^2 < 0 is reported by the
    // checked subtraction as an OverflowException (in Debug, CheckFitsBits also flags the wrapped residual, so two
    // domain errors are recorded; mutating AddSubtract to AddSubtractModulo drops the count to 1 in Debug and removes
    // the exception in Release).
    // mutant: GpuBigIntDivRem.SqrtKarpMarkstein.cs:147 `BigIntWideOps.AddSubtract(` ->
    //         `BigIntWideOps.AddSubtractModulo(`
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void SqrtFloorKarpMarkstein_OverestimatedYOnPerfectSquare_ReportsDifferenceBelowZero()
    {
        CudaCapability.EnsureGpuAvailable();
        const int RootWords = 4;
        // a = 2^(64n - 2) = (2^(32n - 1))^2, so A = (2^(16n + 15))^2; adding 2^16 + 4 to y pushes s0 to sqrt(A) + 1.
        var a = BigInteger.One << (64 * RootWords - 2);
        using var ctx = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);
        using var operand = GpuBigUInt.FromBigInteger(ctx, a, 2 * RootWords);
        using var root = new GpuBigUInt(ctx, RootWords);

        var ex = Assert.Throws<OverflowException>(
            () => GpuBigIntDivRem.SqrtFloorKarpMarksteinForTest(operand, root, yOffset: (1u << 16) + 4u));
#if DEBUG
        Assert.Contains("2 domain error(s)", ex.Message, StringComparison.Ordinal);
#else
        Assert.Contains("below zero", ex.Message, StringComparison.OrdinalIgnoreCase);
#endif
        ctx.ThrowIfDeviceErrors();
    }

    internal static SqrtKarpMarksteinReference.Trace ToReferenceTrace(
        GpuBigIntDivRem.SqrtKarpMarksteinTrace trace, BigInteger actualRoot)
    {
        var stages = new List<(int Words, BigInteger Y, BigInteger D)>(trace.Stages.Count);
        foreach (var stage in trace.Stages)
        {
            stages.Add((stage.Words, stage.Y, stage.D));
        }

        return new SqrtKarpMarksteinReference.Trace(
            stages, trace.Y, trace.S0, trace.DHigh, trace.DShifted, trace.Delta, trace.Estimate, actualRoot);
    }
}

/// <summary>The Karp-Markstein square root in CPU fallback mode.</summary>
[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Stdlib)]
public class GpuBigIntSqrtKarpMarksteinCpuFallbackTests
{
    /// <summary>Root word counts and operand kinds for the CPU fallback Karp-Markstein test.</summary>
    public static TheoryData<int, string> CpuFallbackInputs()
    {
        var data = new TheoryData<int, string>();
        foreach (var rootWords in new[] { 1, 2, 7, 46, 200 })
        {
            foreach (var kind in new[] { "Lowest", "Highest", "Random", "SquareMinus1" })
            {
                data.Add(rootWords, kind);
            }
        }

        return data;
    }

    // K-P1, K-P5 in CPU fallback mode: every stage, intermediate value and final root agrees with the model.
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(CpuFallbackInputs))]
    public void SqrtFloorKarpMarkstein_CpuFallback_MatchesTheModel(int rootWords, string kind)
    {
        using var fallback = CpuFallbackScope.Enter();
        var a = GpuBigIntSqrtTests.BuildSqrtFloorOperand(kind, rootWords);
        var expected = SqrtKarpMarksteinReference.Run(a, rootWords);
        using var ctx = new GpuWorkspace(Math.Max(4096, rootWords * 16), GpuExecutionEngine.GSharpAot);
        using var operand = GpuBigUInt.FromBigInteger(ctx, a, 2 * rootWords);
        using var root = new GpuBigUInt(ctx, rootWords);

        var trace = GpuBigIntDivRem.SqrtFloorKarpMarksteinForTest(operand, root);
        var actual = GpuBigIntSqrtKarpMarksteinTests.ToReferenceTrace(trace, root.ToBigInteger());
        var label = $"{kind} n={rootWords} (CPU)";

        SqrtKarpMarksteinReference.AssertTraceEquals(expected, actual, label);
        GpuBigIntSqrtTests.AssertIsFloorSquareRoot(a, actual.Root, label);
    }
}
