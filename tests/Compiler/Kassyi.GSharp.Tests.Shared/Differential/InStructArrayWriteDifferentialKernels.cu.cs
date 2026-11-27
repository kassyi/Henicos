#nullable enable
using System;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public struct DiffConvertedBag
{
    public DiffBoolPack[] Packs;
    public DiffDeepBag[] Nested;
    public int Tag;
}

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

    [GpuKernel]
    public static void StandaloneInBagArrayWriteKernel(in DiffDeepBag bag, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            bag.Data[1] += 10;
            observed[0] = bag.Data[1];
            observed[1] = bag.Tag;
        }
    }

    [GpuKernel]
    public static void InBagBesideRefBagKernel(in DiffDeepBag readOnlyBag, ref DiffDeepBag mutableBag, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            readOnlyBag.Data[1] += 10;
            mutableBag.Data[0] += 20;
            mutableBag.Tag += 5;
            observed[0] = readOnlyBag.Tag;
            observed[1] = mutableBag.Tag;
        }
    }

    [GpuKernel]
    public static void AliasedInAndRefSameBagKernel(in DiffDeepBag readOnlyBag, ref DiffDeepBag mutableBag, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            readOnlyBag.Data[1] += 10;
            observed[0] = mutableBag.Data[1];
            mutableBag.Tag += 5;
            observed[1] = readOnlyBag.Tag;
        }
    }

    [GpuKernel]
    public static void TwoAliasedInBagsKernel(in DiffDeepBag first, in DiffDeepBag second, [Out] DiffDeepBag[] sink, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            first.Data[1] += 10;
            second.Data[2] += 20;
            observed[0] = second.Data[1];
            observed[1] = first.Data[2];
            observed[2] = first.Tag + (sink.Length > 0 ? sink[0].Tag : 0);
        }
    }

    [GpuKernel]
    public static void InConvertedElementArrayBagKernel(in DiffConvertedBag readOnlyBag, ref DiffConvertedBag mutableBag, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            readOnlyBag.Packs[0].Flag0 = true;
            readOnlyBag.Packs[0].Mid += 10;
            readOnlyBag.Packs[0].Flag1 = false;
            readOnlyBag.Nested[0].Data[0] += 100;
            readOnlyBag.Nested[0].Tag += 5;
            mutableBag.Packs[0].Mid += 20;
            mutableBag.Nested[0].Data[0] += 200;
            mutableBag.Tag += 3;
            observed[0] = readOnlyBag.Tag;
            observed[1] = readOnlyBag.Packs[0].Mid;
            observed[2] = mutableBag.Tag;
        }
    }
}
