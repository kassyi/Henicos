using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public struct ByRefParameterStruct
{
    public int A;
    public float B;
}

public static partial class ByRefKernelParameterKernels
{
    [GpuKernel]
    public static void RefStructBarrierKernel(ref ByRefParameterStruct s, int[] o)
    {
        int tid = ThreadIndex.X;
        int a = s.A;
        Gpu.SyncThreads();
        if (tid == 0)
        {
            s.A = a + 10;
            s.B = s.B + 0.5f;
        }

        Gpu.SyncThreads();
        o[tid] = s.A + tid;
    }

    [GpuKernel]
    public static void RefIntBarrierKernel(ref int x, int[] o)
    {
        int tid = ThreadIndex.X;
        int a = x;
        Gpu.SyncThreads();
        if (tid == 0)
        {
            x = a + 10;
        }

        Gpu.SyncThreads();
        o[tid] = x + tid;
    }

    [GpuKernel]
    public static void OutStructBarrierKernel(out ByRefParameterStruct s, int[] o)
    {
        int tid = ThreadIndex.X;
        s = default;
        s.A = 7;
        s.B = 1.5f;
        Gpu.SyncThreads();
        if (tid == 0)
        {
            s.A = s.A + 1;
        }

        Gpu.SyncThreads();
        o[tid] = s.A + tid;
    }

    [GpuKernel]
    public static void RefReadOnlyStructNoBarrierKernel(ref readonly ByRefParameterStruct s, int[] o)
    {
        int tid = ThreadIndex.X;
        o[tid] = s.A + (int)s.B + tid;
    }

    [GpuKernel]
    public static void RefReadOnlyStructBarrierKernel(ref readonly ByRefParameterStruct s, int[] o)
    {
        int tid = ThreadIndex.X;
        int a = s.A;
        Gpu.SyncThreads();
        o[tid] = a + s.A + (int)s.B + tid;
    }

    [GpuKernel]
    public static void InStructBarrierKernel(in ByRefParameterStruct s, int[] o)
    {
        int tid = ThreadIndex.X;
        int a = s.A;
        Gpu.SyncThreads();
        o[tid] = a + s.A + (int)s.B + tid;
    }

    [GpuKernel]
    public static void SpanBarrierKernel(Span<int> sp, int[] o)
    {
        int tid = ThreadIndex.X;
        int a = sp[tid];
        Gpu.SyncThreads();
        sp[tid] = a * 2;
        Gpu.SyncThreads();
        o[tid] = sp[(tid + 1) % sp.Length];
    }

    [GpuKernel]
    public static void ReadOnlySpanBarrierKernel(ReadOnlySpan<int> rs, int[] o)
    {
        int tid = ThreadIndex.X;
        int a = rs[tid];
        Gpu.SyncThreads();
        o[tid] = a + rs[(tid + 1) % rs.Length];
    }
}
