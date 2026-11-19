#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

// Properties checked here (property-test-design §1), for the switch between the mega-kernel and the precision doubling
// of the inverse square root:
// - Sqrt is the floor on both sides of the threshold, and a root takes the doubling exactly when it is wider than
//   GpuWorkspace.SqrtPrecisionDoublingThresholdWords, also at the default of a new workspace (45 root words).
// - Only the operand of SqrtFloor (bit length 32w or 32w - 1, its margin) takes the doubling.
// - The threshold rejects a negative value and starts at 45.
// The path is read from GpuWorkspace.InverseSqrtDoublingRuns; every expected value is computed on the host.
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Stdlib)]
public class GpuBigIntSqrtDoublingThresholdTests
{
    private static readonly string[] s_rootOperandKinds =
    [
        "AllOnes", "Lowest", "OddLengthAllOnes", "SquareOfMax", "SquareOfMaxMinusOne", "NegativeResidualTop", "Random",
    ];

    /// <summary>
    /// Sqrt around the threshold: the threshold in root words, the root words <c>r</c> of an operand of <c>2r</c>
    /// words, and the kind of operand.
    /// </summary>
    public static TheoryData<int, int, string> ThresholdInputs()
    {
        (int Threshold, int RootWords)[] pairs =
        [
            (7, 6), (7, 7), (7, 8), (0, 1), (0, 2), (0, 3), (0, 33), (int.MaxValue, 1), (int.MaxValue, 33),
        ];
        var data = new TheoryData<int, int, string>();
        foreach (var (threshold, rootWords) in pairs)
        {
            foreach (var kind in new[] { "Random", "AllOnes", "Square", "SquareMinusOne" })
            {
                data.Add(threshold, rootWords, kind);
            }
        }

        return data;
    }

    // Sqrt is the floor on both sides of the threshold, and a root of r words takes the doubling exactly when r is
    // above the threshold (7: at 6 and 7 the mega-kernel, at 8 the doubling; 0: always; int.MaxValue: never).
    // mutant: GpuBigIntDivRem.InverseSqrtDoubling.cs:69 `>` -> `>=` (threshold 7, root 7 counts a run)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ThresholdInputs))]
    public void Sqrt_RootWordsAroundDoublingThreshold_IsFloorAndTakesThePathOfItsSize(int thresholdWords,
        int rootWords, string kind)
    {
        CudaCapability.EnsureGpuAvailable();
        var a = GpuBigIntSqrtTests.BuildSqrtOperand(kind, 2 * rootWords);
        using var ctx = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);
        ctx.SqrtPrecisionDoublingThresholdWords = thresholdWords;
        using var operand = GpuBigUInt.FromBigInteger(ctx, a, 2 * rootWords);
        using var result = new GpuBigUInt(ctx, 2 * rootWords);
        var runsBefore = ctx.InverseSqrtDoublingRuns;

        GpuBigIntDivRem.Sqrt(operand, result);

        GpuBigIntSqrtTests.AssertIsFloorSquareRoot(a, result.ToBigInteger(),
            $"{kind} r={rootWords} threshold={thresholdWords}");
        // The path follows the size of the root: one doubling run above the threshold, none at or below it.
        Assert.Equal(rootWords > thresholdWords ? 1 : 0, ctx.InverseSqrtDoublingRuns - runsBefore);
    }

    /// <summary>The roots of exactly the default threshold and one word more, with the kind of operand.</summary>
    public static TheoryData<int, string> DefaultThresholdInputs()
    {
        var data = new TheoryData<int, string>();
        foreach (var rootWords in new[] { 45, 46 })
        {
            foreach (var kind in s_rootOperandKinds)
            {
                data.Add(rootWords, kind);
            }
        }

        return data;
    }

    // On a workspace nobody configured (default 45 root words), a root of 45 words keeps the mega-kernel and a root of
    // 46 words takes the doubling, and both give the floor for the operands at the ends of the range: bit length 64r
    // and 64r - 1, all ones, the lowest value, the square of the largest root and one below it, and a negative D in a
    // wide stage.
    // mutant: GpuWorkspace.cs:145 `} = 45;` -> `} = 44;` (45 counts a doubling run)
    // mutant: GpuBigIntDivRem.InverseSqrtDoubling.cs:69 `>` -> `>=` (45 counts a doubling run)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(DefaultThresholdInputs))]
    public void Sqrt_DefaultThresholdAt45And46RootWords_IsFloorAndSwitchesPath(int rootWords, string kind)
    {
        CudaCapability.EnsureGpuAvailable();
        var a = GpuBigIntInverseSqrtDoublingTests.BuildRootOperand(kind, rootWords);
        using var ctx = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);
        using var operand = GpuBigUInt.FromBigInteger(ctx, a, 2 * rootWords);
        using var result = new GpuBigUInt(ctx, 2 * rootWords);
        var runsBefore = ctx.InverseSqrtDoublingRuns;

        GpuBigIntDivRem.Sqrt(operand, result);

        GpuBigIntSqrtTests.AssertIsFloorSquareRoot(a, result.ToBigInteger(), $"{kind} r={rootWords}");
        // The path follows the default: one doubling run above 45 root words, none at 45.
        Assert.Equal(rootWords > 45 ? 1 : 0, ctx.InverseSqrtDoublingRuns - runsBefore);
        ctx.ThrowIfDeviceErrors();
    }

    // The doubling is derived for the operand of SqrtFloor only. With the threshold at 0, a margin one word wider and a
    // bit length below 32n - 1 keep the mega-kernel.
    // mutant: GpuBigIntDivRem.InverseSqrtDoubling.cs:70 `&& marginWords == SqrtFloorMarginWords(...)` removed
    //         (WiderMargin counts a run)
    // mutant: GpuBigIntDivRem.InverseSqrtDoubling.cs:71 `&& actualBits >= 32 * operandWords - 1` removed
    //         (ShorterBitLength counts a run)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("WiderMargin")]
    [InlineData("ShorterBitLength")]
    public void ApproximateInverseSqrt_OperandOutsideTheDoublingDerivation_KeepsTheMegaKernel(string condition)
    {
        CudaCapability.EnsureGpuAvailable();
        const int OperandWords = 8;
        var marginWords = GpuBigIntDivRem.SqrtFloorMarginWords(OperandWords - 1) + (condition == "WiderMargin" ? 1 : 0);
        var a = condition switch
        {
            "WiderMargin" => GpuBigIntInverseSqrtDoublingTests.BuildNormalizedOperand("Random", OperandWords),
            "ShorterBitLength" => GpuBigIntSqrtTests.RandomWithBitLength(new Random(1709 + 53 * OperandWords),
                32 * OperandWords - 2),
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "Unknown condition."),
        };
        using var ctx = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);
        ctx.SqrtPrecisionDoublingThresholdWords = 0;
        using var operand = GpuBigUInt.FromBigInteger(ctx, a, OperandWords);
        using var inverse = new GpuBigUInt(ctx, OperandWords + marginWords);
        var runsBefore = ctx.InverseSqrtDoublingRuns;

        GpuBigIntDivRem.ApproximateInverseSqrt(operand, inverse, marginWords);

        // Outside the derivation the doubling does not run.
        Assert.Equal(runsBefore, ctx.InverseSqrtDoublingRuns);
        if (condition == "WiderMargin")
        {
            // The mega-kernel keeps its contract: at most ApproximateInverseSqrtMaxError below the floor, never above.
            // The shorter bit length is outside the normalized operand of the contract, so its value is not checked.
            var floor = InverseSqrtDoublingReference.FloorScaledInverseSqrt(a, 32 * (OperandWords + marginWords));
            var below = floor - inverse.ToBigInteger();
            Assert.True(below >= 0 && below <= GpuBigIntDivRem.ApproximateInverseSqrtMaxError,
                $"WiderMargin: floor(2^B / sqrt(a)) - x = {below}");
        }
    }

    // The threshold is a word count: a negative value is rejected and the previous value stays.
    // mutant: GpuWorkspace.cs:142 `ArgumentOutOfRangeException.ThrowIfNegative(value)` removed
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void SqrtPrecisionDoublingThresholdWords_NegativeValue_ThrowsAndKeepsTheValue()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        var before = ctx.SqrtPrecisionDoublingThresholdWords;

        Assert.Throws<ArgumentOutOfRangeException>(() => { ctx.SqrtPrecisionDoublingThresholdWords = -1; });
        Assert.Equal(before, ctx.SqrtPrecisionDoublingThresholdWords);
    }

    // A new workspace starts at the documented default of 45 root words, the largest root whose mega-kernel transforms
    // fit 512 points.
    // mutant: GpuWorkspace.cs:145 `} = 45;` -> `} = 44;`
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void SqrtPrecisionDoublingThresholdWords_NewWorkspace_Is45()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);

        Assert.Equal(45, ctx.SqrtPrecisionDoublingThresholdWords);
    }
}
