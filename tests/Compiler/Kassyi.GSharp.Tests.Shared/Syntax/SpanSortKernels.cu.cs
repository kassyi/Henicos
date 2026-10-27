using System;
using System.Collections.Generic;
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>An enum whose underlying values are signed and not in declaration order (.NET sorts by the value).</summary>
public enum SortRank : short
{
    High = 7,
    Low = -5,
    Middle = 0
}

/// <summary>Orders by the bits of <see cref="Mask"/> only, so that many keys are equal and their order shows the algorithm.</summary>
public struct MaskedComparer : IComparer<int>
{
    public int Mask;

    public int Compare(int a, int b)
    {
        int x = a & Mask;
        int y = b & Mask;
        return x < y ? -1 : (x > y ? 1 : 0);
    }
}

public struct DescendingComparer : IComparer<int>
{
    public int Compare(int a, int b) => a > b ? -1 : (a < b ? 1 : 0);
}

/// <summary>An inconsistent comparer: every element is less than every other (.NET throws ArgumentException).</summary>
public struct AlwaysLessComparer : IComparer<int>
{
    public int Compare(int a, int b) => -1;
}

/// <summary>A struct key ordered by <see cref="Key"/> only; <see cref="Tag"/> tells equal keys apart.</summary>
public struct SortRecord : IComparable<SortRecord>
{
    public int Key;
    public int Tag;

    public int CompareTo(SortRecord other) => Key < other.Key ? -1 : (Key > other.Key ? 1 : 0);
}

/// <summary>An inconsistent IComparable&lt;T&gt;: every key is less than every other (.NET throws ArgumentException for keys and items).</summary>
public struct InconsistentRecord : IComparable<InconsistentRecord>
{
    public int Key;

    public int CompareTo(InconsistentRecord other) => -1;
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

    [GpuDeviceFunction]
    public static int CompareLowNibble(int a, int b)
    {
        int x = a & 15;
        int y = b & 15;
        return x < y ? -1 : (x > y ? 1 : 0);
    }

    [GpuKernel]
    public static void SortComparerKernel(Span<int> data, int mask)
    {
        if ((ThreadIndex.X | ThreadIndex.Y | ThreadIndex.Z | BlockIndex.X | BlockIndex.Y | BlockIndex.Z) == 0)
        {
            MemoryExtensions.Sort(data, new MaskedComparer { Mask = mask });
        }
    }

    [GpuKernel]
    public static void SortDescendingKernel(Span<int> data)
    {
        if ((ThreadIndex.X | ThreadIndex.Y | ThreadIndex.Z | BlockIndex.X | BlockIndex.Y | BlockIndex.Z) == 0)
        {
            data.Sort(new DescendingComparer());
        }
    }

    [GpuKernel]
    public static void SortComparisonKernel(Span<int> data, int mask)
    {
        if ((ThreadIndex.X | ThreadIndex.Y | ThreadIndex.Z | BlockIndex.X | BlockIndex.Y | BlockIndex.Z) == 0)
        {
            data.Sort((a, b) => (a & mask) < (b & mask) ? -1 : ((a & mask) > (b & mask) ? 1 : 0));
        }
    }

    [GpuKernel]
    public static void SortMethodGroupKernel(Span<int> data)
    {
        if ((ThreadIndex.X | ThreadIndex.Y | ThreadIndex.Z | BlockIndex.X | BlockIndex.Y | BlockIndex.Z) == 0)
        {
            data.Sort(CompareLowNibble);
        }
    }

    // A null comparer and Comparer<T>.Default sort by the default order, as in .NET
    [GpuKernel]
    public static void SortNullComparerKernel(Span<int> data)
    {
        if ((ThreadIndex.X | ThreadIndex.Y | ThreadIndex.Z | BlockIndex.X | BlockIndex.Y | BlockIndex.Z) == 0)
        {
            MemoryExtensions.Sort(data, (IComparer<int>)null);
        }
    }

    [GpuKernel]
    public static void SortComparableKernel(Span<SortRecord> data)
    {
        if ((ThreadIndex.X | ThreadIndex.Y | ThreadIndex.Z | BlockIndex.X | BlockIndex.Y | BlockIndex.Z) == 0)
        {
            data.Sort();
        }
    }

    [GpuKernel]
    public static void SortPairsKernel(Span<int> keys, Span<int> items)
    {
        if ((ThreadIndex.X | ThreadIndex.Y | ThreadIndex.Z | BlockIndex.X | BlockIndex.Y | BlockIndex.Z) == 0)
        {
            MemoryExtensions.Sort(keys, items);
        }
    }

    [GpuKernel]
    public static void SortPairsFloatKernel(Span<float> keys, Span<int> items)
    {
        if ((ThreadIndex.X | ThreadIndex.Y | ThreadIndex.Z | BlockIndex.X | BlockIndex.Y | BlockIndex.Z) == 0)
        {
            keys.Sort(items);
        }
    }

    [GpuKernel]
    public static void SortPairsComparerKernel(Span<int> keys, Span<int> items, int mask)
    {
        if ((ThreadIndex.X | ThreadIndex.Y | ThreadIndex.Z | BlockIndex.X | BlockIndex.Y | BlockIndex.Z) == 0)
        {
            keys.Sort(items, new MaskedComparer { Mask = mask });
        }
    }

    [GpuKernel]
    public static void SortPairsComparisonKernel(Span<int> keys, Span<int> items, int mask)
    {
        if ((ThreadIndex.X | ThreadIndex.Y | ThreadIndex.Z | BlockIndex.X | BlockIndex.Y | BlockIndex.Z) == 0)
        {
            MemoryExtensions.Sort(keys, items, (a, b) => (a & mask) < (b & mask) ? -1 : ((a & mask) > (b & mask) ? 1 : 0));
        }
    }

    [GpuKernel]
    public static void SortPairsComparableKernel(Span<SortRecord> keys, Span<int> items)
    {
        if ((ThreadIndex.X | ThreadIndex.Y | ThreadIndex.Z | BlockIndex.X | BlockIndex.Y | BlockIndex.Z) == 0)
        {
            keys.Sort(items);
        }
    }

    [GpuKernel]
    public static void SortBogusComparerKernel(Span<int> data)
    {
        if ((ThreadIndex.X | ThreadIndex.Y | ThreadIndex.Z | BlockIndex.X | BlockIndex.Y | BlockIndex.Z) == 0)
        {
            data.Sort(new AlwaysLessComparer());
        }
    }

    [GpuKernel]
    public static void SortPairsInconsistentKernel(Span<InconsistentRecord> keys, Span<int> items)
    {
        if ((ThreadIndex.X | ThreadIndex.Y | ThreadIndex.Z | BlockIndex.X | BlockIndex.Y | BlockIndex.Z) == 0)
        {
            keys.Sort(items);
        }
    }

    // The comparison reads table[x] through ElementAt, which throws (traps) for an index out of range
    [GpuKernel]
    public static void SortTrappingComparisonKernel(Span<int> data, int[] table)
    {
        if ((ThreadIndex.X | ThreadIndex.Y | ThreadIndex.Z | BlockIndex.X | BlockIndex.Y | BlockIndex.Z) == 0)
        {
            data.Sort((a, b) => table.AsStackEnumerable().ElementAt(a) < table.AsStackEnumerable().ElementAt(b) ? -1 : 1);
        }
    }
}
