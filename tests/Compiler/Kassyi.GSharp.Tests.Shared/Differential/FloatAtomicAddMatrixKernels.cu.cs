#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Kernels for differential testing of <c>Gpu.AtomicAdd(ref float, float)</c> and
/// <c>Gpu.ReductionAdd(ref float, float)</c> on global memory, static shared memory, and dynamic shared memory (#868).
/// </summary>
public static partial class FloatAtomicAddMatrixKernels
{
    public const int StaticSharedCount = 64;

    public static readonly string[] Columns =
    [
        "global.stored",
        "global.old",
        "global.reduction",
        "staticSmem.stored",
        "staticSmem.old",
        "staticSmem.reduction",
        "dynamicSmem.stored",
        "dynamicSmem.old",
        "dynamicSmem.reduction",
    ];

    [GpuKernel]
    public static void FloatAtomicAddMatrixKernel(float[] left, float[] right, float[] results)
    {
        int tid = ThreadIndex.X;
        int row = BlockIndex.X * BlockDimension.X + tid;
        var staticStored = new SharedMemory<float>(StaticSharedCount);
        var staticReduction = new SharedMemory<float>(StaticSharedCount);
        var dynamicStored = new SharedMemory<float>(BlockDimension.X);
        var dynamicReduction = new SharedMemory<float>(BlockDimension.X);

        if (row >= left.Length)
        {
            return;
        }

        float a = left[row];
        float b = right[row];
        int o = row * 9;

        // Global memory: PTX atom.global.add.f32 / red.global.add.f32 flushes subnormal inputs and results to
        // sign-preserving zero, stores canonical NaN (0x7FFFFFFF) for NaN results, and returns the un-flushed old bits.
        results[o] = a;
        results[o + 1] = Gpu.AtomicAdd(ref results[o], b);
        results[o + 2] = a;
        Gpu.ReductionAdd(ref results[o + 2], b);

        // Static shared memory: PTX atom.shared.add.f32 / red.shared.add.f32 preserves subnormal inputs and results,
        // stores canonical NaN (0x7FFFFFFF) for NaN results, and returns the un-flushed old bits.
        staticStored[tid] = a;
        results[o + 4] = Gpu.AtomicAdd(ref staticStored[tid], b);
        results[o + 3] = staticStored[tid];

        staticReduction[tid] = a;
        Gpu.ReductionAdd(ref staticReduction[tid], b);
        results[o + 5] = staticReduction[tid];

        // Dynamic shared memory: placed in the launch's sharedMemBytes, also in the shared state space.
        dynamicStored[tid] = a;
        results[o + 7] = Gpu.AtomicAdd(ref dynamicStored[tid], b);
        results[o + 6] = dynamicStored[tid];

        dynamicReduction[tid] = a;
        Gpu.ReductionAdd(ref dynamicReduction[tid], b);
        results[o + 8] = dynamicReduction[tid];
    }
}
