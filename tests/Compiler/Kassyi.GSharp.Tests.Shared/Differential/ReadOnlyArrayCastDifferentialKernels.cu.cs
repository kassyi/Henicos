#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// A kernel that casts a <c>[GpuReadOnly]</c> array to its own array type and keeps the result across a barrier.
/// </summary>
public static partial class ReadOnlyArrayCastDifferentialKernels
{
    [GpuKernel]
    public static void CastLocalKernel([GpuReadOnly] int[] source, int[] output)
    {
        int[] view = (int[])source;
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        Gpu.SyncThreads();
        if (tid < source.Length)
        {
            output[tid] = view[tid] + 1;
        }
    }
}
