#nullable enable
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for open issues (#904, #1091) and regression coverage (#1080, StackLinq Take(0)).
/// </summary>
[SkipSnapshotConsistency("Pending bug verification")]
public static partial class PendingBugsDifferentialKernels
{
    public const int WindowSize = 4;

    public static readonly string[] SelectTakeZeroColumns =
    [
        "FusedTakeZeroSum",
        "FusedTakeZeroSideEffects",
        "FusedTakeNegSum",
        "FusedTakeNegSideEffects",
        "UnfusedTakeZeroSum",
        "UnfusedTakeZeroSideEffects",
    ];

    public static readonly string[] ForUnrollOverflowColumns =
    [
        "OverflowWithBarrierCount",
        "OverflowNoBarrierCount",
        "ExactMaxValueBoundCount",
    ];

    public static readonly string[] GridStrideUniformShuffleColumns =
    [
        "iter0_down1",
        "iter1_down1",
    ];

    // Verifies that fused and unfused Select(sideEffect).Take(0).Sum() / Take(-1).Sum()
    // do not evaluate the Select lambda on Managed, Native, or CUDA.
    [GpuKernel]
    public static void SelectTakeZeroSideEffectKernel(int[] input, int[] output)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int rowCount = input.Length / WindowSize;
        if (tid >= rowCount)
        {
            return;
        }

        var slice = input.AsSpan().Slice(tid * WindowSize, WindowSize);

        int fusedZeroSideEffects = 0;
        int fusedZeroSum = slice.AsStackEnumerable()
            .Select(x =>
            {
                fusedZeroSideEffects++;
                return x * 2;
            })
            .Take(0)
            .Sum();

        int fusedNegSideEffects = 0;
        int fusedNegSum = slice.AsStackEnumerable()
            .Select(x =>
            {
                fusedNegSideEffects++;
                return x * 2;
            })
            .Take(-1)
            .Sum();

        int unfusedZeroSideEffects = 0;
        var unfusedSource = slice.AsStackEnumerable();
        int unfusedZeroSum = unfusedSource
            .Select(x =>
            {
                unfusedZeroSideEffects++;
                return x * 2;
            })
            .Take(0)
            .Sum();

        int baseOut = tid * 6;
        output[baseOut + 0] = fusedZeroSum;
        output[baseOut + 1] = fusedZeroSideEffects;
        output[baseOut + 2] = fusedNegSum;
        output[baseOut + 3] = fusedNegSideEffects;
        output[baseOut + 4] = unfusedZeroSum;
        output[baseOut + 5] = unfusedZeroSideEffects;
    }

    // #1080: When start + count overflows Int32 in Gpu.ForUnroll(int.MaxValue - 1, 4, ...),
    // C# evaluates `int end = start + count` (wrapping to negative) so `i < end` is false on the first check (0 iterations).
    // Verifies that Managed, Native, and CUDA all execute the body 0 times (with and without a barrier),
    // while Gpu.ForUnroll(int.MaxValue - 4, 4, ...) executes 4 times.
    [GpuKernel]
    public static void ForUnrollOverflowBoundKernel(int[] output)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;

        int overflowWithBarrier = 0;
        Gpu.ForUnroll(int.MaxValue - 1, 4, i =>
        {
            Gpu.SyncThreads();
            overflowWithBarrier++;
        });

        int overflowNoBarrier = 0;
        Gpu.ForUnroll(int.MaxValue - 1, 4, i =>
        {
            overflowNoBarrier++;
        });

        int exactLimitWithBarrier = 0;
        Gpu.ForUnroll(int.MaxValue - 4, 4, i =>
        {
            Gpu.SyncThreads();
            exactLimitWithBarrier++;
        });

        int baseOut = tid * 3;
        output[baseOut + 0] = overflowWithBarrier;
        output[baseOut + 1] = overflowNoBarrier;
        output[baseOut + 2] = exactLimitWithBarrier;
    }

    // #1091: Direct Warp.ShuffleDownSync inside Gpu.GridStrideLoop body.
    // Case (i): n is 2 * totalThreads (each thread runs 2 iterations; all 32 lanes of every warp are active in both iterations).
    [GpuKernel]
    public static void GridStrideLoopUniformWarpShuffleKernel(int[] input, int[] output)
    {
        int totalThreads = GridDimension.X * BlockDimension.X;
        int n = totalThreads * 2;
        Gpu.GridStrideLoop(n, i =>
        {
            int v = input[i];
            int shuffled = Warp.ShuffleDownSync(0xFFFFFFFFu, v, 1);
            int tid = i % totalThreads;
            int step = i / totalThreads;
            output[tid * 2 + step] = shuffled;
        });
    }

    // #1091: Direct Warp.ShuffleDownSync inside Gpu.GridStrideLoop body.
    // Case (ii): n is not a multiple of totalThreads (the last iteration has only a subset of lanes active).
    [GpuKernel]
    public static void GridStrideLoopPartialWaveWarpShuffleKernel(int[] input, int[] output, int activeCount)
    {
        Gpu.GridStrideLoop(activeCount, i =>
        {
            int v = input[i];
            output[i] = Warp.ShuffleDownSync(0xFFFFFFFFu, v, 1);
        });
    }

    // #904: Non-suspending [GpuDeviceFunction] allocating dynamic SharedMemory<int>(count).
    [GpuDeviceFunction]
    public static int AllocateDynamicInNonSuspendingHelper(int count, int value)
    {
        var s = new SharedMemory<int>(count);
        if (count > 0)
        {
            s[0] = value;
            return s[0];
        }

        return 0;
    }

    [GpuKernel]
    public static void HelperDynamicSharedMemoryZeroBytesKernel(int[] output, int dynamicCount)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid == 0)
        {
            output[0] = AllocateDynamicInNonSuspendingHelper(dynamicCount, 42);
        }
    }

    [GpuKernel]
    public static void HelperDynamicSharedMemoryGrowthAfterLaterDeclKernel(int[] output, int firstCount, int secondCount)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid == 0)
        {
            int first = AllocateDynamicInNonSuspendingHelper(firstCount, 10);
            var middle = new SharedMemory<int>(firstCount);
            middle[0] = 30;
            int second = AllocateDynamicInNonSuspendingHelper(secondCount, 20);
            output[0] = first + middle[0] + second;
        }
    }

    [GpuKernel]
    public static void HelperDynamicSharedMemoryNegativeCountKernel(int[] output, int negCount)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid == 0)
        {
            output[0] = AllocateDynamicInNonSuspendingHelper(negCount, 99);
        }
    }
}
