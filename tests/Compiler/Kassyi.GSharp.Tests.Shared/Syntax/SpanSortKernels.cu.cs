using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>An enum whose underlying values are signed and not in declaration order (.NET sorts by the value).</summary>
public enum SortRank : short
{
    High = 7,
    Low = -5,
    Middle = 0
}

public static partial class SpanSortKernels
{
    /// <summary>The length of the stack buffer that each thread of <see cref="SortSegmentsKernel"/> sorts.</summary>
    public const int SegmentLength = 33;

    // [WHY] Only the thread whose six indices are all 0 sorts, so the launch may have any shape and no other thread
    // touches the span.
    [GpuKernel]
    public static void SortIntKernel(Span<int> data)
    {
        if ((ThreadIndex.X | ThreadIndex.Y | ThreadIndex.Z | BlockIndex.X | BlockIndex.Y | BlockIndex.Z) == 0)
        {
            data.Sort();
        }
    }

    [GpuKernel]
    public static void SortLongKernel(Span<long> data)
    {
        if ((ThreadIndex.X | ThreadIndex.Y | ThreadIndex.Z | BlockIndex.X | BlockIndex.Y | BlockIndex.Z) == 0)
        {
            MemoryExtensions.Sort(data);
        }
    }

    [GpuKernel]
    public static void SortUIntKernel(Span<uint> data)
    {
        if ((ThreadIndex.X | ThreadIndex.Y | ThreadIndex.Z | BlockIndex.X | BlockIndex.Y | BlockIndex.Z) == 0)
        {
            data.Sort();
        }
    }

    [GpuKernel]
    public static void SortCharKernel(Span<char> data)
    {
        if ((ThreadIndex.X | ThreadIndex.Y | ThreadIndex.Z | BlockIndex.X | BlockIndex.Y | BlockIndex.Z) == 0)
        {
            data.Sort();
        }
    }

    [GpuKernel]
    public static void SortEnumKernel(Span<SortRank> data)
    {
        if ((ThreadIndex.X | ThreadIndex.Y | ThreadIndex.Z | BlockIndex.X | BlockIndex.Y | BlockIndex.Z) == 0)
        {
            data.Sort();
        }
    }

    [GpuKernel]
    public static void SortFloatKernel(Span<float> data)
    {
        if ((ThreadIndex.X | ThreadIndex.Y | ThreadIndex.Z | BlockIndex.X | BlockIndex.Y | BlockIndex.Z) == 0)
        {
            data.Sort();
        }
    }

    [GpuKernel]
    public static void SortDoubleKernel(Span<double> data)
    {
        if ((ThreadIndex.X | ThreadIndex.Y | ThreadIndex.Z | BlockIndex.X | BlockIndex.Y | BlockIndex.Z) == 0)
        {
            data.Sort();
        }
    }

    // Every thread sorts its own segment in a stack buffer: many sorts of different inputs run at the same time
    [GpuKernel]
    public static void SortSegmentsKernel(int[] data)
    {
        int segment = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        Span<int> buffer = stackalloc int[SegmentLength];
        for (int i = 0; i < SegmentLength; i++)
        {
            buffer[i] = data[segment * SegmentLength + i];
        }

        buffer.Sort();
        for (int i = 0; i < SegmentLength; i++)
        {
            data[segment * SegmentLength + i] = buffer[i];
        }
    }
}
