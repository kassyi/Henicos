using System;
using System.Collections.Immutable;
using Kassyi.GSharp.Core;

namespace ArrayViewMembersTest
{
    public static partial class ArrayViewMemberCases
    {
        // The members the array view defines with the meaning of C#: no GSH0109
        [GpuKernel]
        [SkipSnapshotConsistency("Diagnostics case")]
        public static void FaithfulKernel(Memory<int> mem, ReadOnlyMemory<int> roMem, ArraySegment<int> seg,
            ImmutableArray<int> imm, int[] arr, int[] r)
        {
            r[0] = mem.Slice(1).Span[0] + mem.Slice(1, 2).Span[0];
            r[3] = roMem.Slice(1).Span[0] + roMem.Slice(0, 1).Span[0];
            r[1] = seg.Slice(1)[0] + seg.Slice(0, 1)[0];
            r[4] = imm.Slice(0, 1)[0] + imm.AsSpan(0, 1)[0] + imm.AsSpan().Length;
            roMem.CopyTo(mem);
            mem.CopyTo(mem);
            r[2] = mem.TryCopyTo(mem) && roMem.TryCopyTo(mem) ? 1 : 0;
            seg.CopyTo(arr);
            seg.CopyTo(seg);
            imm.CopyTo(arr);
            imm.CopyTo(arr.AsSpan());
        }

        // Members whose C# meaning the array view does not have, or that it does not define: GSH0109 at each call
        [GpuKernel]
        [SkipSnapshotConsistency("Diagnostics case")]
        public static void UnfaithfulKernel(ImmutableArray<int> imm, ArraySegment<int> seg, Memory<int> mem, int[] arr)
        {
            imm.Clear();
            seg.CopyTo(arr, 1);
            imm.CopyTo(arr, 1);
            arr[0] = mem.ToArray().Length;
        }
    }
}
