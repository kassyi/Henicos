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
/// Differential kernels for Lane D5 Stage 1: array, <see cref="Span{T}"/>, <see cref="ReadOnlySpan{T}"/>,
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
    /// B-01: 1D array indexing bounds (<c>0</c>, <c>Length - 1</c>, <c>-1</c>, <c>int.MinValue</c>, <c>Length</c>,
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
    /// B-02: <see cref="Span{T}"/> and <see cref="ReadOnlySpan{T}"/> indexing over a canary-padded backing array so
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
    /// B-03: <c>Span.Slice(start)</c> and <c>Span.Slice(start, length)</c> boundary and 32-bit <c>start + length</c>
    /// wrap-around checks (#718).
    /// </summary>
    [GpuKernel]
    public static void SpanSliceBoundsAndOverflowKernel(
        int[] backing,
        int[] starts,
        int[] counts,
        int[] modes,
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
        int mode = modes[t];
        int start = starts[t];
        int count = counts[t];

        if (mode == 0)
        {
            Span<int> sub = span.Slice(start);
            dst[t] = sub.Length;
        }
        else if (mode == 1)
        {
            Span<int> sub = span.Slice(start, count);
            dst[t] = sub.Length;
        }
        else
        {
            ReadOnlySpan<int> sub = span.Slice(start, count);
            dst[t] = sub.Length > 0 ? sub[0] + sub[sub.Length - 1] : 777;
        }
    }

    /// <summary>
    /// B-04: <see cref="ArraySegment{T}"/> (throws <see cref="ArgumentOutOfRangeException"/> on OOB index, S33) and
    /// <see cref="Memory{T}"/> / <see cref="ReadOnlyMemory{T}"/> (throws <see cref="IndexOutOfRangeException"/> on OOB
    /// index) over a canary-padded backing array (#718).
    /// </summary>
    [GpuKernel]
    public static void ArraySegmentAndMemoryBoundsKernel(
        ArraySegment<int> seg,
        Memory<int> mem,
        ReadOnlyMemory<int> roMem,
        int[] indices,
        int[] modes,
        int[] dst,
        int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int idx = indices[t];
        int mode = modes[t];
        if (mode == 0)
        {
            dst[t] = seg[idx] + seg.Count;
        }
        else
        {
            dst[t] = mem.Span[idx] + roMem.Span[idx];
        }
    }

    /// <summary>
    /// B-05: Static <see cref="SharedMemory{T}"/> indexing at <c>0</c>, <c>Length - 1</c>, and out-of-bounds indices.
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
    /// B-06: <see cref="InlineArrayAttribute"/> element indexing over a canary-padded struct holder (#718).
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
    /// B-07: Null local/reassigned array dereference (<see cref="NullReferenceException"/> on both Managed and Native,
    /// #706) vs optional <c>int[]? opt = null</c> parameter (#838).
    /// </summary>
    [GpuKernel]
    public static void NullArrayVsOptionalNullArrayKernel(
        int[] src,
        int[] modes,
        int[] dst,
        int rows,
        int[]? opt = null)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int mode = modes[t];
        if (mode == 0)
        {
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
        else if (mode == 1)
        {
            DiffNullArrayHolder nullHolder = default;
            int[]? localNull = nullHolder.Data;
#pragma warning disable CS8602 // Deliberate null dereference to verify NullReferenceException trap (#706)
            dst[t] = localNull.Length;
#pragma warning restore CS8602
        }
        else if (mode == 2)
        {
            DiffNullArrayHolder nullHolder = default;
            int[]? localNull = nullHolder.Data;
#pragma warning disable CS8602 // Deliberate null element access to verify NullReferenceException trap (#706)
            dst[t] = localNull[0];
#pragma warning restore CS8602
        }
        else if (mode == 3)
        {
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
        else
        {
            DiffNullArrayHolder nullHolder = default;
            int[]? alias = src;
            if (mode == 4)
            {
                alias = nullHolder.Data;
            }

#pragma warning disable CS8602 // Deliberate reassigned-parameter null access (#706)
            dst[t] = alias[0];
#pragma warning restore CS8602
        }
    }

    /// <summary>
    /// B-08: Charter §4 bounds-check over-elimination witnesses (off-by-one <c>i &lt;= s.Length</c>, wrong-span guard
    /// <c>i &lt; other.Length</c>, post-guard mutation <c>i++</c>, and signed upper-only guard <c>i &lt; s.Length</c> with
    /// negative <c>i</c>).
    /// </summary>
    [GpuKernel]
    public static void BoundsCheckEliminationWitnessKernel(
        int[] backing,
        int[] otherBacking,
        int[] indices,
        int[] modes,
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
        int mode = modes[t];

        if (mode == 0)
        {
            if ((uint)i < (uint)s.Length)
            {
                dst[t] = s[i];
            }
        }
        else if (mode == 1)
        {
            // Off-by-one guard: permits i == s.Length.
            if (i >= 0 && i <= s.Length)
            {
                dst[t] = s[i];
            }
        }
        else if (mode == 2)
        {
            // Guard against a longer span (other.Length > s.Length).
            if ((uint)i < (uint)other.Length)
            {
                dst[t] = s[i];
            }
        }
        else if (mode == 3)
        {
            // Post-guard index increment: i == s.Length - 1 becomes s.Length.
            if ((uint)i < (uint)s.Length)
            {
                i++;
                dst[t] = s[i];
            }
        }
        else
        {
            // Upper-bound-only signed guard: permits negative i.
            if (i < s.Length)
            {
                dst[t] = s[i];
            }
        }
    }

    /// <summary>
    /// B-10: Multi-dimensional 3D grid where all 256 threads simultaneously trap on either a null array dereference
    /// (<see cref="NullReferenceException"/>) or an empty <c>StackLinq.First()</c> (<see cref="InvalidOperationException"/>),
    /// verifying lock-free first-fault CAS on <c>GSharpErrorSlot</c>.
    /// </summary>
    [GpuKernel]
    public static void AllThreadsSimultaneousTrapKernel(int[] emptyArr, int[] modes, int[] dst, int rows)
    {
        int bx = (int)(BlockIndex.X + GridDimension.X * (BlockIndex.Y + GridDimension.Y * BlockIndex.Z));
        int tx = (int)(ThreadIndex.X + BlockDimension.X * (ThreadIndex.Y + BlockDimension.Y * ThreadIndex.Z));
        int bsize = (int)(BlockDimension.X * BlockDimension.Y * BlockDimension.Z);
        int t = (bx * bsize) + tx;
        if (t >= rows)
        {
            return;
        }

        int mode = modes[t];
        if (mode == 0)
        {
            dst[t] = (t * 31) + bx + tx;
        }
        else if (mode == 1)
        {
            DiffNullArrayHolder nullHolder = default;
            int[]? nullLocal = nullHolder.Data;
#pragma warning disable CS8602 // Deliberate simultaneous null dereference across all 256 threads
            dst[t] = nullLocal[t & 3];
#pragma warning restore CS8602
        }
        else
        {
            Span<int> emptySlice = emptyArr.AsSpan().Slice(0, 0);
            dst[t] = emptySlice.AsStackEnumerable().First();
        }
    }
}
