using Kassyi.GSharp.Core;
using System.Collections.Immutable;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The instance methods of <c>Memory&lt;T&gt;</c>, <c>ReadOnlyMemory&lt;T&gt;</c>, <c>ArraySegment&lt;T&gt;</c> and
/// <c>ImmutableArray&lt;T&gt;</c> that device code calls as the members of the same name of its array view.
/// </summary>
public static partial class ArrayViewMemberKernels
{
    // r = [mem.Slice(1).Span[0], mem.Slice(1, 2).Span[1], roMem.Slice(2).Span[0], roMem.Slice(0, 1).Span[0]]
    [GpuKernel]
    public static void MemorySliceKernel(Memory<int> mem, ReadOnlyMemory<int> roMem, int[] r)
    {
        r[0] = mem.Slice(1).Span[0];
        r[1] = mem.Slice(1, 2).Span[1];
        r[2] = roMem.Slice(2).Span[0];
        r[3] = roMem.Slice(0, 1).Span[0];
    }

    // r = [seg.Slice(1)[0], seg.Slice(1, 2)[1], seg.Slice(2).Count]
    [GpuKernel]
    public static void SegmentSliceKernel(ArraySegment<int> seg, int[] r)
    {
        r[0] = seg.Slice(1)[0];
        r[1] = seg.Slice(1, 2)[1];
        r[2] = seg.Slice(2).Count;
    }

    // r = [imm.Slice(1, 2)[1], imm.AsSpan(1, 2)[0]]
    [GpuKernel]
    public static void ImmutableSliceKernel(ImmutableArray<int> imm, int[] r)
    {
        r[0] = imm.Slice(1, 2)[1];
        r[1] = imm.AsSpan(1, 2)[0];
    }

    [GpuKernel]
    public static void ReadOnlyMemoryCopyToKernel(ReadOnlyMemory<int> source, Memory<int> destination)
    {
        source.CopyTo(destination);
    }

    [GpuKernel]
    public static void ImmutableCopyToKernel(ImmutableArray<int> imm, int[] toSpan, int[] toArray)
    {
        imm.CopyTo(toSpan.AsSpan());
        imm.CopyTo(toArray);
    }

    [GpuKernel]
    public static void SegmentCopyToKernel(ArraySegment<int> seg, int[] toArray, ArraySegment<int> toSegment)
    {
        seg.CopyTo(toArray);
        seg.CopyTo(toSegment);
    }

    // r = [source.TryCopyTo(destination), source.Slice(1).TryCopyTo(destination)] as 1 or 0
    [GpuKernel]
    public static void MemoryTryCopyToKernel(Memory<int> source, Memory<int> destination, int[] r)
    {
        r[0] = source.TryCopyTo(destination) ? 1 : 0;
        r[1] = source.Slice(1).TryCopyTo(destination) ? 1 : 0;
    }
}
