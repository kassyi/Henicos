using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// GSH0003: a kernel named as a method group, and delegate combination
public static partial class KernelMethodGroupKernels
{
    [GpuDeviceFunction]
    public static void Set(int[] data) => data[0] = 1;

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void CalleeKernel(int[] data)
    {
        data[ThreadIndex.X] += 1;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void MethodGroupKernel(int[] data)
    {
        Action<int[]> typed = CalleeKernel;
        var inferred = KernelMethodGroupKernels.CalleeKernel;
        Action<int[]> device = Set;
        device(data);
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void CombineKernel(int[] data)
    {
        Action<int[]> first = Set;
        first += Set;
        first -= Set;
        Action<int[]> both = first + first;
        Action<int[]> plain = first;
        plain(data);
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void NullCombineKernel(int[] data)
    {
        Action<int[]> first = Set;
        Action<int[]> viaNull = null + first;
        Action<int[]> removed = first - viaNull;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void HeapCastKernel(int[] data)
    {
        Action<int[]> device = Set;
        var boxed = (object)Set;
        var viaDelegate = (Delegate)device;
    }
}
