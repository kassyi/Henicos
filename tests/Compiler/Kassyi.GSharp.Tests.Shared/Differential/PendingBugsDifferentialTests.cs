#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for open issues (#904) and regression coverage (#1080, #1091, StackLinq Take(0)).
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class PendingBugsDifferentialTests
{
    internal static readonly DifferentialCase s_selectTakeZeroSideEffect =
        new($"{nameof(PendingBugsDifferentialKernels)}.{nameof(PendingBugsDifferentialKernels.SelectTakeZeroSideEffectKernel)}")
        {
            Shape = LaunchShape.ForRows(16, blockX: 8),
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("input", DiffValues.Int32(rng, rows * PendingBugsDifferentialKernels.WindowSize))
                .Out<int>("output", rows, PendingBugsDifferentialKernels.SelectTakeZeroColumns, OutputPolicy.Exact),
            Launch = static (b, s) => PendingBugsDifferentialKernels.LaunchSelectTakeZeroSideEffectKernel(
                b.Get<int>("input"),
                b.Get<int>("output"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static (b, _) =>
            {
                var output = b.Get<int>("output");
                Array.Clear(output);
            },
        };

    internal static readonly DifferentialCase s_forUnrollOverflowBound =
        new($"{nameof(PendingBugsDifferentialKernels)}.{nameof(PendingBugsDifferentialKernels.ForUnrollOverflowBoundKernel)}")
        {
            Shape = LaunchShape.ForRows(64, blockX: 32),
            Seeds = [1],
            Inputs = static (_, rows) => new DiffBuffers()
                .Out<int>("output", rows, PendingBugsDifferentialKernels.ForUnrollOverflowColumns, OutputPolicy.Exact),
            Launch = static (b, s) => PendingBugsDifferentialKernels.LaunchForUnrollOverflowBoundKernel(
                b.Get<int>("output"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static (b, _) =>
            {
                var output = b.Get<int>("output");
                int rows = output.Length / 3;
                for (int tid = 0; tid < rows; tid++)
                {
                    int baseOut = tid * 3;
                    output[baseOut + 0] = 0;
                    output[baseOut + 1] = 0;
                    output[baseOut + 2] = 4;
                }
            },
        };

    internal static readonly DifferentialCase s_gridStrideLoopUniformWarpShuffle =
        new($"{nameof(PendingBugsDifferentialKernels)}.{nameof(PendingBugsDifferentialKernels.GridStrideLoopUniformWarpShuffleKernel)}")
        {
            Shape = LaunchShape.ForRows(128, blockX: 64),
            Seeds = [1],
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("input", DiffValues.Int32(rng, rows * 2))
                .Out<int>("output", rows, PendingBugsDifferentialKernels.GridStrideUniformShuffleColumns, OutputPolicy.Exact),
            Launch = static (b, s) => PendingBugsDifferentialKernels.LaunchGridStrideLoopUniformWarpShuffleKernel(
                b.Get<int>("input"),
                b.Get<int>("output"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static (b, _) =>
            {
                var input = b.Get<int>("input");
                var output = b.Get<int>("output");
                int totalThreads = output.Length / 2;
                for (int step = 0; step < 2; step++)
                {
                    int stepBase = step * totalThreads;
                    for (int tid = 0; tid < totalThreads; tid++)
                    {
                        int laneId = tid & 31;
                        int srcTid = laneId + 1 < 32 ? tid + 1 : tid;
                        output[tid * 2 + step] = input[stepBase + srcTid];
                    }
                }
            },
        };

    internal static readonly DifferentialCase s_zeroSharedMemBytes =
        new($"{nameof(PendingBugsDifferentialKernels)}.{nameof(PendingBugsDifferentialKernels.HelperDynamicSharedMemoryZeroBytesKernel)}")
        {
            Shape = LaunchShape.ForRows(1, blockX: 1),
            Seeds = [1],
            Inputs = static (_, rows) => new DiffBuffers()
                .Out<int>("output", rows, ["val"], OutputPolicy.Exact),
            Launch = static (b, s) => PendingBugsDifferentialKernels.LaunchHelperDynamicSharedMemoryZeroBytesKernel(
                b.Get<int>("output"),
                dynamicCount: 4,
                gridDimX: s.GridX,
                blockDimX: s.BlockX,
                sharedMemBytes: 0
            ),
            ExpectedException = static (_, _) =>
                ExceptionPolicy.Expect<ArgumentOutOfRangeException>(DiffPath.Managed | DiffPath.Native),
        };

    internal static readonly DifferentialCase s_growthAfterLaterDecl =
        new($"{nameof(PendingBugsDifferentialKernels)}.{nameof(PendingBugsDifferentialKernels.HelperDynamicSharedMemoryGrowthAfterLaterDeclKernel)}")
        {
            Shape = LaunchShape.ForRows(1, blockX: 1),
            Seeds = [1],
            Inputs = static (_, rows) => new DiffBuffers()
                .Out<int>("output", rows, ["val"], OutputPolicy.Exact),
            Launch = static (b, s) => PendingBugsDifferentialKernels.LaunchHelperDynamicSharedMemoryGrowthAfterLaterDeclKernel(
                b.Get<int>("output"),
                firstCount: 2,
                secondCount: 8,
                gridDimX: s.GridX,
                blockDimX: s.BlockX,
                sharedMemBytes: 256
            ),
            ExpectedException = static (_, _) =>
                ExceptionPolicy.Expect<InvalidOperationException>(DiffPath.Managed | DiffPath.Native),
        };

    internal static readonly DifferentialCase s_negativeCount =
        new($"{nameof(PendingBugsDifferentialKernels)}.{nameof(PendingBugsDifferentialKernels.HelperDynamicSharedMemoryNegativeCountKernel)}")
        {
            Shape = LaunchShape.ForRows(1, blockX: 1),
            Seeds = [1],
            Inputs = static (_, rows) => new DiffBuffers()
                .Out<int>("output", rows, ["val"], OutputPolicy.Exact),
            Launch = static (b, s) => PendingBugsDifferentialKernels.LaunchHelperDynamicSharedMemoryNegativeCountKernel(
                b.Get<int>("output"),
                negCount: -1,
                gridDimX: s.GridX,
                blockDimX: s.BlockX,
                sharedMemBytes: 64
            ),
            ExpectedException = static (_, _) =>
                ExceptionPolicy.Expect<ArgumentOutOfRangeException>(DiffPath.Managed | DiffPath.Native),
        };

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void StackLinq_SelectTakeZeroAndNegativeSideEffects_AreZeroOnEveryPath() =>
        DifferentialRunner.Run(s_selectTakeZeroSideEffect);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ForUnroll_OverflowingStartPlusCount_ExecutesZeroIterationsOnEveryPath() =>
        DifferentialRunner.Run(s_forUnrollOverflowBound);

    // #1091: the CPU paths turn the grid-stride loop into a loop before the state machine lowering, so the shuffle splits
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void WarpShuffle_GridStrideLoopUniformIterationCount_MatchesOracle() =>
        DifferentialRunner.Run(s_gridStrideLoopUniformWarpShuffle);

    // #1146: A full-mask Warp.ShuffleDownSync in the last, partial wave of a Gpu.GridStrideLoop is undefined on CUDA.
    // Under semantics charter section 3, undefined behavior must be rejected with a diagnostic or run-time exception:
    // lane 15 of the last wave reads lane 16, which has exited. The release CUDA build does not check it.
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void WarpShuffle_GridStrideLoopPartialLastWave_RejectsFullMaskShuffle()
    {
        Skip.If(DiffPaths.Available.All(static p => p.Path == DiffPath.Cuda),
            "The release CUDA build does not check a shuffle that reads a lane that has exited");
        const int activeCount = 80;
        var input = new int[activeCount];
        for (int i = 0; i < activeCount; i++)
        {
            input[i] = (i + 1) * 10;
        }

        foreach (var pathRunner in DiffPaths.Available)
        {
            pathRunner.Preflight?.Invoke();
            var output = new int[activeCount];
            var ex = Assert.Throws<InvalidOperationException>(() =>
                pathRunner.Run(
                    () => PendingBugsDifferentialKernels.LaunchGridStrideLoopPartialWaveWarpShuffleKernel(
                        input, output, activeCount, gridDimX: 1, blockDimX: 64)));
            Assert.Contains("Warp.ShuffleDownSync", ex.Message);
            Assert.Contains("PendingBugsDifferentialKernels.cu.cs", ex.Message);
            Assert.Contains("154", ex.Message);
            Assert.Contains("which has exited", ex.Message);
        }
    }

    // #904: Remove KnownDivergence entry once ManagedVersionAnalysis rewrites non-suspending helpers that allocate dynamic SharedMemory<T>(count).
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void DynamicSharedMemory_NonSuspendingHelperZeroBytes_DivergesOnManaged() =>
        DifferentialRunner.Run(s_zeroSharedMemBytes);

    // #904: Remove KnownDivergence entry once ManagedVersionAnalysis rewrites non-suspending helpers that allocate dynamic SharedMemory<T>(count).
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void DynamicSharedMemory_NonSuspendingHelperGrowthAfterLaterDecl_DivergesOnManaged() =>
        DifferentialRunner.Run(s_growthAfterLaterDecl);

    // The helper is not rewritten on the managed path (#904), so its declaration takes the static allocation there,
    // which reports a negative count like the dynamic placement (#1790)
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void DynamicSharedMemory_NonSuspendingHelperNegativeCount_ThrowsOnEveryPath() =>
        DifferentialRunner.Run(s_negativeCount);
}
