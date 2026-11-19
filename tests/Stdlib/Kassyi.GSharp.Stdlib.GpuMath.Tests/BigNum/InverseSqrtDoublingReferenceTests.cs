#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// The schedule of the precision-doubling inverse square root and the BigInteger model the GPU tests compare with,
/// both against the reference model.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Stdlib)]
public class InverseSqrtDoublingReferenceTests
{
    /// <summary>Final widths with their stages as the reference lists them.</summary>
    public static TheoryData<int, int[]> ScheduleExamples() => new()
    {
        { 3, [2, 3] },
        { 4, [2, 3, 4] },
        { 9, [2, 3, 4, 6, 9] },
        { 1002, [2, 3, 4, 6, 10, 18, 34, 65, 127, 252, 502, 1002] },
    };

    // The product schedule writes the stages the reference lists: each p_(k-1) = ceil(p_k / 2) + 1 down to 2, first
    // stage first. A stage too narrow leaves the next one more than twice as wide, beyond what the error bound allows.
    // mutant: GpuBigIntDivRem.InverseSqrtDoubling.cs:57 `words - words / 2 + 1` -> `words - words / 2` (4, 9, 1002)
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ScheduleExamples))]
    public void InverseSqrtDoublingSchedule_ReferenceExample_MatchesTheListedStages(int finalWords, int[] expected)
    {
        Span<int> stages = stackalloc int[GpuBigIntDivRem.InverseSqrtDoublingMaxStages];
        var count = GpuBigIntDivRem.InverseSqrtDoublingSchedule(finalWords, stages);

        Assert.Equal(expected, stages[..count].ToArray());
    }

    // Every final width from 3 to 5000 words gives the stages of the reference port, which follows the Python model
    // line by line.
    // mutant: GpuBigIntDivRem.InverseSqrtDoubling.cs:57 `words - words / 2 + 1` -> `words - words / 2` (from 4 words)
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void InverseSqrtDoublingSchedule_EveryFinalWidthUpTo5000_MatchesReferencePort()
    {
        Span<int> stages = stackalloc int[GpuBigIntDivRem.InverseSqrtDoublingMaxStages];
        for (var finalWords = 3; finalWords <= 5000; finalWords++)
        {
            var count = GpuBigIntDivRem.InverseSqrtDoublingSchedule(finalWords, stages);
            Assert.Equal(InverseSqrtDoublingReference.Schedule(finalWords).ToArray(), stages[..count].ToArray());
        }
    }

    // The widest final width has 33 stages, which fit the stack buffer of the product; it starts at 2 words and ends
    // at the full width without overflowing ceil(p / 2) + 1.
    // mutant: GpuBigIntDivRem.InverseSqrtDoubling.cs:13 `40` -> `32` (ArgumentException: the schedule does not fit)
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void InverseSqrtDoublingSchedule_LargestWidth_FitsTheStageBuffer()
    {
        Span<int> stages = stackalloc int[GpuBigIntDivRem.InverseSqrtDoublingMaxStages];
        var count = GpuBigIntDivRem.InverseSqrtDoublingSchedule(int.MaxValue, stages);

        Assert.Equal(33, count);
        Assert.True(count <= GpuBigIntDivRem.InverseSqrtDoublingMaxStages);
        Assert.Equal(2, stages[0]);
        Assert.Equal(int.MaxValue, stages[count - 1]);
    }

    // The schedule needs a final stage of at least 3 words (an operand of at least 2 words).
    // mutant: GpuBigIntDivRem.InverseSqrtDoubling.cs:38 `ThrowIfLessThan(finalWords, 3)` removed
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void InverseSqrtDoublingSchedule_FinalWidthBelowThree_ThrowsArgumentOutOfRangeException()
    {
        var stages = new int[GpuBigIntDivRem.InverseSqrtDoublingMaxStages];

        Assert.Throws<ArgumentOutOfRangeException>(() => GpuBigIntDivRem.InverseSqrtDoublingSchedule(2, stages));
    }

    // The C# model reproduces the Python reference bit for bit: the initial guess, every stage and x~. The GPU tests
    // compare the product with this model, so the model itself is pinned to the golden values here.
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(InverseSqrtDoublingReference.GoldenCases), MemberType = typeof(InverseSqrtDoublingReference))]
    public void InverseSqrtDoublingReference_GoldenCase_MatchesThePythonModel(int index, string label, int rootWords)
    {
        var golden = InverseSqrtDoublingReference.Golden[index];
        var g = new InverseSqrtDoublingReference.Geometry(rootWords);

        // The margin of the golden case is the one SqrtFloor uses for this root.
        Assert.Equal(golden.MarginWords, g.M);
        // The initial guess is the one ComputeInitialGuess writes for targetBits = B.
        Assert.Equal(golden.X0, InverseSqrtDoublingReference.InitialGuess(golden.A, g.B));

        var stages = InverseSqrtDoublingReference.Run(golden.A, g, InverseSqrtDoublingReference.FirstY(golden.A, g),
            out var xTilde);

        // Every stage, its width and its Y, then the final x~.
        Assert.Equal(golden.Stages.Count, stages.Count);
        for (var k = 0; k < stages.Count; k++)
        {
            Assert.True(golden.Stages[k].Words == stages[k].Words && golden.Stages[k].Y == stages[k].Y,
                $"{label} n={rootWords}: stage {k} (p = {stages[k].Words}) differs from the golden value");
        }

        Assert.Equal(golden.XTilde, xTilde);
    }
}
