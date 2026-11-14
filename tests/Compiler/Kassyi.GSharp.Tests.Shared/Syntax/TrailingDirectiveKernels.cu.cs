#define TRAILING_DIRECTIVE_TEST_ENABLED
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public static partial class TrailingDirectiveKernels
{
    [GpuDeviceFunction]
    public static int Identity(int x) => x;

    // 1. Single-state kernel with #pragma warning disable / restore around final statement
    [GpuKernel]
    public static void TrailingPragmaNoBarrierKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        data[tid] = tid;
#pragma warning disable CS0219 // Variable is assigned but its value is never used
        int unused = 42;
#pragma warning restore CS0219
    }

    // 2. Multi-state kernel with barrier (Gpu.SyncThreads()) and trailing #pragma warning restore
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

    // 3. Kernel with #if / #endif opened inside body and closed at end of body
    [GpuKernel]
    public static void TrailingIfEndIfKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        data[tid] = tid;
#if TRAILING_DIRECTIVE_TEST_ENABLED
        data[tid] += 100;
#endif
    }

    // 4. Kernel with #region / #endregion opened inside body and closed at end of body
    [GpuKernel]
    public static void TrailingRegionKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        data[tid] = tid;
#region TrailingBlockRegion
        data[tid] += 200;
#endregion
    }
}
