#nullable enable
using System;
using System.Linq;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// <c>Warp.ActiveMask()</c> returns the lanes that reach the call together (#844): the lanes that took the same way
/// through the thread-dependent branches since the warp last ran together, and have not exited. The expected values are
/// the ones the GPU gives (RTX 3070 Ti, sm_86, CUDA 13.4); CUDA documents only that the result holds the caller and no
/// lane that has exited, and from Volta on it may hold fewer lanes, so the CPU paths return the classic SIMT value.
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class WarpActiveMaskDifferentialTests
{
    // Runs the launch on every path of this build and compares the output with the expected values
    private static void RunOnEveryPath<T>(int outputLength, Action<T[]> launch, T[] expected)
    {
#if GSHARP_CUDA
        CudaCapability.EnsureGpuAvailable();
#endif
        foreach (var path in DiffPaths.Available)
        {
            path.Preflight?.Invoke();
            var output = new T[outputLength];
            path.Run(() => launch(output));
            Assert.True(expected.SequenceEqual(output),
                $"{path.Name}: expected [{Format(expected)}], got [{Format(output)}]");
        }
    }

    // Runs the launch on every path: the CPU paths return the classic value, the GPU any subset of it that holds the
    // calling lane (CUDA leaves the rest to the scheduler), lane giving the lane of each entry of the output
    private static void RunWithinClassicBounds(int outputLength, Action<uint[]> launch, uint[] classic,
        Func<int, int> lane)
    {
#if GSHARP_CUDA
        CudaCapability.EnsureGpuAvailable();
#endif
        foreach (var path in DiffPaths.Available)
        {
            path.Preflight?.Invoke();
            var output = new uint[outputLength];
            path.Run(() => launch(output));
            if (path.Path != DiffPath.Cuda)
            {
                Assert.True(classic.SequenceEqual(output),
                    $"{path.Name}: expected [{Format(classic)}], got [{Format(output)}]");
                continue;
            }

            for (int i = 0; i < outputLength; i++)
            {
                var own = 1u << (lane(i) & 31);
                Assert.True(
                    classic[i] == 0u ? output[i] == 0u : (output[i] & own) != 0u && (output[i] & ~classic[i]) == 0u,
                    $"{path.Name}: entry {i} is 0x{output[i]:X8}, not within 0x{classic[i]:X8} with lane {lane(i) & 31}");
            }
        }
    }

    private static string Format<T>(T[] values) =>
        string.Join(", ", values.Select(static v => v is uint u ? $"0x{u:X8}" : $"{v}"));

    // Runs the launch on the CPU paths of this build and checks the error of each; the release CUDA build does not check
    // the forms that CUDA leaves undefined
    private static void AssertErrorOnCpuPaths<T>(Action<T[]> launch, int outputLength, params string[] fragments)
    {
        var cpuPaths = DiffPaths.Available.Where(static p => p.Path != DiffPath.Cuda).ToArray();
        Skip.If(cpuPaths.Length == 0, "The release CUDA build does not check shuffles that read a lane that does not take part");
        foreach (var path in cpuPaths)
        {
            path.Preflight?.Invoke();
            var output = new T[outputLength];
            var ex = Assert.Throws<InvalidOperationException>(() => path.Run(() => launch(output)));
            foreach (var fragment in fragments)
            {
                Assert.True(ex.Message.Contains(fragment, StringComparison.Ordinal),
                    $"{path.Name}: '{fragment}' is not in: {ex.Message}");
            }
        }
    }

    // value for the lanes of a warp of 32 that select, 0 for the others
    private static uint[] PerLane(int lanes, Func<int, bool> select, uint value) =>
        Enumerable.Range(0, lanes).Select(t => select(t) ? value : 0u).ToArray();

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_InBranch_ReturnsTheLanesOfTheBranch() =>
        RunOnEveryPath(32,
            static output => WarpActiveMaskDifferentialKernels.LaunchInBranchKernel(output, blockDimX: 32),
            PerLane(32, static t => t < 4, 0xFu));

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_AfterEarlyReturn_LeavesTheExitedLanesOut() =>
        RunOnEveryPath(32,
            static output => WarpActiveMaskDifferentialKernels.LaunchAfterEarlyReturnKernel(output, blockDimX: 32),
            PerLane(32, static t => t < 4, 0xFu));

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_DeviceFunctionOnBothSides_ReturnsTheLanesOfEachSide() =>
        RunOnEveryPath(32,
            static output => WarpActiveMaskDifferentialKernels.LaunchDeviceFunctionOnBothSidesKernel(output, blockDimX: 32),
            Enumerable.Range(0, 32).Select(static t => t < 4 ? 0x0000000Fu : 0xFFFFFFF0u).ToArray());

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_BothSides_ReturnsTheLanesOfEachSide() =>
        RunOnEveryPath(64,
            static output => WarpActiveMaskDifferentialKernels.LaunchBothSidesKernel(output, blockDimX: 64),
            Enumerable.Range(0, 64).Select(static t => (t & 1) == 0 ? 0x55555555u : 0xAAAAAAAAu).ToArray());

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_AfterBranch_ReturnsEveryLane()
    {
#if GSHARP_CUDA
        CudaCapability.EnsureGpuAvailable();
#endif
        foreach (var path in DiffPaths.Available)
        {
            path.Preflight?.Invoke();
            var side = new uint[32];
            var output = new uint[32];
            path.Run(() => WarpActiveMaskDifferentialKernels.LaunchAfterBranchKernel(side, output, blockDimX: 32));
            Assert.True(output.All(static m => m == 0xFFFFFFFFu), $"{path.Name}: got [{Format(output)}]");
            Assert.Equal(PerLane(32, static t => t < 4, 1u), side);
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_DivergentLoop_ReturnsTheLanesOfEachIteration()
    {
        // Iteration k: the lanes t with t % 4 >= k
        uint[] masks = [0xFFFFFFFFu, 0xEEEEEEEEu, 0xCCCCCCCCu, 0x88888888u];
        var expected = new uint[128];
        for (int t = 0; t < 32; t++)
        {
            for (int k = 0; k <= t % 4; k++)
            {
                expected[t * 4 + k] = masks[k];
            }
        }

        RunOnEveryPath(128,
            static output => WarpActiveMaskDifferentialKernels.LaunchDivergentLoopKernel(output, blockDimX: 32),
            expected);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_AfterDivergentLoop_ReturnsEveryLane()
    {
        var expected = new uint[64];
        for (int t = 0; t < 32; t++)
        {
            expected[t * 2] = 0xFFFFFFFFu;
            expected[t * 2 + 1] = (uint)((t % 4) * (t % 4 + 1) / 2);
        }

        RunOnEveryPath(64,
            static output => WarpActiveMaskDifferentialKernels.LaunchAfterDivergentLoopKernel(output, blockDimX: 32),
            expected);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_AlternatingBranchInLoop_ReturnsTheLanesOfEachIteration()
    {
        var expected = new uint[64];
        for (int t = 0; t < 32; t++)
        {
            for (int k = 0; k < 2; k++)
            {
                if (((t + k) & 1) == 0)
                {
                    expected[t * 2 + k] = k == 0 ? 0x55555555u : 0xAAAAAAAAu;
                }
            }
        }

        RunOnEveryPath(64,
            static output => WarpActiveMaskDifferentialKernels.LaunchAlternatingBranchInLoopKernel(output, blockDimX: 32),
            expected);
    }

    // The GPU does not run the lanes together again after the inner loop here: in the second outer iteration it gives
    // 0x77777777 and 0x88888888 where the classic value is 0xFFFFFFFF (RTX 3070 Ti, sm_86, CUDA 13.4), a subset that CUDA
    // allows (Independent Thread Scheduling)
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_NestedLoop_KeepsTheOuterIterationsApart()
    {
        var expected = new uint[256];
        for (int t = 0; t < 32; t++)
        {
            for (int o = 0; o < 2; o++)
            {
                for (int k = 0; k < t % 4 + o; k++)
                {
                    expected[t * 8 + o * 4 + k] = ComputeMask(o, k);
                }
            }
        }

        RunWithinClassicBounds(256,
            static output => WarpActiveMaskDifferentialKernels.LaunchNestedLoopKernel(output, blockDimX: 32),
            expected, static i => i / 8);

        static uint ComputeMask(int o, int k)
        {
            uint mask = 0u;
            for (int u = 0; u < 32; u++)
            {
                mask |= u % 4 + o > k ? 1u << u : 0u;
            }

            return mask;
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ContinueInLoop_StartsEachIterationTogether()
    {
        var expected = new uint[192];
        for (int t = 0; t < 32; t++)
        {
            for (int k = 0; k < 3; k++)
            {
                uint clear = 0u;
                for (int u = 0; u < 32; u++)
                {
                    clear |= ((u >> k) & 1) == 0 ? 1u << u : 0u;
                }

                bool taken = ((t >> k) & 1) == 0;
                expected[t * 6 + k * 2 + (taken ? 0 : 1)] = taken ? clear : ~clear;
            }
        }

        RunOnEveryPath(192,
            static output => WarpActiveMaskDifferentialKernels.LaunchContinueInLoopKernel(output, blockDimX: 32),
            expected);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_BreakInLoop_ReturnsTheLanesOfEachIterationAndEveryLaneAfter()
    {
        // Iteration k: the lanes t with t % 4 > k; after the loop, every lane
        uint[] masks = [0xEEEEEEEEu, 0xCCCCCCCCu, 0x88888888u];
        var expected = new uint[128];
        for (int t = 0; t < 32; t++)
        {
            for (int k = 0; k < t % 4; k++)
            {
                expected[t * 4 + k] = masks[k];
            }

            expected[t * 4 + 3] = 0xFFFFFFFFu;
        }

        RunWithinClassicBounds(128,
            static output => WarpActiveMaskDifferentialKernels.LaunchBreakInLoopKernel(output, blockDimX: 32),
            expected, static i => i / 4);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Switch_ReturnsTheLanesOfEachSection()
    {
        uint[] masks = [0x49249249u, 0x92492492u, 0x24924924u];
        var expected = Enumerable.Range(0, 64).Select(i => i < 32 ? masks[i % 3] : 0xFFFFFFFFu).ToArray();
        RunWithinClassicBounds(64,
            static output => WarpActiveMaskDifferentialKernels.LaunchSwitchKernel(output, blockDimX: 32),
            expected, static i => i);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_EarlyReturnFromDeviceFunction_MeetsTheOtherLanesAfterTheCall()
    {
#if GSHARP_CUDA
        CudaCapability.EnsureGpuAvailable();
#endif
        var expectedEarly = PerLane(32, static t => t < 4, 0x0000000Fu);
        var expected = Enumerable.Range(0, 64).Select(static i => i < 4 ? 1u : i < 32 ? 0xFFFFFFF0u : 0xFFFFFFFFu).ToArray();
        foreach (var path in DiffPaths.Available)
        {
            path.Preflight?.Invoke();
            var early = new uint[32];
            var output = new uint[64];
            path.Run(() => WarpActiveMaskDifferentialKernels.LaunchEarlyReturnFromDeviceFunctionKernel(early, output,
                blockDimX: 32));
            Assert.True(expectedEarly.SequenceEqual(early), $"{path.Name}: got early [{Format(early)}]");
            Assert.True(expected.SequenceEqual(output), $"{path.Name}: got [{Format(output)}]");
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_DataBranch_ReturnsTheLanesWhoseDataSelectsIt()
    {
        var data = Enumerable.Range(0, 32).Select(static t => t % 3 == 0 ? 1 : 0).ToArray();
        RunOnEveryPath(32,
            output => WarpActiveMaskDifferentialKernels.LaunchDataBranchKernel(data, output, blockDimX: 32),
            PerLane(32, static t => t % 3 == 0, 0x49249249u));
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_BallotWithActiveMask_VotesAmongTheLanesOfTheBranch() =>
        RunOnEveryPath(32,
            static output => WarpActiveMaskDifferentialKernels.LaunchBallotWithActiveMaskKernel(output, blockDimX: 32),
            PerLane(32, static t => t < 4, 0x5u));

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_AfterSyncWarp_ReturnsTheLanesOfTheBranch() =>
        RunOnEveryPath(32,
            static output => WarpActiveMaskDifferentialKernels.LaunchAfterSyncWarpKernel(output, blockDimX: 32),
            PerLane(32, static t => t < 4, 0xFu));

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Halves_ShuffleWithinTheMaskOfEachHalf()
    {
#if GSHARP_CUDA
        CudaCapability.EnsureGpuAvailable();
#endif
        var input = Enumerable.Range(0, 32).Select(static t => t * 10 + 1).ToArray();
        var expectedMasks = Enumerable.Range(0, 32).Select(static t => t < 16 ? 0x0000FFFFu : 0xFFFF0000u).ToArray();
        var expected = Enumerable.Range(0, 32)
            .Select(t => (input[t] + input[t ^ 1]) * (t < 16 ? 1 : 3))
            .ToArray();
        foreach (var path in DiffPaths.Available)
        {
            path.Preflight?.Invoke();
            var masks = new uint[32];
            var output = new int[32];
            path.Run(() => WarpActiveMaskDifferentialKernels.LaunchHalvesKernel(input, masks, output, blockDimX: 32));
            Assert.True(expectedMasks.SequenceEqual(masks), $"{path.Name}: got masks [{Format(masks)}]");
            Assert.Equal(expected, output);
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_GridStrideLoop_LastRoundReturnsTheLanesLeftInTheLoop()
    {
        // One block of 64 threads, 80 elements: the second round runs lanes 0..15 of warp 0
        var expected = Enumerable.Range(0, 80).Select(static i => i < 64 ? 0xFFFFFFFFu : 0x0000FFFFu).ToArray();
        RunOnEveryPath(80,
            static output => WarpActiveMaskDifferentialKernels.LaunchGridStrideLoopKernel(output, 80, blockDimX: 64),
            expected);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_GridStrideReduce_ReducesTheLanesLeftInTheLoop()
    {
        var input = Enumerable.Range(0, 80).Select(static i => i % 7 == 3 ? 1u << (i % 32) : 0u).ToArray();
        var expected = new uint[80];
        for (int i = 0; i < 80; i++)
        {
            // The lanes that run the same round together: 0..31, 32..63, then 64..79
            int start = i < 64 ? i & ~31 : 64;
            int end = i < 64 ? start + 32 : 80;
            for (int j = start; j < end; j++)
            {
                expected[i] |= input[j];
            }
        }

        RunOnEveryPath(80,
            output => WarpActiveMaskDifferentialKernels.LaunchGridStrideReduceKernel(input, output, 80, blockDimX: 64),
            expected);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_PartialWarp_ReturnsTheLanesThatExist() =>
        RunOnEveryPath(40,
            static output => WarpActiveMaskDifferentialKernels.LaunchPartialWarpKernel(output, blockDimX: 40),
            Enumerable.Range(0, 40).Select(static t => t < 32 ? 0xFFFFFFFFu : 0xFFu).ToArray());

    // The shuffles of GpuBigIntCmpKernels.IsZeroKernel read lanes 16..31 in the last round, which have left the loop
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Test_GridStrideShuffleTail_IsAnErrorOnTheCpuPaths()
    {
        var input = new uint[80];
        AssertErrorOnCpuPaths<uint>(
            output => WarpActiveMaskDifferentialKernels.LaunchGridStrideShuffleTailKernel(input, output, 80, blockDimX: 64),
            80, "Warp.ShuffleDownSync", "reads lane 16 of warp 0, which has exited");
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Test_ShuffleOutsideActiveMask_IsAnErrorOnTheCpuPaths() =>
        AssertErrorOnCpuPaths<int>(
            static output => WarpActiveMaskDifferentialKernels.LaunchShuffleOutsideActiveMaskKernel(output, blockDimX: 32),
            32, "Warp.ShuffleXorSync", "reads lane 16 of warp 0, which does not take part");
}
