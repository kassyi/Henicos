#nullable enable
using System;
using System.Linq;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// The lanes of a warp meet at the same instance of a warp collective on every path (#1468): in device functions, in loops
/// whose iterations diverge differently, after some lanes have exited and at <c>Gpu.SyncWarp</c>. Lanes of the mask that
/// never reach the same call are a run-time error on the CPU paths, as is reading a lane that has exited (CUDA leaves both
/// undefined; the release CUDA build does not check them).
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class WarpRendezvousDifferentialTests
{
    // t * 10 + 1, the input of #1468
    private static int[] Input(int threads) => Enumerable.Range(0, threads).Select(static t => t * 10 + 1).ToArray();

    // Runs the launch on every path of this build and compares the output with the expected values
    private static void RunOnEveryPath(int outputLength, Action<int[]> launch, int[] expected)
    {
#if GSHARP_CUDA
        CudaCapability.EnsureGpuAvailable();
#endif
        foreach (var path in DiffPaths.Available)
        {
            path.Preflight?.Invoke();
            var output = new int[outputLength];
            path.Run(() => launch(output));
            Assert.True(expected.SequenceEqual(output),
                $"{path.Name}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", output)}]");
        }
    }

    // Runs the launch on the CPU paths of this build and returns the error of each; the release CUDA build does not
    // check the forms that CUDA leaves undefined
    private static void AssertErrorOnCpuPaths(Action<int[]> launch, int outputLength, params string[] fragments)
    {
        var cpuPaths = DiffPaths.Available.Where(static p => p.Path != DiffPath.Cuda).ToArray();
        Skip.If(cpuPaths.Length == 0, "The release CUDA build does not check warp collectives whose lanes never meet");
        foreach (var path in cpuPaths)
        {
            path.Preflight?.Invoke();
            var output = new int[outputLength];
            var ex = Assert.Throws<InvalidOperationException>(() => path.Run(() => launch(output)));
            foreach (var fragment in fragments)
            {
                Assert.True(ex.Message.Contains(fragment, StringComparison.Ordinal),
                    $"{path.Name}: '{fragment}' is not in: {ex.Message}");
            }
        }
    }

    // The values of HalfThenFull for each lane of a warp of 32
    private static int[] HalfThenFull(int[] v)
    {
        var half = new int[v.Length];
        for (int t = 0; t < v.Length; t++)
        {
            half[t] = (t & 31) >= 16 ? v[t] + v[t ^ 1] : v[t];
        }

        var result = new int[v.Length];
        for (int t = 0; t < v.Length; t++)
        {
            result[t] = half[t] + half[t ^ 16];
        }

        return result;
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SubWarpShuffleInDeviceFunction_ReadsTheSameShuffleOfEveryLane()
    {
        var input = Input(32);
        var expected = HalfThenFull(input);
        // The values of #1468: 333, 343, 393, ...
        Assert.Equal(new[] { 333, 343, 393 }, expected.Take(3));
        RunOnEveryPath(32,
            output => WarpRendezvousDifferentialKernels.LaunchSubWarpShuffleInDeviceFunctionKernel(input, output,
                blockDimX: 32),
            expected);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_SubWarpShuffleInNestedDeviceFunction_KeepsTheCallSitesApart()
    {
        var input = Input(64);
        var first = HalfThenFull(input);
        var second = HalfThenFull(first);
        var expected = second.Zip(first, static (s, f) => s - f).ToArray();
        RunOnEveryPath(64,
            output => WarpRendezvousDifferentialKernels.LaunchSubWarpShuffleInNestedDeviceFunctionKernel(input, output,
                blockDimX: 64),
            expected);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_DeviceFunctionInLoop_MeetsInEveryIteration()
    {
        var input = Input(32);
        var expected = input;
        for (int k = 0; k < 3; k++)
        {
            expected = HalfThenFull(expected).Select(static x => x & 0xFFFF).ToArray();
        }

        RunOnEveryPath(32,
            output => WarpRendezvousDifferentialKernels.LaunchDeviceFunctionInLoopKernel(input, output, blockDimX: 32),
            expected);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_AlternatingDivergenceInLoop_MeetsWithTheLanesOfEachIteration()
    {
        var input = Input(64);
        var v = (int[])input.Clone();
        for (int k = 0; k < 4; k++)
        {
            var taken = (int[])v.Clone();
            for (int t = 0; t < v.Length; t++)
            {
                if ((((t & 31) + k) & 1) == 0)
                {
                    taken[t] = v[t] + v[t ^ 2] * (k + 1);
                }
            }

            for (int t = 0; t < v.Length; t++)
            {
                int down = (t & 31) < 31 ? taken[t + 1] : taken[t];
                v[t] = (taken[t] + down) & 0xFFFFF;
            }
        }

        RunOnEveryPath(64,
            output => WarpRendezvousDifferentialKernels.LaunchAlternatingDivergenceInLoopKernel(input, output,
                blockDimX: 64),
            v);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_DeviceFunctionOnBothSides_MeetsWithinEachSide()
    {
        var input = Input(32);
        var pair = new int[32];
        for (int t = 0; t < 32; t++)
        {
            int sum = input[t] + input[t ^ 1];
            pair[t] = t < 16 ? sum : sum * 3;
        }

        var expected = Enumerable.Range(0, 32).Select(t => pair[t] + pair[t ^ 16]).ToArray();
        RunOnEveryPath(32,
            output => WarpRendezvousDifferentialKernels.LaunchDeviceFunctionOnBothSidesKernel(input, output,
                blockDimX: 32),
            expected);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_PartialWarpVotes_CountTheLanesThatExist()
    {
        // 40 threads: warp 1 has lanes 0..7 only
        var input = Input(40).Select(static (v, t) => t % 3 == 0 ? v + 1 : v).ToArray();
        var expected = new int[80];
        for (int t = 0; t < 40; t++)
        {
            int warpBase = t & ~31;
            int warpEnd = Math.Min(warpBase + 32, 40);
            int sum = 0;
            uint ballot = 0u;
            for (int u = warpBase; u < warpEnd; u++)
            {
                sum += input[u];
                ballot |= (input[u] & 1) == 0 ? 1u << (u - warpBase) : 0u;
            }

            expected[t * 2] = sum;
            expected[t * 2 + 1] = (int)ballot;
        }

        RunOnEveryPath(80,
            output => WarpRendezvousDifferentialKernels.LaunchPartialWarpVotesKernel(input, output, blockDimX: 40),
            expected);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_VotesAfterExit_LeaveTheExitedLanesOut()
    {
        var input = Input(32).Select(static (v, t) => 1000 - v + (t % 5)).ToArray();
        var expected = new int[128];
        int min = input.Take(16).Min();
        int sum = input.Take(16).Sum();
        for (int t = 0; t < 16; t++)
        {
            // The GPU gives 0x5555 and 1 (RTX 3070 Ti, sm_86, CUDA 13.4)
            expected[t * 4] = 0x5555;
            expected[t * 4 + 1] = 1;
            expected[t * 4 + 2] = min;
            expected[t * 4 + 3] = sum;
        }

        RunOnEveryPath(128,
            output => WarpRendezvousDifferentialKernels.LaunchVotesAfterExitKernel(input, output, blockDimX: 32),
            expected);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SyncWarp_OrdersTheSharedMemoryOfTheLanesOfTheMask()
    {
        var input = Input(64);
        var expected = Enumerable.Range(0, 64).Select(t => (t & 31) < 16 ? input[t ^ 1] * 2 : -1).ToArray();
        RunOnEveryPath(64,
            output => WarpRendezvousDifferentialKernels.LaunchSyncWarpExchangeKernel(input, output, blockDimX: 64),
            expected);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Test_FullMaskOnBothSides_IsAnErrorOnTheCpuPaths()
    {
        var input = Input(32);
        AssertErrorOnCpuPaths(
            output => WarpRendezvousDifferentialKernels.LaunchFullMaskOnBothSidesKernel(input, output, blockDimX: 32),
            32, "Warp.ShuffleXorSync", "never reach the same call", "WarpRendezvousDifferentialKernels.cu.cs(37,");
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Test_FullMaskAtTwoSites_IsAnErrorOnTheCpuPaths()
    {
        var input = Input(32);
        AssertErrorOnCpuPaths(
            output => WarpRendezvousDifferentialKernels.LaunchFullMaskAtTwoSitesKernel(input, output, blockDimX: 32),
            32, "Warp.ShuffleXorSync", "never reach the same call");
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Test_FullMaskBeforeBarrier_IsAnErrorOnTheCpuPaths()
    {
        var input = Input(32);
        AssertErrorOnCpuPaths(
            output => WarpRendezvousDifferentialKernels.LaunchFullMaskBeforeBarrierKernel(input, output, blockDimX: 32),
            32, "Warp.ShuffleXorSync", "lanes 0xFFFF0000", "never reach the same call");
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Test_ShuffleReadsExitedLane_IsAnErrorOnTheCpuPaths() =>
        AssertErrorOnCpuPaths(
            output => WarpRendezvousDifferentialKernels.LaunchShuffleReadsExitedLaneKernel(output, blockDimX: 32),
            32, "Warp.ShuffleDownSync", "in lane 15 reads lane 16 of warp 0, which has exited");
}
