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

    // Checks exact scalar flags in [0..flagCount) on all paths, exact masks on CPU, and classic-bound masks on CUDA
    private static void RunFlagsAndMasksWithinClassicBounds(
        int flagCount,
        int outputLength,
        Action<uint[]> launch,
        uint[] classic)
    {
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

            for (int i = 0; i < flagCount; i++)
            {
                Assert.Equal(classic[i], output[i]);
            }

            for (int i = flagCount; i < outputLength; i++)
            {
                var own = 1u << (i & 31);
                Assert.True(
                    classic[i] == 0u ? output[i] == 0u : (output[i] & own) != 0u && (output[i] & ~classic[i]) == 0u,
                    $"{path.Name}: entry {i} is 0x{output[i]:X8}, not within 0x{classic[i]:X8} with lane {i & 31}");
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
    public void ActiveMask_InBranch_ReturnsTheLanesOfTheBranch() =>
        RunOnEveryPath(32,
            static output => WarpActiveMaskDifferentialKernels.LaunchInBranchKernel(output, blockDimX: 32),
            PerLane(32, static t => t < 4, 0xFu));

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_AfterEarlyReturn_LeavesTheExitedLanesOut() =>
        RunOnEveryPath(32,
            static output => WarpActiveMaskDifferentialKernels.LaunchAfterEarlyReturnKernel(output, blockDimX: 32),
            PerLane(32, static t => t < 4, 0xFu));

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_DeviceFunctionOnBothSides_ReturnsTheLanesOfEachSide() =>
        RunOnEveryPath(32,
            static output => WarpActiveMaskDifferentialKernels.LaunchDeviceFunctionOnBothSidesKernel(output, blockDimX: 32),
            Enumerable.Range(0, 32).Select(static t => t < 4 ? 0x0000000Fu : 0xFFFFFFF0u).ToArray());

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_BothSidesOfBranch_ReturnsTheLanesOfEachSide() =>
        RunOnEveryPath(64,
            static output => WarpActiveMaskDifferentialKernels.LaunchBothSidesKernel(output, blockDimX: 64),
            Enumerable.Range(0, 64).Select(static t => (t & 1) == 0 ? 0x55555555u : 0xAAAAAAAAu).ToArray());

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_AfterBranch_ReturnsEveryLane()
    {
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
    public void ActiveMask_DivergentLoop_ReturnsTheLanesOfEachIteration()
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
    public void ActiveMask_AfterDivergentLoop_ReturnsEveryLane()
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
    public void ActiveMask_AlternatingBranchInLoop_ReturnsTheLanesOfEachIteration()
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
    public void ActiveMask_NestedLoop_KeepsTheOuterIterationsApart()
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
    public void ActiveMask_ContinueInLoop_StartsEachIterationTogether()
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
    public void ActiveMask_BreakInLoop_ReturnsTheLanesOfEachIterationAndEveryLaneAfter()
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
    public void ActiveMask_Switch_ReturnsTheLanesOfEachSection()
    {
        uint[] masks = [0x49249249u, 0x92492492u, 0x24924924u];
        var expected = Enumerable.Range(0, 64).Select(i => i < 32 ? masks[i % 3] : 0xFFFFFFFFu).ToArray();
        RunWithinClassicBounds(64,
            static output => WarpActiveMaskDifferentialKernels.LaunchSwitchKernel(output, blockDimX: 32),
            expected, static i => i);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_EarlyReturnFromDeviceFunction_MeetsTheOtherLanesAfterTheCall()
    {
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
    public void ActiveMask_DataBranch_ReturnsTheLanesWhoseDataSelectsIt()
    {
        var data = Enumerable.Range(0, 32).Select(static t => t % 3 == 0 ? 1 : 0).ToArray();
        RunOnEveryPath(32,
            output => WarpActiveMaskDifferentialKernels.LaunchDataBranchKernel(data, output, blockDimX: 32),
            PerLane(32, static t => t % 3 == 0, 0x49249249u));
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Ballot_ActiveMaskInBranch_VotesAmongTheLanesOfTheBranch() =>
        RunOnEveryPath(32,
            static output => WarpActiveMaskDifferentialKernels.LaunchBallotWithActiveMaskKernel(output, blockDimX: 32),
            PerLane(32, static t => t < 4, 0x5u));

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_AfterSyncWarp_ReturnsTheLanesOfTheBranch() =>
        RunOnEveryPath(32,
            static output => WarpActiveMaskDifferentialKernels.LaunchAfterSyncWarpKernel(output, blockDimX: 32),
            PerLane(32, static t => t < 4, 0xFu));

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ShuffleXorSync_ActiveMaskOfEachHalf_ShufflesWithinTheHalf()
    {
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
    public void ActiveMask_GridStrideLoopLastRound_ReturnsTheLanesLeftInTheLoop()
    {
        // One block of 64 threads, 80 elements: the second round runs lanes 0..15 of warp 0
        var expected = Enumerable.Range(0, 80).Select(static i => i < 64 ? 0xFFFFFFFFu : 0x0000FFFFu).ToArray();
        RunOnEveryPath(80,
            static output => WarpActiveMaskDifferentialKernels.LaunchGridStrideLoopKernel(output, 80, blockDimX: 64),
            expected);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_GridStrideReduce_ReducesTheLanesLeftInTheLoop()
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
    public void ActiveMask_PartialWarp_ReturnsTheLanesThatExist() =>
        RunOnEveryPath(40,
            static output => WarpActiveMaskDifferentialKernels.LaunchPartialWarpKernel(output, blockDimX: 40),
            Enumerable.Range(0, 40).Select(static t => t < 32 ? 0xFFFFFFFFu : 0xFFu).ToArray());

    // A lane leaves the loop by break outer, or continues outer; the lanes of one iteration meet at the end of its body,
    // and every lane meets the others where the loop ends (#1990)
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_LabeledBreakAndContinue_StartsEachIterationWithTheLanesLeft()
    {
        // The lanes in the loop at the start of the iteration, and those that run its inner loop to the end
        static bool InLoop(int lane, int iteration) => ((lane >> 2) & 1) == 1 || lane % 4 >= iteration;
        static bool PassesInnerLoop(int lane, int iteration) => ((lane >> 2) & 1) == 0 && lane % 4 > iteration;

        var expected = new uint[256];
        for (int t = 0; t < 32; t++)
        {
            for (int i = 0; i < 3; i++)
            {
                uint start = 0u;
                uint passing = 0u;
                for (int u = 0; u < 32; u++)
                {
                    start |= InLoop(u, i) ? 1u << u : 0u;
                    passing |= PassesInnerLoop(u, i) ? 1u << u : 0u;
                }

                expected[(t * 8) + i] = InLoop(t, i) ? start : 0u;
                expected[(t * 8) + 3 + i] = PassesInnerLoop(t, i) ? passing : 0u;
            }

            expected[(t * 8) + 6] = 0xFFFFFFFFu;
        }

        RunWithinClassicBounds(256,
            static output => LabeledJumpDifferentialKernels.LaunchActiveMaskAfterALabeledBreak(output, blockDimX: 32),
            expected, static i => i / 8);
    }

    // The shuffles of GpuBigIntCmpKernels.IsZeroKernel read lanes 16..31 in the last round, which have left the loop
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void ShuffleDownSync_GridStrideTailReadsExitedLane_FailsOnTheCpuPaths()
    {
        var input = new uint[80];
        AssertErrorOnCpuPaths<uint>(
            output => WarpActiveMaskDifferentialKernels.LaunchGridStrideShuffleTailKernel(input, output, 80, blockDimX: 64),
            80, "Warp.ShuffleDownSync", "reads lane 16 of warp 0, which has exited");
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void ShuffleXorSync_SourceLaneOutsideActiveMask_FailsOnTheCpuPaths() =>
        AssertErrorOnCpuPaths<int>(
            static output => WarpActiveMaskDifferentialKernels.LaunchShuffleOutsideActiveMaskKernel(output, blockDimX: 32),
            32, "Warp.ShuffleXorSync", "reads lane 16 of warp 0, which does not take part");

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_UniformSwitchBreak_ReconvergesAfterSwitch() =>
        RunOnEveryPath(96,
            static output => WarpActiveMaskDifferentialKernels.LaunchUniformSwitchBreakKernel(
                output, 0, blockDimX: 32),
            PerLane(32, static _ => true, 0xFFFFFFFFu)
                .Concat(PerLane(32, static t => (t & 1) == 0, 0x55555555u))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_UniformSwitchGotoCase_ReconvergesAfterSwitch() =>
        RunOnEveryPath(160,
            static output => WarpActiveMaskDifferentialKernels.LaunchUniformSwitchGotoCaseKernel(
                output, 0, blockDimX: 32),
            PerLane(32, static _ => true, 0xFFFFFFFFu)
                .Concat(PerLane(32, static t => (t & 1) == 0, 0x55555555u))
                .Concat(PerLane(32, static t => (t & 1) == 0, 0x55555555u))
                .Concat(PerLane(32, static t => (t & 1) == 1, 0xAAAAAAAAu))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_UniformSwitchLabeledBreak_ReconvergesAfterSwitch() =>
        RunOnEveryPath(96,
            static output => WarpActiveMaskDifferentialKernels.LaunchUniformSwitchLabeledBreakKernel(
                output, 0, blockDimX: 32),
            PerLane(32, static _ => true, 0xFFFFFFFFu)
                .Concat(PerLane(32, static t => (t & 1) == 0, 0x55555555u))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_UniformSwitchBreakShuffle_ShufflesAcrossAllLanes() =>
        RunOnEveryPath(32,
            static output => WarpActiveMaskDifferentialKernels.LaunchUniformSwitchBreakShuffleKernel(
                output, 0, blockDimX: 32),
            Enumerable.Range(0, 32).Select(static t => t ^ 1).ToArray());

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_ForwardGoto_ReconvergesAtTargetLabel() =>
        RunOnEveryPath(96,
            static output => WarpActiveMaskDifferentialKernels.LaunchForwardGotoKernel(output, blockDimX: 32),
            PerLane(32, static _ => true, 0xFFFFFFFFu)
                .Concat(PerLane(32, static t => (t & 1) == 0, 0x55555555u))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_GotoOutOfUniformIf_ReconvergesAtTargetLabel() =>
        RunOnEveryPath(96,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoOutOfUniformIfKernel(
                output, 0, blockDimX: 32),
            PerLane(32, static _ => true, 0xFFFFFFFFu)
                .Concat(PerLane(32, static t => (t & 1) == 0, 0x55555555u))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_GotoFromBranchWithSplit_ReconvergesAtTargetLabel() =>
        RunOnEveryPath(96,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoFromBranchWithSplitKernel(
                output, blockDimX: 32),
            PerLane(32, static t => (t & 1) == 1, 0xAAAAAAAAu)
                .Concat(PerLane(32, static t => (t & 1) == 0, 0x55555555u))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_TwoGotosOneLabel_ReconvergesAtTargetLabel() =>
        RunOnEveryPath(96,
            static output => WarpActiveMaskDifferentialKernels.LaunchTwoGotosOneLabelKernel(output, blockDimX: 32),
            PerLane(32, static t => (t & 3) != 1, 0xDDDDDDDDu)
                .Concat(PerLane(32, static t => (t & 3) == 0 || (t & 3) == 3, 0x99999999u))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_CrossingGotos_ReconvergesAtBothLabels()
    {
        var expected = PerLane(32, static t => (t & 1) == 0, 0x55555555u)
            .Concat(PerLane(32, static t => (t & 3) == 0, 0x11111111u))
            .Concat(PerLane(32, static t => (t & 1) == 1 || (t & 3) == 0, 0xBBBBBBBBu))
            .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
            .ToArray();

        RunWithinClassicBounds(128,
            static output => WarpActiveMaskDifferentialKernels.LaunchCrossingGotosKernel(output, blockDimX: 32),
            expected, static i => i);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_GotoOutOfLoopInRange_ReconvergesAtTargetLabel() =>
        RunOnEveryPath(96,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoOutOfLoopInRangeKernel(
                output, blockDimX: 32),
            PerLane(32, static _ => true, 0xFFFFFFFFu)
                .Concat(PerLane(32, static t => (t & 1) == 0, 0x55555555u))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    // The label of the other section is found and the jump leaves only the inner regions (the lowering used to fail
    // here). Both ways into the label take the same number of split points, so the masks match by symmetry; a second
    // way into a section with a different count is not reconverged yet
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_GotoToOtherSection_ReconvergesAtSiblingLabel() =>
        RunOnEveryPath(160,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoToOtherSectionKernel(
                output, 0, blockDimX: 32),
            PerLane(32, static t => (t & 4) == 0 && (t & 1) == 0, 0x05050505u)
                .Concat(PerLane(32, static t => (t & 4) == 0 && (t & 1) == 1, 0x0A0A0A0Au))
                .Concat(PerLane(32, static t => (t & 4) == 0, 0x0F0F0F0Fu))
                .Concat(PerLane(32, static t => (t & 4) == 0, 0x0F0F0F0Fu))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_ForwardGotoShuffle_ShufflesAcrossAllLanes() =>
        RunOnEveryPath(32,
            static output => WarpActiveMaskDifferentialKernels.LaunchForwardGotoShuffleKernel(
                output, blockDimX: 32),
            Enumerable.Range(0, 32).Select(static t => t ^ 1).ToArray());

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_GotoBackToRangeStart_ReconvergesAfterTheLoop() =>
        RunOnEveryPath(64,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoBackToRangeStartKernel(
                output, blockDimX: 32),
            PerLane(32, static t => (t & 1) == 0, 0x55555555u)
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_ForwardGotoOverLongerNestedBlock_SkipsTheBlock() =>
        RunOnEveryPath(96,
            static output => WarpActiveMaskDifferentialKernels.LaunchForwardGotoOverNestedBlockKernel(
                output, blockDimX: 32),
            PerLane(32, static t => (t & 1) == 0, 0x55555555u)
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .Concat(PerLane(32, static t => (t & 1) == 0, 4u))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_GotoInsideFirstStatementOfRange_ReconvergesAtTargetLabel() =>
        RunOnEveryPath(64,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoInsideFirstStatementOfRangeKernel(
                output, blockDimX: 32),
            PerLane(32, static t => (t & 1) == 0, 0x55555555u)
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_ForwardGotoFromDoublyLabeledStatement_ReconvergesAtTargetLabel() =>
        RunOnEveryPath(64,
            static output => WarpActiveMaskDifferentialKernels.LaunchForwardGotoFromDoublyLabeledStatementKernel(
                output, blockDimX: 32),
            PerLane(32, static t => (t & 1) == 0, 0x55555555u)
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_ForwardGotoIntoRangeMiddle_ReconvergesAtBothLabels() =>
        RunOnEveryPath(64,
            static output => WarpActiveMaskDifferentialKernels.LaunchForwardGotoIntoRangeMiddleKernel(
                output, blockDimX: 32),
            PerLane(32, static t => (t & 3) != 1, 0xDDDDDDDDu)
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_BackwardGotoLoop_ReconvergesAfterTheLoop() =>
        RunOnEveryPath(64,
            static output => WarpActiveMaskDifferentialKernels.LaunchBackwardGotoKernel(
                output, blockDimX: 32),
            Enumerable.Range(0, 32)
                .Select(static t => (t & 1) == 0 ? 0xFFFFFFFFu : 0xAAAAAAAAu)
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_GotoLoopWithExit_ReconvergesAfterBoth() =>
        RunOnEveryPath(64,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoLoopWithExitKernel(
                output, blockDimX: 32),
            Enumerable.Range(0, 32)
                .Select(static t => (t & 3) == 1 ? 0x22222222u : 0xFFFFFFFFu)
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_TwoBackwardGotosToSameLabel_ReconvergesAfterLatestGoto() =>
        RunOnEveryPath(64,
            static output => WarpActiveMaskDifferentialKernels.LaunchTwoBackwardGotosKernel(
                output, blockDimX: 32),
            Enumerable.Range(0, 32)
                .Select(static t => (t & 3) == 2 ? 0x44444444u : 0xFFFFFFFFu)
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_DoublyLabeledGotoLoop_ReopensInnerRangeOnSecondIteration() =>
        RunOnEveryPath(64,
            static output => WarpActiveMaskDifferentialKernels.LaunchDoublyLabeledGotoLoopKernel(
                output, blockDimX: 32),
            Enumerable.Range(0, 32)
                .Select(static t => (t & 1) == 1 ? 0u : ((t & 2) == 0 ? 0x11111111u : 0x55555555u))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_GotoLoopExitAfterSplit_ReconvergesAfterBoth() =>
        RunOnEveryPath(64,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoLoopExitAfterSplitKernel(
                output, blockDimX: 32),
            Enumerable.Range(0, 32)
                .Select(static t => (t & 4) == 0 && (t & 1) == 1 ? 0x0A0A0A0Au : 0xFFFFFFFFu)
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_ForwardGotoToLoopHead_ReconvergesBeforeAndAfterLoop() =>
        RunFlagsAndMasksWithinClassicBounds(32, 96,
            static output => WarpActiveMaskDifferentialKernels.LaunchForwardGotoToLoopHeadKernel(
                output, blockDimX: 32),
            Enumerable.Range(0, 32)
                .Select(static t => (t & 2) == 0 ? 1u : 0u)
                .Concat(Enumerable.Range(0, 32).Select(static t => (t & 1) == 0 ? 0xFFFFFFFFu : 0xAAAAAAAAu))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_GotoLoopInsideLabeledStatement_ReconvergesAfterTheLoop() =>
        RunOnEveryPath(64,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoLoopInsideLabeledStatementKernel(
                output, blockDimX: 32),
            PerLane(32, static t => (t & 1) == 1, 0xAAAAAAAAu)
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_GotoLoopAtKernelEnd_RecordsEachIteration() =>
        RunOnEveryPath(96,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoLoopAtKernelEndKernel(
                output, blockDimX: 32),
            PerLane(32, static _ => true, 0xFFFFFFFFu)
                .Concat(PerLane(32, static t => (t & 1) == 1, 0xAAAAAAAAu))
                .Concat(PerLane(32, static t => (t & 1) == 1, 0xAAAAAAAAu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_NestedGotoLoops_ReconvergesAfterInnerAndOuter() =>
        RunOnEveryPath(96,
            static output => WarpActiveMaskDifferentialKernels.LaunchNestedGotoLoopsKernel(
                output, blockDimX: 32),
            Enumerable.Range(0, 32)
                .Select(static t => (t & 1) == 0 ? 0xFFFFFFFFu : 0xAAAAAAAAu)
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_GotoBackToRangeStartWithoutSplit_LeavesAndReentersRange() =>
        RunOnEveryPath(32,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoBackToRangeStartWithoutSplitKernel(
                output, blockDimX: 32),
            PerLane(32, static t => (t & 1) == 0, 0x55555555u));

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_GotoLoopOverNestedBlock_ClosesOnlyInOuterList() =>
        RunOnEveryPath(96,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoLoopOverNestedBlockKernel(
                output, blockDimX: 32),
            Enumerable.Range(0, 32)
                .Select(static t => (t & 1) == 0 ? 0xFFFFFFFFu : 0xAAAAAAAAu)
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .Concat(PerLane(32, static _ => true, 5u))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_ChainedLabelDoubleLoopOnSplit_ReconvergesAfterBothLoops()
    {
        var expected = Enumerable.Range(0, 32)
            .Select(static t => (t & 3) switch
            {
                0 => 0xFFFFFFFFu,
                3 => 0x88888888u,
                _ => 0xEEEEEEEEu
            })
            .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
            .ToArray();
        RunWithinClassicBounds(64,
            static output => WarpActiveMaskDifferentialKernels.LaunchChainedLabelDoubleLoopOnSplitKernel(
                output, blockDimX: 32),
            expected, static i => i & 31);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_ChainedLabelSplitWithForwardGoto_ReconvergesBeforeAndAfterLoop() =>
        RunFlagsAndMasksWithinClassicBounds(32, 96,
            static output => WarpActiveMaskDifferentialKernels.LaunchChainedLabelSplitWithForwardGotoKernel(
                output, blockDimX: 32),
            Enumerable.Range(0, 32)
                .Select(static t => (t & 2) == 0 ? 1u : 0u)
                .Concat(Enumerable.Range(0, 32).Select(static t => (t & 1) == 0 ? 0xFFFFFFFFu : 0xAAAAAAAAu))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    private static uint[] ExpectedReenteredBranchMasks()
    {
        var expected = new uint[64];
        for (int t = 0; t < 32; t++)
        {
            expected[t * 2] = t == 2 ? 0x00000004u : 0u;
            expected[t * 2 + 1] = (t & 3) switch
            {
                1 => 0xAAAAAAAAu,
                2 or 3 => 0xCCCCCCCCu,
                _ => 0u
            };
        }

        return expected;
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_ReenteredBranchInLoop_ReconvergesAllLanesOfIteration() =>
        RunOnEveryPath(64,
            static output => WarpActiveMaskDifferentialKernels.LaunchReenteredBranchInLoopKernel(
                output, blockDimX: 32),
            ExpectedReenteredBranchMasks());

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_ReenteredDeviceFunctionInLoop_ReconvergesAllLanesOfIteration() =>
        RunOnEveryPath(64,
            static output => WarpActiveMaskDifferentialKernels.LaunchReenteredDeviceFunctionInLoopKernel(
                output, blockDimX: 32),
            ExpectedReenteredBranchMasks());

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_ReenteredSwitchInLoop_ReconvergesAllLanesOfIteration() =>
        RunOnEveryPath(64,
            static output => WarpActiveMaskDifferentialKernels.LaunchReenteredSwitchInLoopKernel(
                output, blockDimX: 32),
            ExpectedReenteredBranchMasks());

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_ReenteredInnerLoopInLoop_ReconvergesAllLanesOfIteration() =>
        RunOnEveryPath(64,
            static output => WarpActiveMaskDifferentialKernels.LaunchReenteredInnerLoopInLoopKernel(
                output, blockDimX: 32),
            ExpectedReenteredBranchMasks());

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_ReenteredBranchWithEarlyReturn_ExcludesExitedLane()
    {
        var expected = new uint[64];
        for (int t = 0; t < 32; t++)
        {
            expected[t * 2] = t == 2 ? 0x00000004u : 0u;
            expected[t * 2 + 1] = t == 6
                ? 0x0000DEADu
                : (t & 3) switch
                {
                    1 => 0xAAAAAAAAu,
                    2 or 3 => 0xCCCCCC8Cu,
                    _ => 0u
                };
        }

        RunOnEveryPath(64,
            static output => WarpActiveMaskDifferentialKernels.LaunchReenteredBranchWithEarlyReturnKernel(
                output, blockDimX: 32),
            expected);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_ReenteredBranchWithForwardGoto_ReconvergesAtLabel() =>
        RunOnEveryPath(64,
            static output => WarpActiveMaskDifferentialKernels.LaunchReenteredBranchWithForwardGotoKernel(
                output, blockDimX: 32),
            ExpectedReenteredBranchMasks());

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_RangeRestartWithSplitAfterLabel_ReconvergesAfterLoop() =>
        RunOnEveryPath(64,
            static output => WarpActiveMaskDifferentialKernels.LaunchRangeRestartWithSplitAfterLabelKernel(
                output, blockDimX: 32),
            PerLane(32, static t => (t & 1) == 0, 0x55555555u)
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_GotoLoopReenteredFromBranch_ReconvergesAfterInnerBranch()
    {
        var expected = PerLane(32, static t => (t & 1) == 1, 0xAAAAAAAAu)
            .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
            .ToArray();
        RunWithinClassicBounds(64,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoLoopReenteredFromBranchKernel(
                output, blockDimX: 32),
            expected, static i => i & 31);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_ThreeLevelReenteredBranch_ReconvergesAtEachLevel()
    {
        var expected = new uint[64];
        for (int t = 0; t < 32; t++)
        {
            expected[t * 2] = t == 14 ? 0x00004000u : 0u;
            for (int i = 0; i < 2; i++)
            {
                if (((t >> i) & 1) != 1)
                {
                    continue;
                }

                for (int j = 0; j < 2; j++)
                {
                    if (((t >> (2 + j)) & 1) == 1)
                    {
                        uint mask = 0u;
                        for (int s = 0; s < 32; s++)
                        {
                            if (((s >> i) & 1) == 1 && ((s >> (2 + j)) & 1) == 1)
                            {
                                mask |= 1u << s;
                            }
                        }

                        expected[t * 2 + 1] = mask;
                    }
                }
            }
        }

        RunOnEveryPath(64,
            static output => WarpActiveMaskDifferentialKernels.LaunchThreeLevelReenteredBranchKernel(
                output, blockDimX: 32),
            expected);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_GotoCaseJoin_ReconvergesAtTargetSection() =>
        RunOnEveryPath(128,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoCaseJoinKernel(
                output, 0, blockDimX: 32),
            PerLane(32, static t => (t & 1) == 0, 0x55555555u)
                .Concat(PerLane(32, static t => (t & 1) == 0, 0x55555555u))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_GotoCaseToEarlierSection_ReconvergesAtTargetSection() =>
        RunOnEveryPath(128,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoCaseToEarlierSectionKernel(
                output, 0, blockDimX: 32),
            PerLane(32, static t => (t & 1) == 1, 0xAAAAAAAAu)
                .Concat(PerLane(32, static t => (t & 1) == 1, 0xAAAAAAAAu))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_GotoLabelJoin_ReconvergesAtTargetLabel() =>
        RunOnEveryPath(128,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoLabelJoinKernel(
                output, 0, blockDimX: 32),
            PerLane(32, static t => (t & 1) == 0, 0x55555555u)
                .Concat(PerLane(32, static t => (t & 1) == 1, 0xAAAAAAAAu))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_GotoDefaultJoin_ReconvergesAtDefaultSection() =>
        RunOnEveryPath(128,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoDefaultJoinKernel(
                output, 0, blockDimX: 32),
            PerLane(32, static t => (t & 1) == 0, 0x55555555u)
                .Concat(PerLane(32, static t => (t & 1) == 0, 0x55555555u))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_GotoCaseChain_ReconvergesAtEachSection() =>
        RunOnEveryPath(160,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoCaseChainKernel(
                output, 0, blockDimX: 32),
            PerLane(32, static t => t % 3 == 0, 0x49249249u)
                .Concat(PerLane(32, static t => t % 3 == 0, 0x49249249u))
                .Concat(PerLane(32, static t => t % 3 != 2, 0xDB6DB6DBu))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_TwoSourcesOneJoin_ReconvergesBothSourcesWithDirectLanes() =>
        RunOnEveryPath(160,
            static output => WarpActiveMaskDifferentialKernels.LaunchTwoSourcesOneJoinKernel(
                output, 0, blockDimX: 32),
            PerLane(32, static t => t % 3 == 0, 0x49249249u)
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .Concat(PerLane(32, static t => t % 3 == 2, 0x24924924u))
                .Concat(PerLane(32, static t => t % 3 == 2, 0x24924924u))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_ConditionalGotoCase_LeavesBreakLanesBeforeJoin() =>
        RunOnEveryPath(128,
            static output => WarpActiveMaskDifferentialKernels.LaunchConditionalGotoCaseKernel(
                output, 0, blockDimX: 32),
            PerLane(32, static t => (t & 1) == 0, 0x55555555u)
                .Concat(PerLane(32, static t => (t & 3) == 0, 0x11111111u))
                .Concat(PerLane(32, static t => (t & 3) != 0, 0xEEEEEEEEu))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_GotoCaseJoinShuffle_ShufflesAcrossAllLanes() =>
        RunOnEveryPath(32,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoCaseJoinShuffleKernel(
                output, 0, blockDimX: 32),
            Enumerable.Range(0, 32).Select(static t => t ^ 1).ToArray());

    // The CPU paths join the lanes that jump to case 1 with those that enter it from the switch (m1 = 0xD34D34D3). CUDA
    // keeps them apart there: m1 is 0x41041041 for the lanes that jumped and 0x92492492 for the others, each a subset of
    // the classic value that holds the calling lane; every other entry is the same on every path
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_SkippedJoinGotoCase_LeavesSkippedJoinOnOuterJump() =>
        RunWithinClassicBounds(160,
            static output => WarpActiveMaskDifferentialKernels.LaunchSkippedJoinGotoCaseKernel(
                output, 0, blockDimX: 32),
            PerLane(32, static t => t % 3 == 0, 0x49249249u)
                .Concat(PerLane(32, static t => t % 3 == 0 && (t & 1) == 1, 0x08208208u))
                .Concat(PerLane(32, static t => t % 3 == 1 || (t % 3 == 0 && (t & 1) == 0), 0xD34D34D3u))
                .Concat(PerLane(32, static t => t % 3 == 2 || (t % 3 == 0 && (t & 1) == 1), 0x2CB2CB2Cu))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray(),
            static i => i & 31);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_BackwardGotoToJoinLabel_ReconvergesOnceAtTheJoin() =>
        RunOnEveryPath(160,
            static output => WarpActiveMaskDifferentialKernels.LaunchBackwardGotoToJoinLabelKernel(
                output, 0, blockDimX: 32),
            PerLane(32, static t => (t & 1) == 0, 0x55555555u)
                .Concat(PerLane(32, static t => (t & 1) == 1, 0xAAAAAAAAu))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .Concat(PerLane(32, static _ => true, 2u))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    // t % 3 == 0 (A) and the lanes of section 1 that jump to case 0 (t & 4 == 0, t & 8 != 0) meet at case 0; the other
    // lanes of section 1 and the default lanes meet at shared, those of section 1 by a forward goto. CUDA keeps the lanes
    // that reach shared without a jump (0x00090002) apart from those that jump there (0xB4D269B4)
    private static uint[] ForwardGotoToSwitchJoinLabelExpected() =>
        PerLane(32, static t => t % 3 == 0 || (t % 3 == 1 && (t & 12) == 8), 0x4B249649u)
            .Concat(PerLane(32, static t => t % 3 == 1 && (t & 12) == 0, 0x00090002u))
            .Concat(PerLane(32, static t => t % 3 == 2 || (t % 3 == 1 && (t & 12) != 8), 0xB4DB69B6u))
            .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
            .ToArray();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_ForwardGotoToSwitchJoinLabel_ArrivesAtTheRangeWithItsSwitchJoins() =>
        RunWithinClassicBounds(128,
            static output => WarpActiveMaskDifferentialKernels.LaunchForwardGotoToSwitchJoinLabelKernel(
                output, 0, blockDimX: 32),
            ForwardGotoToSwitchJoinLabelExpected(),
            static i => i & 31);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_ForwardGotoToSwitchJoinLabelInDeviceFunction_ArrivesAtTheRangeWithItsSwitchJoins() =>
        RunWithinClassicBounds(128,
            static output => WarpActiveMaskDifferentialKernels.LaunchForwardGotoToSwitchJoinLabelInDeviceFunctionKernel(
                output, 0, blockDimX: 32),
            ForwardGotoToSwitchJoinLabelExpected(),
            static i => i & 31);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_SelfGotoCaseRetry_LoopsBackToTheSectionHead() =>
        RunOnEveryPath(128,
            static output => WarpActiveMaskDifferentialKernels.LaunchSelfGotoCaseRetryKernel(
                output, 0, blockDimX: 32),
            PerLane(32, static t => (t & 3) == 0, 0x55555555u)
                .Zip(PerLane(32, static t => (t & 3) == 2, 0x44444444u), static (a, b) => a | b)
                .Concat(PerLane(32, static t => (t & 1) == 1, 0xAAAAAAAAu))
                .Concat(Enumerable.Range(0, 32).Select(static t => (t & 1) == 1 ? 0u : 1u + (uint)((t >> 1) & 1)))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    // The lanes that take the goto ((t & 3) != 0) write slots 1 and 2 on their own; every lane meets right after the if
    // that holds the goto, inside the block (slot 3), as on the GPU. n is 3 for them, 0 for the others
    private static uint[] GotoLoopEndingInBlockExpected(int allLaneSlots) =>
        PerLane(32, static _ => true, 0xFFFFFFFFu)
            .Concat(PerLane(32, static t => (t & 3) != 0, 0xEEEEEEEEu))
            .Concat(PerLane(32, static t => (t & 3) != 0, 0xEEEEEEEEu))
            .Concat(Enumerable.Range(0, allLaneSlots).SelectMany(static _ => PerLane(32, static _ => true, 0xFFFFFFFFu)))
            .Concat(Enumerable.Range(0, 32).Select(static t => (t & 3) != 0 ? 3u : 0u))
            .ToArray();

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ActiveMask_GotoLoopEndingInBlock_ReconvergesInsideTheBlock() =>
        RunOnEveryPath(192,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoLoopEndingInBlockKernel(
                output, blockDimX: 32),
            GotoLoopEndingInBlockExpected(2));

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_GotoLoopEndingInNestedBlock_ReconvergesInsideTheInnerBlock() =>
        RunOnEveryPath(224,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoLoopEndingInNestedBlockKernel(
                output, blockDimX: 32),
            GotoLoopEndingInBlockExpected(3));

    // The lanes with (t & 3) == 1 (B) take the first goto, those with (t & 3) == 2 (C) the second, the others (A) none.
    // Between the two gotos the loop has not ended: A and C pass it together the first time (0xDDDDDDDD), B and C
    // alone the second time. Every lane meets after the second goto (slot 4). Slot 1 is not compared: B and C come back
    // to L1 by different backward gotos and meet there on CUDA (0x66666666) but not on the CPU paths: #2263 (Lanes
    // that come back to a goto loop's label by different backward gotos do not meet there on the CPU paths)
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_GotoLoopEndingInBlockWithTwoGotos_ReconvergesAfterTheLastGoto() =>
        RunOnEveryPathIgnoring(224,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoLoopEndingInBlockWithTwoGotosKernel(
                output, blockDimX: 32),
            PerLane(32, static _ => true, 0xFFFFFFFFu)
                .Concat(Enumerable.Range(0, 32).Select(static t => (t & 3) switch
                {
                    1 => 0x22222222u,
                    2 => 0x44444444u,
                    _ => 0u
                }))
                .Concat(PerLane(32, static _ => false, 0u))
                .Concat(Enumerable.Range(0, 32).Select(static t => (t & 3) switch
                {
                    1 => 0x22222222u,
                    2 => 0x44444444u,
                    _ => 0xDDDDDDDDu
                }))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .Concat(Enumerable.Range(0, 32).Select(static t => (t & 3) is 1 or 2 ? 2u : 0u))
                .ToArray(),
            ignoredFrom: 32,
            ignoredTo: 64);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_GotoLoopEndingInIf_ReconvergesAfterTheGotoInsideTheIf() =>
        RunOnEveryPath(192,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoLoopEndingInIfKernel(
                output, blockDimX: 32),
            PerLane(32, static _ => true, 0xFFFFFFFFu)
                .Concat(PerLane(32, static t => (t & 4) == 0 && (t & 3) != 0, 0x0E0E0E0Eu))
                .Concat(PerLane(32, static t => (t & 4) == 0 && (t & 3) != 0, 0x0E0E0E0Eu))
                .Concat(PerLane(32, static t => (t & 4) == 0, 0x0F0F0F0Fu))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .Concat(PerLane(32, static t => (t & 4) == 0 && (t & 3) != 0, 3u))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_GotoLoopEndingInIfElse_ReconvergesInTheBranchOfTheGoto() =>
        RunOnEveryPath(224,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoLoopEndingInIfElseKernel(
                output, blockDimX: 32),
            PerLane(32, static _ => true, 0xFFFFFFFFu)
                .Concat(PerLane(32, static t => (t & 4) == 0 && (t & 3) != 0, 0x0E0E0E0Eu))
                .Concat(PerLane(32, static t => (t & 4) == 0 && (t & 3) != 0, 0x0E0E0E0Eu))
                .Concat(PerLane(32, static t => (t & 4) == 0, 0x0F0F0F0Fu))
                .Concat(PerLane(32, static t => (t & 4) != 0, 0xF0F0F0F0u))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .Concat(PerLane(32, static t => (t & 4) == 0 && (t & 3) != 0, 3u))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_GotoLoopEndingInNestedIf_ReconvergesAtEachLevel() =>
        RunOnEveryPath(224,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoLoopEndingInNestedIfKernel(
                output, blockDimX: 32),
            PerLane(32, static _ => true, 0xFFFFFFFFu)
                .Concat(PerLane(32, static t => (t & 12) == 0 && (t & 3) != 0, 0x000E000Eu))
                .Concat(PerLane(32, static t => (t & 12) == 0 && (t & 3) != 0, 0x000E000Eu))
                .Concat(PerLane(32, static t => (t & 12) == 0, 0x000F000Fu))
                .Concat(PerLane(32, static t => (t & 4) == 0, 0x0F0F0F0Fu))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .Concat(PerLane(32, static t => (t & 12) == 0 && (t & 3) != 0, 3u))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_GotoLoopEndingInElseIf_ReconvergesInTheMiddleBranch() =>
        RunOnEveryPath(256,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoLoopEndingInElseIfKernel(
                output, blockDimX: 32),
            PerLane(32, static _ => true, 0xFFFFFFFFu)
                .Concat(PerLane(32, static t => (t & 12) == 8 && (t & 3) != 0, 0x0E000E00u))
                .Concat(PerLane(32, static t => (t & 12) == 8 && (t & 3) != 0, 0x0E000E00u))
                .Concat(PerLane(32, static t => (t & 12) == 0, 0x000F000Fu))
                .Concat(PerLane(32, static t => (t & 12) == 8, 0x0F000F00u))
                .Concat(PerLane(32, static t => (t & 4) != 0, 0xF0F0F0F0u))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .Concat(PerLane(32, static t => (t & 12) == 8 && (t & 3) != 0, 3u))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_NestedGotoLoopsEndingInIf_ReconvergesInsideTheIfOnEveryOuterPass() =>
        RunOnEveryPath(320,
            static output => WarpActiveMaskDifferentialKernels.LaunchNestedGotoLoopsEndingInIfKernel(
                output, blockDimX: 32),
            PerLane(32, static _ => true, 0xFFFFFFFFu)
                .Concat(PerLane(32, static t => (t & 2) != 0, 0xCCCCCCCCu))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .Concat(PerLane(32, static t => (t & 4) == 0 && (t & 1) != 0, 0x0A0A0A0Au))
                .Concat(PerLane(32, static t => (t & 2) != 0, 0xCCCCCCCCu))
                .Concat(PerLane(32, static t => (t & 7) == 3, 0x08080808u))
                .Concat(PerLane(32, static t => (t & 4) == 0, 0x0F0F0F0Fu))
                .Concat(PerLane(32, static t => (t & 4) == 0 && (t & 2) != 0, 0x0C0C0C0Cu))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .Concat(PerLane(32, static t => (t & 2) != 0, 1u))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_GotoLoopEndingInIfInDeviceFunction_ReconvergesInsideTheIf() =>
        RunOnEveryPath(192,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoLoopEndingInIfInDeviceFunctionKernel(
                output, blockDimX: 32),
            PerLane(32, static _ => true, 0xFFFFFFFFu)
                .Concat(PerLane(32, static t => (t & 4) == 0 && (t & 3) != 0, 0x0E0E0E0Eu))
                .Concat(PerLane(32, static t => (t & 4) == 0 && (t & 3) != 0, 0x0E0E0E0Eu))
                .Concat(PerLane(32, static t => (t & 4) == 0, 0x0F0F0F0Fu))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_GotoLoopEndingInIfThenForwardGoto_ReconvergesBeforeTheForwardGoto() =>
        RunOnEveryPath(192,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoLoopEndingInIfThenForwardGotoKernel(
                output, blockDimX: 32),
            PerLane(32, static _ => true, 0xFFFFFFFFu)
                .Concat(PerLane(32, static t => (t & 4) == 0 && (t & 3) != 0, 0x0E0E0E0Eu))
                .Concat(PerLane(32, static t => (t & 4) == 0 && (t & 3) != 0, 0x0E0E0E0Eu))
                .Concat(PerLane(32, static t => (t & 4) == 0, 0x0F0F0F0Fu))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .Concat(Enumerable.Range(0, 32).Select(static t => (t & 4) != 0 ? 8u : (t & 3) != 0 ? 3u : 0u))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_GotoLoopLeftByForwardGotoBlock_RunsTheBlockPerPass() =>
        RunOnEveryPath(224,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoLoopLeftByForwardGotoBlockKernel(
                output, blockDimX: 32),
            PerLane(32, static _ => true, 0xFFFFFFFFu)
                .Concat(PerLane(32, static t => (t & 7) != 0, 0xFEFEFEFEu))
                .Concat(PerLane(32, static t => (t & 7) > 1, 0xFCFCFCFCu))
                .Concat(PerLane(32, static t => (t & 7) > 2, 0xF8F8F8F8u))
                .Concat(Enumerable.Range(0, 32).Select(static t => (t & 4) == 0 ? 0x01010101u << (t & 3) : 0u))
                .Concat(PerLane(32, static t => (t & 4) != 0, 0xF0F0F0F0u))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .ToArray());

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_GotoLoopLeftByElseOfBackGoto_ReconvergesInTheElse() =>
        RunOnEveryPath(224,
            static output => WarpActiveMaskDifferentialKernels.LaunchGotoLoopLeftByElseOfBackGotoKernel(
                output, 0, blockDimX: 32),
            PerLane(32, static t => (t & 1) == 0, 0xFFFFFFFFu)
                .Concat(PerLane(32, static t => (t & 3) == 1, 0xFFFFFFFFu))
                .Concat(PerLane(32, static t => (t & 3) == 3, 0xFFFFFFFFu))
                .Concat(Enumerable.Range(0, 32).Select(static t => (t & 3) == 3 ? 2u : (t & 1) != 0 ? 1u : 0u))
                .Concat(PerLane(32, static _ => true, 0xFFFFFFFFu))
                .Concat(PerLane(32, static t => (t & 1) != 0, 0xAAAAAAAAu))
                .Concat(PerLane(32, static t => (t & 3) == 3, 0x88888888u))
                .ToArray());

    // RunOnEveryPath with the entries from ignoredFrom up to ignoredTo left out of the comparison
    private static void RunOnEveryPathIgnoring(int outputLength, Action<uint[]> launch, uint[] expected,
        int ignoredFrom, int ignoredTo)
    {
        foreach (var path in DiffPaths.Available)
        {
            path.Preflight?.Invoke();
            var output = new uint[outputLength];
            path.Run(() => launch(output));
            for (int i = 0; i < outputLength; i++)
            {
                Assert.True(
                    (i >= ignoredFrom && i < ignoredTo) || output[i] == expected[i],
                    $"{path.Name}: entry {i} is 0x{output[i]:X8}, expected 0x{expected[i]:X8}; got [{Format(output)}]");
            }
        }
    }
}
