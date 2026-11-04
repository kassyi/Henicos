#nullable enable
using System;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for open issues (#904, #1091) and regression coverage (#1080, StackLinq Take(0)).
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class PendingBugsDifferentialTests
{
    private static readonly DifferentialCase s_selectTakeZeroSideEffect =
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

    private static readonly DifferentialCase s_forUnrollOverflowBound =
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

    private static readonly DifferentialCase s_gridStrideLoopUniformWarpShuffle =
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

    private static readonly DifferentialCase s_zeroSharedMemBytes =
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

    private static readonly DifferentialCase s_growthAfterLaterDecl =
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

    private static readonly DifferentialCase s_negativeCount =
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
    public void Test_SelectTakeZeroAndNegativeSideEffects_AreZeroOnEveryPath() =>
        DifferentialRunner.Run(s_selectTakeZeroSideEffect);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ForUnrollWithOverflowingStartPlusCount_ExecutesZeroIterationsOnEveryPath() =>
        DifferentialRunner.Run(s_forUnrollOverflowBound);

    // #1091: Remove KnownDivergence entries and unskip CPU once Warp.ShuffleDownSync inside Gpu.GridStrideLoop is rejected or lowered.
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_WarpShuffleDownInGridStrideLoop_UniformIterationCount()
    {
#if !GSHARP_CUDA
        Skip.If(true, "#1091: Managed path throws GpuOnlyException for Warp.ShuffleDownSync inside GridStrideLoop.");
#endif
        DifferentialRunner.Run(s_gridStrideLoopUniformWarpShuffle);
    }

    // #1091: Partial last wave in Gpu.GridStrideLoop with Warp.ShuffleDownSync(0xFFFFFFFFu, ...) is undefined on CUDA;
    // verifies what each path does without comparing undefined values.
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_WarpShuffleDownInGridStrideLoop_PartialLastWave_ObservesPathsWithoutComparingUndefinedValues()
    {
#if GSHARP_CUDA
        CudaCapability.EnsureGpuAvailable();
#endif
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
            if (pathRunner.Path == DiffPath.Managed)
            {
                Assert.Throws<GpuOnlyException>(() =>
                    pathRunner.Run(
                        () => PendingBugsDifferentialKernels.LaunchGridStrideLoopPartialWaveWarpShuffleKernel(
                            input, output, activeCount, gridDimX: 1, blockDimX: 64)));
            }
            else if (pathRunner.Path == DiffPath.Native)
            {
                pathRunner.Run(
                    () => PendingBugsDifferentialKernels.LaunchGridStrideLoopPartialWaveWarpShuffleKernel(
                        input, output, activeCount, gridDimX: 1, blockDimX: 64));
                // Native CPU fallback __shfl_down_sync returns the thread's own value for every iteration
                Assert.Equal(input, output);
            }
            else if (pathRunner.Path == DiffPath.Cuda)
            {
                pathRunner.Run(
                    () => PendingBugsDifferentialKernels.LaunchGridStrideLoopPartialWaveWarpShuffleKernel(
                        input, output, activeCount, gridDimX: 1, blockDimX: 64));
                // Iteration 0 (indices 0..63) has all 32 threads of both warps active
                for (int i = 0; i < 64; i++)
                {
                    int laneId = i & 31;
                    int expected = laneId + 1 < 32 ? input[i + 1] : input[i];
                    Assert.Equal(expected, output[i]);
                }

                // Iteration 1 (indices 64..79) has only threads 0..15 of warp 0 active;
                // threads 0..14 read from active thread + 1, while thread 15 reads inactive thread 16 (undefined on GPU).
                for (int i = 64; i < 79; i++)
                {
                    Assert.Equal(input[i + 1], output[i]);
                }
            }
        }
    }

    // #904: Remove KnownDivergence entry once ManagedVersionAnalysis rewrites non-suspending helpers that allocate dynamic SharedMemory<T>(count).
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Test_DynamicSharedMemoryInNonSuspendingHelper_ZeroSharedMemBytes_DivergesOnManaged() =>
        DifferentialRunner.Run(s_zeroSharedMemBytes);

    // #904: Remove KnownDivergence entry once ManagedVersionAnalysis rewrites non-suspending helpers that allocate dynamic SharedMemory<T>(count).
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Test_DynamicSharedMemoryInNonSuspendingHelper_GrowthAfterLaterDecl_DivergesOnManaged() =>
        DifferentialRunner.Run(s_growthAfterLaterDecl);

    // #904: Remove KnownDivergence entry once ManagedVersionAnalysis rewrites non-suspending helpers that allocate dynamic SharedMemory<T>(count).
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Test_DynamicSharedMemoryInNonSuspendingHelper_NegativeCount_DivergesOnManaged() =>
        DifferentialRunner.Run(s_negativeCount);
}
