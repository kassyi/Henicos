// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// A local const whose declaration a goto jumps over, read after a barrier. CPU state machine only: the C++ that the
/// CUDA path and the native one-stack model write splits the declaration into a declaration and an assignment, which
/// the jump skips.
/// </summary>
[SkipSnapshotConsistency("CPU state machine only; the CUDA and one-stack C++ of this form read an uninitialized local")]
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
}
