#nullable enable
using Xunit.Abstractions;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

// Properties checked here (property-test-design §1):
// - K-P1: every doubling stage (p, Y, D) and every intermediate value (y, s0, d_hi, d', delta, r'') and the final root
//   agree bit for bit with the golden set and with the BigInteger reference model.
// - K-P2: 0 <= floor(2^P / sqrt(A)) - y <= ApproximateInverseSqrtMaxError (never above the root).
// - K-P3: buffer bounds: s0 <= floor(sqrt(A)), 0 <= d_hi = A - s0^2 < 2^(16n + 21), d' and delta fit stepWords words.
// - K-P4: floor(sqrt(a)) - r'' in {0, 1}, so SqrtFloorCorrection needs at most one upward step.
// - K-P5: root == floor(sqrt(a)) and r^2 <= a < (r + 1)^2.
// - K-P6: SqrtFloor increments InverseSqrtDoublingRuns by 1 iff n > SqrtPrecisionDoublingThresholdWords.
// Every expected value is computed on the host from the operand the test holds, never from a value read back.
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Stdlib)]
public class GpuBigIntSqrtKarpMarksteinTests(ITestOutputHelper output)
{
    private static readonly string[] s_sqrtFloorKinds =
    [
        "TopSquareMaxLow", "Random", "RandomOddLength", "Square", "SquareMinus1", "SquarePlus2R", "Lowest", "Highest",
    ];

    // K-P1: every stage, intermediate value and final root on the GPU matches the Python golden set bit for bit.
    // mutant: GpuBigIntDivRem.SqrtKarpMarkstein.cs:89 `GpuBigIntMath.Sub(y, 2u, y);` -> `GpuBigIntMath.Sub(y, 0u, y);`
    // mutant: GpuBigIntDivRem.SqrtKarpMarkstein.cs:109 `-31` -> `-32`
    // mutant: GpuBigIntDivRem.SqrtKarpMarkstein.cs:112 `BorrowWords(inverseWords, stepWords)` ->
    //         `BorrowWords(inverseWords - 1, stepWords)`
    // mutant: GpuBigIntDivRem.SqrtKarpMarkstein.cs:115 `16 * n - 16` -> `16 * n - 15`
    // mutant: GpuBigIntDivRem.SqrtKarpMarkstein.cs:128 `CorrectSqrtFloor(operand, root);` removed
    // mutant: GpuBigIntDivRem.SqrtKarpMarkstein.cs:80 `(n + 1) / 2` -> `(n + 1) / 2 - 1`
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
        var actualRoot = root.ToBigInteger();
        var expected = golden.Trace;

        AssertTraceEquals(expected, trace, actualRoot, $"{label} n={rootWords}");
        ctx.ThrowIfDeviceErrors();
    }

    /// <summary>Structured operands for <see cref="SqrtFloorKarpMarkstein_SqrtFloorOperand_FollowsTheModelOntoTheFloor"/>.</summary>
    public static TheoryData<string, int> SqrtFloorInputs()
    {
        var data = new TheoryData<string, int>();
        foreach (var kind in s_sqrtFloorKinds)
        {
            foreach (var n in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 15, 16, 17, 31, 32, 33, 46, 47, 64, 65, 127, 128 })
            {
                data.Add(kind, n);
            }
        }

        return data;
    }

    // K-P1..K-P5 across structured operands from n = 1 to n = 128.
    // mutant: GpuBigIntDivRem.SqrtKarpMarkstein.cs:77 `n / 2` -> `n / 2 + 1`
    // mutant: GpuBigIntDivRem.SqrtKarpMarkstein.cs:76 `Math.Max(3, n / 2 + 1)` -> `Math.Max(3, n / 2)`
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
        var g = new SqrtKarpMarksteinReference.Widths(rootWords);
        var top = a >> (32 * (rootWords - 1));
        using var ctx = new GpuWorkspace(Math.Max(4096, rootWords * 16), GpuExecutionEngine.GSharpAot);
        using var operand = GpuBigUInt.FromBigInteger(ctx, a, 2 * rootWords);
        using var root = new GpuBigUInt(ctx, rootWords);

        var trace = GpuBigIntDivRem.SqrtFloorKarpMarksteinForTest(operand, root);
        var actualRoot = root.ToBigInteger();
        var label = $"{kind} n={rootWords}";

        // K-P1: every stage and every value of the tail agree with the model.
        AssertTraceEquals(expected, trace, actualRoot, label);

        // K-P2: y stays at most ApproximateInverseSqrtMaxError below floor(2^P / sqrt(A)), never above.
        var yFloor = InverseSqrtDoublingReference.FloorScaledInverseSqrt(top, g.P);
        var yGap = yFloor - trace.Y;
        output.WriteLine($"{label}: floor(2^P / sqrt(A)) - y = {yGap}");
        Assert.True(yGap >= 0 && yGap <= GpuBigIntDivRem.ApproximateInverseSqrtMaxError,
            $"{label}: floor(2^P / sqrt(A)) - y = {yGap}");

        // K-P3: the bounds that size the buffers.
        var sqrtTop = GpuBigIntSqrtTests.FloorSqrt(top);
        Assert.True(trace.S0 <= sqrtTop, $"{label}: s0 exceeds floor(sqrt(A))");
        Assert.Equal(top - trace.S0 * trace.S0, trace.DHigh);
        Assert.True(trace.DHigh.Sign >= 0 && trace.DHigh < (BigInteger.One << (16 * rootWords + 21)),
            $"{label}: d_hi out of [0, 2^(16n + 21))");
        var stepBound = BigInteger.One << (32 * g.StepWords);
        Assert.True(trace.DShifted.Sign >= 0 && trace.DShifted < stepBound, $"{label}: d' out of stepWords");
        Assert.True(trace.Delta.Sign >= 0 && trace.Delta < stepBound, $"{label}: delta out of stepWords");

        // K-P4: floor(sqrt(a)) - r'' in {0, 1}.
        var gap = expected.Root - trace.Estimate;
        Assert.True(gap >= 0 && gap <= 1, $"{label}: floor(sqrt(a)) - r'' = {gap}");

        // K-P5: the root is the floor and r^2 <= a < (r + 1)^2.
        GpuBigIntSqrtTests.AssertIsFloorSquareRoot(a, actualRoot, label);
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
    // mutant: GpuBigIntDivRem.Sqrt.cs:147 `n > workspace.SqrtPrecisionDoublingThresholdWords` -> `>=`
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

    // An operand whose word count is not 2 * root.NumWords throws ArgumentException.
    // mutant: GpuBigIntDivRem.Sqrt.cs:172 `operand.NumWords != 2 * n` -> `operand.NumWords < 2 * n`
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void SqrtFloorKarpMarkstein_OperandNotTwiceTheRoot_ThrowsArgumentException()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        using var operand = new GpuBigUInt(ctx, 7);
        using var root = new GpuBigUInt(ctx, 4);

        Assert.Throws<ArgumentException>(() => GpuBigIntDivRem.SqrtFloorKarpMarksteinForTest(operand, root));
    }

    internal static void AssertTraceEquals(SqrtKarpMarksteinReference.Trace expected,
        GpuBigIntDivRem.SqrtKarpMarksteinTrace actual, BigInteger actualRoot, string label)
    {
        Assert.Equal(expected.Stages.Count, actual.Stages.Count);
        for (var k = 0; k < expected.Stages.Count; k++)
        {
            Assert.True(
                expected.Stages[k].Words == actual.Stages[k].Words
                && expected.Stages[k].Y == actual.Stages[k].Y
                && expected.Stages[k].D == actual.Stages[k].D,
                $"{label}: stage {k} (p = {actual.Stages[k].Words}) differs from the model");
        }

        Assert.Equal(expected.Y, actual.Y);
        Assert.Equal(expected.S0, actual.S0);
        Assert.Equal(expected.DHigh, actual.DHigh);
        Assert.Equal(expected.DShifted, actual.DShifted);
        Assert.Equal(expected.Delta, actual.Delta);
        Assert.Equal(expected.Estimate, actual.Estimate);
        Assert.Equal(expected.Root, actualRoot);
    }
}

/// <summary>The Karp-Markstein square root in CPU fallback mode.</summary>
[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Stdlib)]
public class GpuBigIntSqrtKarpMarksteinCpuFallbackTests
{
    // K-P1, K-P5 in CPU fallback mode: every stage, intermediate value and final root agrees with the model.
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(46)]
    [InlineData(200)]
    public void SqrtFloorKarpMarkstein_CpuFallback_MatchesTheModel(int rootWords)
    {
        using var fallback = CpuFallbackScope.Enter();
        foreach (var kind in new[] { "Lowest", "Highest", "Random", "SquareMinus1" })
        {
            var a = GpuBigIntSqrtTests.BuildSqrtFloorOperand(kind, rootWords);
            var expected = SqrtKarpMarksteinReference.Run(a, rootWords);
            using var ctx = new GpuWorkspace(Math.Max(4096, rootWords * 16), GpuExecutionEngine.GSharpAot);
            using var operand = GpuBigUInt.FromBigInteger(ctx, a, 2 * rootWords);
            using var root = new GpuBigUInt(ctx, rootWords);

            var trace = GpuBigIntDivRem.SqrtFloorKarpMarksteinForTest(operand, root);
            var actualRoot = root.ToBigInteger();
            var label = $"{kind} n={rootWords} (CPU)";

            GpuBigIntSqrtKarpMarksteinTests.AssertTraceEquals(expected, trace, actualRoot, label);
            GpuBigIntSqrtTests.AssertIsFloorSquareRoot(a, actualRoot, label);
        }
    }
}
