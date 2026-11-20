// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// A local const whose declaration a goto jumps over. A const has no storage in the C++ output (a reference writes its
/// value), so the jump skips nothing, with or without a barrier.
/// </summary>
public static partial class LocalConstSkippedByGotoKernels
{
    // Thread 0 jumps over the declaration; C# reads the constant on that path too
    [GpuKernel]
    public static void ConstSkippedByGotoKernel(int[] result)
    {
        int tid = ThreadIndex.X;
        if (tid == 0)
        {
            goto After;
        }

        const int K = 3;
        result[tid] = 1;
    After:
        Gpu.SyncThreads();
        result[tid] += K;
    }

    // The same jump in a body that no barrier splits
    [GpuKernel]
    public static void ConstSkippedByGotoNoBarrierKernel(int[] result)
    {
        int tid = ThreadIndex.X;
        if (tid == 0)
        {
            goto After;
        }

        const int K = 3;
        result[tid] = 1;
    After:
        result[tid] += K;
    }
}
