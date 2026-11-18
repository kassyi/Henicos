#define TRAILING_DIRECTIVE_TEST_ENABLED
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public static partial class TrailingDirectiveKernels
{
    // (a) Single-state kernel without barrier; trailing restore after last statement.
    // Also contains #if/#endif and #region/#endregion blocks for regression verification.
    [GpuKernel]
    public static void TrailingPragmaNoBarrierKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        data[tid] = tid;
#if TRAILING_DIRECTIVE_TEST_ENABLED
        data[tid] += 100;
#endif
#region TrailingBlockRegion
        data[tid] += 200;
#endregion
#pragma warning disable CS0219 // Variable is assigned but its value is never used
        int unused = 42;
#pragma warning restore CS0219
    }

    // (b) Multi-state kernel with barrier; trailing restore after last statement.
    [GpuKernel]
    public static void TrailingPragmaWithBarrierKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        data[tid] = tid + 1;
        Gpu.SyncThreads();
#pragma warning disable CS0219
        int unused = data[tid] + 10;
#pragma warning restore CS0219
    }

    // (c) Multi-state kernel with barrier inside for loop; restore at end of loop body.
    [GpuKernel]
    public static void TrailingPragmaLoopWithBarrierKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        for (int i = 0; i < 2; i++)
        {
            data[tid] += i;
            Gpu.SyncThreads();
#pragma warning disable CS0219
            int unused = data[tid] + i;
#pragma warning restore CS0219
        }
    }
}
