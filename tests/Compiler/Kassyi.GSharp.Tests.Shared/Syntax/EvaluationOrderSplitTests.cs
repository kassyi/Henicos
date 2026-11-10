using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Execution of <see cref="EvaluationOrderSplitKernels"/>: the left operand of a binary expression runs before a split
/// in its right operand, as C# evaluates it, so the value the split reads is the one the left operand wrote.
/// </summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class EvaluationOrderSplitTests : TranspilerTestBase
{
    internal const int BlockSize = 32;

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_AssignmentBeforeShuffle_ShuffleReadsTheAssignedValue() => AssertAssignmentBeforeShuffle();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_LocalAssignmentBeforeShuffle_ShuffleReadsTheAssignedValue() =>
        AssertLocalAssignmentBeforeShuffle();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_CallBeforeShuffle_ShuffleReadsWhatTheCallWrote() => AssertCallBeforeShuffle();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_AssignmentBeforeSuspendingCall_CallReadsTheAssignedValue() =>
        AssertAssignmentBeforeSuspendingCall();

    // C#: lane + 100 written, then lane 0's a[0] = 100 read by every lane
    internal static void AssertAssignmentBeforeShuffle()
    {
        var a = new int[BlockSize];
        var result = new int[BlockSize];
        EvaluationOrderSplitKernels.LaunchAssignmentBeforeShuffleKernel(a, result, blockDimX: BlockSize);
        AssertEach(result, static lane => lane + 200);
    }

    internal static void AssertLocalAssignmentBeforeShuffle()
    {
        var result = new int[BlockSize];
        EvaluationOrderSplitKernels.LaunchLocalAssignmentBeforeShuffleKernel(result, blockDimX: BlockSize);
        AssertEach(result, static lane => lane + 200);
    }

    // C#: Bump writes a[lane] = 7 and returns 1, then the shuffle reads lane 0's a[0] = 7
    internal static void AssertCallBeforeShuffle()
    {
        var a = new int[BlockSize];
        var result = new int[BlockSize];
        EvaluationOrderSplitKernels.LaunchCallBeforeShuffleKernel(a, result, blockDimX: BlockSize);
        AssertEach(result, static _ => 8);
    }

    // C#: every lane writes a[lane] = lane + 100 before the barrier inside the call, then reads a[0] = 100
    internal static void AssertAssignmentBeforeSuspendingCall()
    {
        var a = new int[BlockSize];
        var result = new int[BlockSize];
        EvaluationOrderSplitKernels.LaunchAssignmentBeforeSuspendingCallKernel(a, result, blockDimX: BlockSize);
        AssertEach(result, static lane => lane + 200);
    }

    private static void AssertEach(int[] result, Func<int, int> expected)
    {
        for (int lane = 0; lane < BlockSize; lane++)
        {
            Assert.Equal(expected(lane), result[lane]);
        }
    }
}
