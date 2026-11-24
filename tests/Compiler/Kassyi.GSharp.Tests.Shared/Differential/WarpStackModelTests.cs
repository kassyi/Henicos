#nullable enable
using System;
using System.Linq;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// The lanes of a warp meet at the instance of a collective that each reaches on the same static call path for the
/// same time, whichever model the CPU paths run: the visits add up over every call through the same call site and over
/// the copies of a <see cref="Kassyi.GSharp.Core.Gpu.ForUnroll"/> body, and every block counts from 0. The expected
/// values follow that rule; a GPU that disagrees shares the root of the shuffles that lanes reach a different number of
/// times.
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
public partial class WarpStackModelTests
{
    // Runs the launch on every path of this build and compares the output with the expected values
    private static void RunOnEveryPath(int outputLength, Action<int[]> launch, int[] expected)
    {
        foreach (var path in DiffPaths.Available)
        {
            path.Preflight?.Invoke();
            var output = new int[outputLength];
            path.Run(() => launch(output));
            Assert.True(expected.SequenceEqual(output),
                $"{path.Name}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", output)}]");
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void SkewedCalls_SameCallSiteInDifferentIterations_MeetAtTheirFirstVisit()
    {
        int[] input = [10, 20];
        RunOnEveryPath(2,
            output => WarpStackModelKernels.LaunchSkewedCallsKernel(input, output, blockDimX: 2),
            [30, 30]);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ForUnrollSkewedShuffle_CopiesOfOneBody_MeetAtTheirFirstVisit()
    {
        int[] input = [10, 20];
        RunOnEveryPath(2,
            output => WarpStackModelKernels.LaunchForUnrollSkewedShuffleKernel(input, output, blockDimX: 2),
            [30, 30]);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void CallSiteFrames_SameFunctionFromTwoCallSites_CountsEachCallSiteApart()
    {
        int[] input = [10, 20];
        // Lane 0: 10 + 1, then reads lane 1 (20); lane 1 reads lane 0 (11)
        RunOnEveryPath(2,
            output => WarpStackModelKernels.LaunchCallSiteFramesKernel(input, output, blockDimX: 2),
            [20, 11]);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void StaleVisits_TwoBlocks_EachBlockCountsFromZero()
    {
        int[] input = [10, 20, 30, 40];
        // Block 0: lane 0 reads lane 1 (21) and then itself (22), lane 1 reads lane 0 (11); block 1 reads across once
        RunOnEveryPath(4,
            output => WarpStackModelKernels.LaunchStaleVisitsKernel(input, output, gridDimX: 2, blockDimX: 2),
            [22, 11, 41, 31]);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void EndInsideBranch_LaneOfFirstBlockEndsInBranch_SecondBlockMeetsWithoutIt()
    {
        // Per block: the masks inside the branch, then the masks after it. Block 0: lane 1 alone in the branch, lanes
        // 1 to 3 after it (lane 0 has ended); block 1: lanes 0 and 1 in the branch, every lane after it
        RunOnEveryPath(16,
            output => WarpStackModelKernels.LaunchEndInsideBranchKernel(output, gridDimX: 2, blockDimX: 4),
            [0, 0x2, 0, 0, 0, 0xE, 0xE, 0xE, 0x3, 0x3, 0, 0, 0xF, 0xF, 0xF, 0xF]);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ContinuePastLocal_LanesContinueBeforeTheLocal_MeetAtTheEndOfEachIteration()
    {
        // Iteration 0: lanes 0 and 2 continue (0x5), lanes 1 and 3 take 0xA; iteration 1: lanes 0 and 1 continue (0x3),
        // lanes 2 and 3 take 0xC * 2
        RunOnEveryPath(4,
            output => WarpStackModelKernels.LaunchContinuePastLocalKernel(output, blockDimX: 4),
            [0x5 + 0x3, 0xA + 0x3, 0x5 + 0xC * 2, 0xA + 0xC * 2]);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ContinuePastNullable_LanesContinueBeforeTheNullable_ReadTheLanesThatTookTheBranch()
    {
        // Iteration 0: the odd lanes go on and lanes 5 and 7 take the mask 0xA0; iteration 1: the even lanes go on and
        // lanes 4 and 6 take 0x50; a lane 3 or below adds 9 in the iteration it goes on
        RunOnEveryPath(8,
            output => WarpStackModelKernels.LaunchContinuePastNullableKernel(output, blockDimX: 8),
            [9, 9, 9, 9, 0x50, 0xA0, 0x50, 0xA0]);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ContinuePastUsing_LanesContinueBeforeTheUsing_ReadTheLanesOfTheIteration()
    {
        // Iteration 0: the odd lanes 0xAA; iteration 1: the even lanes 0x55; each lane adds its index
        RunOnEveryPath(8,
            output => WarpStackModelKernels.LaunchContinuePastUsingKernel(output, blockDimX: 8),
            [0x55, 0xAA + 1, 0x55 + 2, 0xAA + 3, 0x55 + 4, 0xAA + 5, 0x55 + 6, 0xAA + 7]);
    }
}
