#nullable enable
using System;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

[SkipSnapshotConsistency("Differential execution tests for writing arrays held by an in struct")]
public static unsafe partial class InStructArrayWriteDifferentialKernels
{
    [GpuDeviceFunction]
    private static void AddSecondElementThroughInBag(in DiffDeepBag bag)
    {
        bag.Data[1] += 10;
    }

    [GpuDeviceFunction]
    private static void IncrementSpanElementsThroughIn(in Span<int> data)
    {
        foreach (ref int element in data)
        {
            element += 1;
        }
    }

    [GpuDeviceFunction]
    private static void AddSecondElementThroughFixedInBag(in DiffDeepBag bag)
    {
        fixed (int* p = bag.Data)
        {
            p[1] += 10;
        }
    }

    [GpuKernel]
    public static void DeviceInStructArrayWriteKernel(DiffDeepBag bag, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            AddSecondElementThroughInBag(in bag);
            observed[0] = bag.Data[1];
            observed[1] = bag.Tag;
        }
    }

    [GpuKernel]
    public static void DeviceInSpanForEachRefWriteKernel(DiffDeepBag bag, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            Span<int> span = bag.Data.AsSpan();
            IncrementSpanElementsThroughIn(in span);
            observed[0] = bag.Data[0];
            observed[1] = bag.Data[1];
            observed[2] = bag.Data[2];
        }
    }

    [GpuKernel]
    public static void RefReadOnlyLocalStructArrayWriteKernel(DiffDeepBag bag, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            ref readonly DiffDeepBag view = ref bag;
            view.Data[1] += 10;
            observed[0] = bag.Data[1];
            observed[1] = view.Tag;
        }
    }

    [GpuKernel]
    public static void DeviceInStructFixedArrayWriteKernel(DiffDeepBag bag, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            AddSecondElementThroughFixedInBag(in bag);
            observed[0] = bag.Data[1];
            observed[1] = bag.Tag;
        }
    }
}
