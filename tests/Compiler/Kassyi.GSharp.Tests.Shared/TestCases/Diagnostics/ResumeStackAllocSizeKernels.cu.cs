using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Diagnostics;

// A stackalloc whose size is not a constant is a dynamic array size (GSH0003) at its size, with or without a barrier
// after it (the CPU dialects declare a local that a barrier follows again on every resume)
[SkipSnapshotConsistency("Sad Path")]
public static partial class ResumeStackAllocSizeKernels
{
    [GpuKernel]
    public static void RuntimeSizeSpanKernel(int[] a, int n)
    {
        Span<int> s = stackalloc int[n];
        s[0] = 1;
        a[0] = s[0];
        Gpu.SyncThreads();
        a[1] = 2;
    }

    [GpuKernel]
    public static unsafe void RuntimeSizePointerKernel(int[] a, int n)
    {
        int* p = stackalloc int[n];
        p[0] = 1;
        a[0] = p[0];
        Gpu.SyncThreads();
        a[1] = 2;
    }

    [GpuKernel]
    public static void ConstexprSizeKernel(int[] a, [GpuConstexpr] int n)
    {
        Span<int> s = stackalloc int[n];
        s[0] = 1;
        a[0] = s[0];
        Gpu.SyncThreads();
        a[1] = 2;
    }

    [GpuKernel]
    public static unsafe void PointerWithoutBarrierKernel(int[] a, int n)
    {
        int* p = stackalloc int[n];
        p[0] = 1;
        a[0] = p[0];
    }

    [GpuKernel]
    public static unsafe void CastStackAllocKernel(int[] a)
    {
        int* p = (int*)stackalloc byte[16];
        p[0] = 1;
        a[0] = p[0];
        Gpu.SyncThreads();
        a[1] = 2;
    }
}
