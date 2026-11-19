#nullable enable
using System;
using System.Linq;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// The gotos to chained labels that the CPU state machine lowers run as on the GPU: one warp of 32 lanes, each lane
/// running the loop twice, so the full-mask shuffle is reached by every lane together.
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class ChainedLabelGotoDifferentialTests
{
    private const int Lanes = 32;

    private static void RunOnEveryPath(Action<int[]> launch, Func<int, int> expectedOfLane)
    {
        var expected = Enumerable.Range(0, Lanes).Select(expectedOfLane).ToArray();
        foreach (var path in DiffPaths.Available)
        {
            path.Preflight?.Invoke();
            var output = new int[Lanes];
            path.Run(() => launch(output));
            Assert.True(expected.SequenceEqual(output),
                $"{path.Name}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", output)}]");
        }
    }

    // Twice: v = (t ^ 1) + 1 after the first shuffle, then t + 2 after the second
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void SplitShuffleBackInner_GotoToLabelWithOwnStatement_ShufflesTwice() =>
        RunOnEveryPath(
            static output => ChainedLabelGotoDifferentialKernels.LaunchSplitShuffleBackInnerKernel(
                output, blockDimX: Lanes),
            static t => t + 2);

    // The first label names the same statement as the inner one
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ChainedShuffleBackOuter_GotoToFirstLabel_ShufflesTwice() =>
        RunOnEveryPath(
            static output => ChainedLabelGotoDifferentialKernels.LaunchChainedShuffleBackOuterKernel(
                output, blockDimX: Lanes),
            static t => t + 2);

    // The loop stays before the shuffle: two increments, then one shuffle
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ChainedSameStateInner_GotoInsideOneState_ShufflesOnce() =>
        RunOnEveryPath(
            static output => ChainedLabelGotoDifferentialKernels.LaunchChainedSameStateInnerKernel(
                output, blockDimX: Lanes),
            static t => (t ^ 1) + 2);
}
