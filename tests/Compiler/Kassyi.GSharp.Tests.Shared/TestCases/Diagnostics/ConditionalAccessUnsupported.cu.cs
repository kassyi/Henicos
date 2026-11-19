#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Diagnostics;

public struct ConditionalIndexed
{
    public int X;

    public int this[int i] => X + i;
}

public static partial class ConditionalAccessUnsupported
{
    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void IndexerKernel(int[] dst, int mode)
    {
        ConditionalIndexed? p = mode == 0 ? null : new ConditionalIndexed { X = 5 };
        // GSH0003: the indexer of the value of a Nullable<T>
        int? v = p?[2];
        // No diagnostic: a member of the value
        int? x = p?.X;
        dst[ThreadIndex.X] = (v ?? -1) + (x ?? -1);
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void SegmentKernel(int[] dst, ArraySegment<int> segment)
    {
        ArraySegment<int>? seg = segment;
        // GSH0003 twice: a member and an element of the value of a Nullable<T> of an array view
        int? count = seg?.Count;
        int? first = seg?[1];
        dst[ThreadIndex.X] = (count ?? -1) + (first ?? -1);
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void MemoryKernel(int[] dst, Memory<int> memory)
    {
        Memory<int>? mem = memory;
        // GSH0003: a member of the value of a Nullable<T> of an array view
        int? length = mem?.Length;
        dst[ThreadIndex.X] = length ?? -1;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void VectorKernel(float[] dst, int mode)
    {
        System.Numerics.Vector2? v = mode == 0 ? null : new System.Numerics.Vector2(3f, 4f);
        Float2? f = mode == 0 ? null : new Float2(5f, 6f);
        // GSH0003 twice: a member of the value of a Nullable<T> of a vector
        float? x = v?.X;
        float? y = f?.Y;
        dst[ThreadIndex.X] = (x ?? -1f) + (y ?? -1f);
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void IncrementorKernel(ConditionalIndexed[] items, int[] dst, int mode)
    {
        ConditionalIndexed[]? q = mode == 0 ? null : items;
        int t = ThreadIndex.X;
        // GSH0003: an extension method that returns void, called through ?. outside a statement
        for (int i = 0; i < 2; i++, q?[t].Bump())
        {
            dst[t] += i;
        }
    }
}

public static partial class ConditionalIndexedExtensions
{
    [GpuDeviceFunction]
    public static void Bump(this ref ConditionalIndexed p) => p.X++;
}
