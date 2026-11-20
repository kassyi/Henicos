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
}
