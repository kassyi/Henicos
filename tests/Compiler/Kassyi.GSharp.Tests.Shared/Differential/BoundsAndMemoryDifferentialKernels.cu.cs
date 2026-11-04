#nullable enable
using System;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Four-element inline array used by <see cref="BoundsAndMemoryDifferentialKernels"/> to test <c>[InlineArray]</c>
/// indexing bounds.
/// </summary>
[InlineArray(4)]
public struct DiffInlineBuffer4
{
    private int _element0;
}

/// <summary>
/// Canary-padded wrapper around <see cref="DiffInlineBuffer4"/> so out-of-bounds indices in <c>[-4, 7]</c> stay within
/// stack memory on unchecked native builds while throwing <see cref="IndexOutOfRangeException"/> on managed paths.
/// </summary>
public struct DiffCanaryInlineHolder
{
    public DiffInlineBuffer4 Pre;
    public DiffInlineBuffer4 Buf;
    public DiffInlineBuffer4 Post;
}

/// <summary>
/// Holder struct with a default-null array field used to test <see cref="NullReferenceException"/> traps (#706).
/// </summary>
public struct DiffNullArrayHolder
{
    public int[]? Data;
}

/// <summary>
/// Differential kernels for array, <see cref="Span{T}"/>, <see cref="ReadOnlySpan{T}"/>,
/// <c>Span.Slice</c>, <see cref="ArraySegment{T}"/>, <see cref="Memory{T}"/>, <see cref="SharedMemory{T}"/>,
/// <see cref="InlineArrayAttribute"/>, null array vs optional null array (#706 / #838), Charter §4 bounds-check
/// over-elimination witnesses, and simultaneous multi-block trap contention.
/// </summary>
public static partial class BoundsAndMemoryDifferentialKernels
{
    public const int CanaryPad = 8;
    public const int CanarySliceLength = 16;
    public const int CanaryBackingLength = CanaryPad + CanarySliceLength + CanaryPad;
    public const int SharedTileLength = 16;

    public static readonly string[] BoundaryEdges4PathColumns =
    [
        "arr_first",
        "arr_last",
        "arr_row",
        "span_first_last",
        "empty_slice_at_end_len",
        "stackalloc_edges",
        "inline_array_edges",
    ];

    /// <summary>
    /// All-in-bounds boundary witness kernel comparing 0, <c>Length - 1</c>, empty end-slices, <c>stackalloc</c>, and
    /// <c>[InlineArray]</c> across all 4 paths (<c>SequentialOracle</c>, <c>Managed</c>, <c>Native</c>, <c>Cuda</c>).
    /// </summary>
    [GpuKernel]
    public static void ArrayAndSpanBoundaryEdgesKernel(int[] src, int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int len = src.Length;
        int arrFirst = src[0];
        int arrLast = src[len - 1];
        int arrRow = src[t];

        Span<int> span = src.AsSpan();
        ReadOnlySpan<int> roSpan = src.AsSpan().Slice(0, len);
        int spanFirstLast = span[0] ^ roSpan[len - 1];

        Span<int> emptyAtEnd = span.Slice(len, 0);
        int emptySliceLen = emptyAtEnd.Length;

        Span<int> localStack = stackalloc int[4];
        localStack[0] = arrFirst + 1;
        localStack[1] = arrRow + 2;
        localStack[2] = arrRow + 3;
        localStack[3] = arrLast + 4;
        int stackEdges = localStack[0] + localStack[3];

        DiffInlineBuffer4 inlineBuf = default;
        inlineBuf[0] = arrFirst ^ 0x55;
        inlineBuf[1] = arrRow;
        inlineBuf[2] = arrRow ^ 0xAA;
        inlineBuf[3] = arrLast ^ 0xFF;
        int inlineEdges = inlineBuf[0] + inlineBuf[3];

        int baseIdx = t * 7;
        dst[baseIdx + 0] = arrFirst;
        dst[baseIdx + 1] = arrLast;
        dst[baseIdx + 2] = arrRow;
        dst[baseIdx + 3] = spanFirstLast;
        dst[baseIdx + 4] = emptySliceLen;
        dst[baseIdx + 5] = stackEdges;
        dst[baseIdx + 6] = inlineEdges;
    }

    /// <summary>
    /// 1D array indexing bounds (<c>0</c>, <c>Length - 1</c>, <c>-1</c>, <c>int.MinValue</c>, <c>Length</c>,
    /// <c>int.MaxValue</c>) for both element reads and element writes.
    /// </summary>
    [GpuKernel]
    public static void ArrayIndexBoundsKernel(int[] src, int[] readIndices, int[] writeIndices, int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int readIdx = readIndices[t];
        int writeIdx = writeIndices[t];
        int val = src[readIdx];
        dst[writeIdx] = val + src.Length;
    }

    /// <summary>
    /// <see cref="Span{T}"/> and <see cref="ReadOnlySpan{T}"/> indexing over a canary-padded backing array so
    /// out-of-bounds indices in <c>[-8, sliceLen + 7]</c> safely read canary cells on unchecked native builds while
    /// throwing <see cref="IndexOutOfRangeException"/> on managed paths (#718).
    /// </summary>
    [GpuKernel]
    public static void SpanCanaryIndexBoundsKernel(
        int[] backing,
        int[] indices,
        int[] dst,
        int sliceStart,
        int sliceLen,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        Span<int> span = backing.AsSpan().Slice(sliceStart, sliceLen);
        ReadOnlySpan<int> roSpan = backing.AsSpan().Slice(sliceStart, sliceLen);
        int idx = indices[t];
        int v1 = span[idx];
        int v2 = roSpan[idx];
        dst[t] = v1 + v2 + span.Length;
    }

    /// <summary>
    /// <c>Span.Slice(start)</c> boundary checks (#718).
    /// </summary>
    [GpuKernel]
    public static void SpanSliceStartKernel(
        int[] backing,
        int[] starts,
        int[] dst,
        int sliceStart,
        int sliceLen,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        Span<int> span = backing.AsSpan().Slice(sliceStart, sliceLen);
        Span<int> sub = span.Slice(starts[t]);
        dst[t] = sub.Length;
    }

    /// <summary>
    /// <c>Span.Slice(start, length)</c> boundary and 32-bit <c>start + length</c> wrap-around checks (#718).
    /// </summary>
    [GpuKernel]
    public static void SpanSliceStartCountKernel(
        int[] backing,
        int[] starts,
        int[] counts,
        int[] dst,
        int sliceStart,
        int sliceLen,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        Span<int> span = backing.AsSpan().Slice(sliceStart, sliceLen);
        Span<int> sub = span.Slice(starts[t], counts[t]);
        dst[t] = sub.Length;
    }

    /// <summary>
    /// <see cref="ArraySegment{T}"/> (throws <see cref="ArgumentOutOfRangeException"/> on OOB index, #718).
    /// </summary>
    [GpuKernel]
    public static void ArraySegmentBoundsKernel(
        ArraySegment<int> seg,
        int[] indices,
        int[] dst,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int idx = indices[t];
        dst[t] = seg[idx] + seg.Count;
    }

    /// <summary>
    /// <see cref="Memory{T}"/> and <see cref="ReadOnlyMemory{T}"/> (throws <see cref="IndexOutOfRangeException"/> on OOB index, #718).
    /// </summary>
    [GpuKernel]
    public static void MemoryBoundsKernel(
        Memory<int> mem,
        ReadOnlyMemory<int> roMem,
        int[] indices,
        int[] dst,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int idx = indices[t];
        dst[t] = mem.Span[idx] + roMem.Span[idx];
    }

    /// <summary>
    /// Static <see cref="SharedMemory{T}"/> indexing at <c>0</c>, <c>Length - 1</c>, and out-of-bounds indices.
    /// </summary>
    [GpuKernel]
    public static void SharedMemoryBoundsKernel(int[] indices, int[] dst, int rows)
    {
        var smem = new SharedMemory<int>(SharedTileLength);
        int tid = (int)ThreadIndex.X;
        if (tid < SharedTileLength)
        {
            smem[tid] = (tid + 1) * 10;
        }

        Gpu.SyncThreads();

        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t < rows)
        {
            int idx = indices[t];
            dst[t] = smem[idx];
        }
    }

    /// <summary>
    /// <see cref="InlineArrayAttribute"/> element indexing over a canary-padded struct holder (#718).
    /// </summary>
    [GpuKernel]
    public static void InlineArrayBoundsKernel(int[] indices, int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        DiffCanaryInlineHolder holder = default;
        for (int i = 0; i < 4; i++)
        {
            holder.Pre[i] = -100 - i;
            holder.Buf[i] = (t * 10) + i + 1;
            holder.Post[i] = 100 + i;
        }

        int idx = indices[t];
        dst[t] = holder.Buf[idx];
    }

    /// <summary>
    /// Optional <c>int[]? opt = null</c> parameter with null or non-null argument (#838).
    /// </summary>
    [GpuKernel]
    public static void NullArrayOptionalValidKernel(
        int[] src,
        int[] dst,
        int rows,
        int[]? opt = null)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int optLen = opt == null ? 0 : opt.Length;
        int optSum = 0;
        if (opt != null)
        {
            foreach (int v in opt)
            {
                optSum += v;
            }
        }

        dst[t] = src[t] + optLen + optSum;
    }

    /// <summary>
    /// Null local array <c>.Length</c> dereference (<see cref="NullReferenceException"/>, #706).
    /// </summary>
    [GpuKernel]
    public static void NullArrayLengthTrapKernel(int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        DiffNullArrayHolder nullHolder = default;
        int[]? localNull = nullHolder.Data;
#pragma warning disable CS8602 // Deliberate null dereference to verify NullReferenceException trap (#706)
        dst[t] = localNull.Length;
#pragma warning restore CS8602
    }

    /// <summary>
    /// Null local array indexer dereference (<see cref="NullReferenceException"/>, #706).
    /// </summary>
    [GpuKernel]
    public static void NullArrayIndexerTrapKernel(int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        DiffNullArrayHolder nullHolder = default;
        int[]? localNull = nullHolder.Data;
#pragma warning disable CS8602 // Deliberate null element access to verify NullReferenceException trap (#706)
        dst[t] = localNull[0];
#pragma warning restore CS8602
    }

    /// <summary>
    /// Null local array foreach enumeration (<see cref="NullReferenceException"/>, #706).
    /// </summary>
    [GpuKernel]
    public static void NullArrayForeachTrapKernel(int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        DiffNullArrayHolder nullHolder = default;
        int[]? localNull = nullHolder.Data;
        int sum = 0;
#pragma warning disable CS8602 // Deliberate null foreach to verify NullReferenceException trap (#706)
        foreach (int x in localNull)
#pragma warning restore CS8602
        {
            sum += x;
        }

        dst[t] = sum;
    }

    /// <summary>
    /// Reassigned parameter null array indexer dereference (<see cref="NullReferenceException"/>, #706).
    /// </summary>
    [GpuKernel]
    public static void NullArrayReassignedParameterTrapKernel(int[] src, int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        DiffNullArrayHolder nullHolder = default;
        int[]? alias = src;
        if (t >= 0)
        {
            alias = nullHolder.Data;
        }

#pragma warning disable CS8602 // Deliberate reassigned-parameter null access (#706)
        dst[t] = alias[0];
#pragma warning restore CS8602
    }

    /// <summary>
    /// Charter §4 bounds-check elimination witness: valid unsigned comparison.
    /// </summary>
    [GpuKernel]
    public static void BoundsCheckEliminationValidKernel(
        int[] backing,
        int[] indices,
        int[] dst,
        int sliceStart,
        int sliceLen,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        Span<int> s = backing.AsSpan().Slice(sliceStart, sliceLen);
        int i = indices[t];
        if ((uint)i < (uint)s.Length)
        {
            dst[t] = s[i];
        }
    }

    /// <summary>
    /// Charter §4 bounds-check elimination witness: off-by-one guard (<c>i &lt;= s.Length</c>).
    /// </summary>
    [GpuKernel]
    public static void BoundsCheckEliminationOffByOneKernel(
        int[] backing,
        int[] indices,
        int[] dst,
        int sliceStart,
        int sliceLen,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        Span<int> s = backing.AsSpan().Slice(sliceStart, sliceLen);
        int i = indices[t];
        if (i >= 0 && i <= s.Length)
        {
            dst[t] = s[i];
        }
    }

    /// <summary>
    /// Charter §4 bounds-check elimination witness: wrong-span guard (<c>i &lt; other.Length</c>).
    /// </summary>
    [GpuKernel]
    public static void BoundsCheckEliminationWrongSpanKernel(
        int[] backing,
        int[] otherBacking,
        int[] indices,
        int[] dst,
        int sliceStart,
        int sliceLen,
        int otherLen,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        Span<int> s = backing.AsSpan().Slice(sliceStart, sliceLen);
        Span<int> other = otherBacking.AsSpan().Slice(sliceStart, otherLen);
        int i = indices[t];
        if ((uint)i < (uint)other.Length)
        {
            dst[t] = s[i];
        }
    }

    /// <summary>
    /// Charter §4 bounds-check elimination witness: post-guard index increment (<c>i++</c>).
    /// </summary>
    [GpuKernel]
    public static void BoundsCheckEliminationPostIncrementKernel(
        int[] backing,
        int[] indices,
        int[] dst,
        int sliceStart,
        int sliceLen,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        Span<int> s = backing.AsSpan().Slice(sliceStart, sliceLen);
        int i = indices[t];
        if ((uint)i < (uint)s.Length)
        {
            i++;
            dst[t] = s[i];
        }
    }

    /// <summary>
    /// Charter §4 bounds-check elimination witness: upper-bound-only signed guard (<c>i &lt; s.Length</c>).
    /// </summary>
    [GpuKernel]
    public static void BoundsCheckEliminationUpperOnlyKernel(
        int[] backing,
        int[] indices,
        int[] dst,
        int sliceStart,
        int sliceLen,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        Span<int> s = backing.AsSpan().Slice(sliceStart, sliceLen);
        int i = indices[t];
        if (i < s.Length)
        {
            dst[t] = s[i];
        }
    }

    /// <summary>
    /// Multi-dimensional 3D grid where all 256 threads simultaneously trap on a null array dereference
    /// (<see cref="NullReferenceException"/>), verifying lock-free first-fault CAS on <c>GSharpErrorSlot</c>.
    /// </summary>
    [GpuKernel]
    public static void AllThreadsNullDereferenceTrapKernel(int[] flags, int[] dst, int rows)
    {
        int bx = (int)(BlockIndex.X + GridDimension.X * (BlockIndex.Y + GridDimension.Y * BlockIndex.Z));
        int tx = (int)(ThreadIndex.X + BlockDimension.X * (ThreadIndex.Y + BlockDimension.Y * ThreadIndex.Z));
        int bsize = (int)(BlockDimension.X * BlockDimension.Y * BlockDimension.Z);
        int t = (bx * bsize) + tx;
        if (t >= rows)
        {
            return;
        }

        if (flags[t] == 0)
        {
            dst[t] = (t * 31) + bx + tx;
        }
        else
        {
            DiffNullArrayHolder nullHolder = default;
            int[]? nullLocal = nullHolder.Data;
#pragma warning disable CS8602 // Deliberate simultaneous null dereference across all 256 threads
            dst[t] = nullLocal[t & 3];
#pragma warning restore CS8602
        }
    }

    /// <summary>
    /// Multi-dimensional 3D grid where all 256 threads simultaneously trap on an empty <c>StackLinq.First()</c>
    /// (<see cref="InvalidOperationException"/>), verifying lock-free first-fault CAS on <c>GSharpErrorSlot</c>.
    /// </summary>
    [GpuKernel]
    public static void AllThreadsEmptyFirstTrapKernel(int[] emptyArr, int[] flags, int[] dst, int rows)
    {
        int bx = (int)(BlockIndex.X + GridDimension.X * (BlockIndex.Y + GridDimension.Y * BlockIndex.Z));
        int tx = (int)(ThreadIndex.X + BlockDimension.X * (ThreadIndex.Y + BlockDimension.Y * ThreadIndex.Z));
        int bsize = (int)(BlockDimension.X * BlockDimension.Y * BlockDimension.Z);
        int t = (bx * bsize) + tx;
        if (t >= rows)
        {
            return;
        }

        if (flags[t] == 0)
        {
            dst[t] = (t * 31) + bx + tx;
        }
        else
        {
            Span<int> emptySlice = emptyArr.AsSpan().Slice(0, 0);
            dst[t] = emptySlice.AsStackEnumerable().First();
        }
    }
}
