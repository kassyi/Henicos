using System;
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

[SkipSnapshotConsistency("Analyzer-only case")]
public static partial class StackModelLiftKernels
{
    [GpuKernel]
    public static void SpanLocalKernel(int[] data, int[] output)
    {
        Span<int> localSpan = data.AsSpan();
        localSpan[0] = 1;
        Gpu.SyncThreads();
        output[ThreadIndex.X] = localSpan[1];
    }

    [GpuKernel]
    public static void StackLinqChainKernel(int[] data, int[] output)
    {
        var chain = data.AsStackEnumerable().Where(static x => x > 0);
        Gpu.SyncThreads();
        output[ThreadIndex.X] = chain.Sum();
    }

    [GpuKernel]
    public static void RefLocalKernel(int[] data)
    {
        ref int first = ref data[0];
        first = 10;
        Gpu.SyncThreads();
        first = 20;
    }

    [GpuDeviceFunction]
    public static int ChangedSpanParameterDeviceFunction(Span<int> s)
    {
        s = s.Slice(1);
        Gpu.SyncThreads();
        return s[0];
    }

    [GpuKernel]
    public static void RefForEachKernel(int[] data)
    {
        foreach (ref int element in data.AsSpan(ThreadIndex.X, 1))
        {
            Gpu.SyncThreads();
            element += 1;
        }
    }

    [GpuKernel]
    public static unsafe void LocalAddressPointerKernel(int[] output)
    {
        int localVal = 42;
        int* ptr = &localVal;
        Gpu.SyncThreads();
        output[ThreadIndex.X] = *ptr;
    }

    [GpuKernel]
    public static unsafe void StackallocPointerKernel(int[] output)
    {
        int* localArr = stackalloc int[4];
        localArr[0] = 1;
        Gpu.SyncThreads();
        output[ThreadIndex.X] = localArr[0];
    }

    [GpuDeviceFunction]
    public static int RecursiveStep(int n)
    {
        Gpu.SyncThreads();
        return n > 0 ? RecursiveStep(n - 1) : 0;
    }

    [GpuKernel]
    public static void FallbackRecursiveSpanKernel(int[] data, int[] output)
    {
        Span<int> held = data.AsSpan();
        int step = RecursiveStep(1);
        output[ThreadIndex.X] = held[0] + step;
    }

    [GpuKernel]
    public static unsafe void FallbackRecursivePointerKernel(int[] output)
    {
        int* heldPtr = stackalloc int[2];
        heldPtr[0] = 7;
        int step = RecursiveStep(1);
        output[ThreadIndex.X] = heldPtr[0] + step;
    }
}
