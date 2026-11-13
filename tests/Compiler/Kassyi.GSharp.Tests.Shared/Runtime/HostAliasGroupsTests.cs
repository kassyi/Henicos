#nullable enable
using System.Runtime.InteropServices;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// Checks how <see cref="HostAliasGroups"/> groups the ranges of a launch's arguments: which arguments overlap, where
/// each member starts in its group, which copy directions a group takes, and which groups cannot share one copy. The
/// addresses are plain numbers; only the CPU copy reads memory.
/// </summary>
public class HostAliasGroupsTests
{
    private const byte InOut = HostAliasGroups.CopiesIn | HostAliasGroups.CopiesBack;
    private const byte In = HostAliasGroups.CopiesIn;
    private const byte Whole = HostAliasGroups.WholeArray;
    private const byte InView = HostAliasGroups.CopiesIn | HostAliasGroups.SpanView;

    // An element that holds a reference, 16 bytes
    private struct ReferenceHolder
    {
        public object? Value;
        public long Tag;
    }

    // (address, byte length, element size) per argument, read by the device as it is, alignment from the size
    private static nint[] Ranges(params (nint Address, nint Length, nint Size)[] ranges)
    {
        var full = new (nint, nint, nint, nint, nint, nint)[ranges.Length];
        for (var i = 0; i < ranges.Length; i++)
        {
            full[i] = (ranges[i].Address, ranges[i].Length, ranges[i].Size, ranges[i].Size, 0, 0);
        }

        return FullRanges(full);
    }

    // (address, byte length, host size, device size, device alignment, conversion) per argument
    private static nint[] FullRanges(params (nint Address, nint Length, nint Host, nint Device, nint Alignment,
        nint Conversion)[] ranges)
    {
        const int F = HostAliasGroups.RangeFields;
        var values = new nint[ranges.Length * F];
        for (var i = 0; i < ranges.Length; i++)
        {
            values[i * F] = ranges[i].Address;
            values[(i * F) + 1] = ranges[i].Length;
            values[(i * F) + 2] = ranges[i].Host;
            values[(i * F) + 3] = ranges[i].Device;
            values[(i * F) + 4] = ranges[i].Alignment;
            values[(i * F) + 5] = ranges[i].Conversion;
        }

        return values;
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void Find_DisjointAndAdjacentRanges_FindsNoGroup()
    {
        var ranges = Ranges((1000, 8, 4), (1008, 8, 4), (2000, 16, 4));

        Assert.Null(HostAliasGroups.Find(ranges, [InOut, InOut, InOut], "a,b,c"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Find_EmptyRangeAtTheSameAddress_FindsNoGroup()
    {
        var ranges = Ranges((1000, 0, 4), (1000, 16, 4));

        Assert.Null(HostAliasGroups.Find(ranges, [InOut, InOut], "a,b"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Find_TwoWholeArraysOfOneArray_LeavesThemToTheIdentitySharing()
    {
        var ranges = Ranges((1000, 16, 4), (1000, 16, 4));

        Assert.Null(HostAliasGroups.Find(ranges, [InOut | Whole, In | Whole], "a,b"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Find_WholeArraysJoinedByASlice_GroupsAllThree()
    {
        var ranges = Ranges((1000, 16, 4), (1000, 16, 4), (1004, 4, 4));

        var groups = HostAliasGroups.Find(ranges, [InOut | Whole, In | Whole, InOut], "a,b,s");

        Assert.NotNull(groups);
        Assert.Equal(1, groups.GroupCount);
        Assert.Equal([0, 0, 0], [groups.GetGroup(0), groups.GetGroup(1), groups.GetGroup(2)]);
        Assert.Equal((nint)4, groups.GetOffset(2));
        Assert.Equal((nint)16, groups.GetGroupLayout(0).Length);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void Find_ChainedSlices_GroupsThemFromTheLowestAddress()
    {
        // c overlaps b, b overlaps a, a and c do not overlap; d is apart
        var ranges = Ranges((1016, 12, 4), (1008, 12, 4), (1000, 12, 4), (3000, 4, 4));

        var groups = HostAliasGroups.Find(ranges, [InOut, In, In, InOut], "a,b,c,d");

        Assert.NotNull(groups);
        Assert.Equal(1, groups.GroupCount);
        Assert.False(groups.IsMember(3));
        Assert.Equal([(nint)16, 8, 0], [groups.GetOffset(0), groups.GetOffset(1), groups.GetOffset(2)]);
        Assert.Equal(new HostAliasGroupLayout(28, 0, 4, CopiesIn: true, CopiesBack: true, 0), groups.GetGroupLayout(0));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void Find_TwoSeparateOverlaps_MakesTwoGroups()
    {
        var ranges = Ranges((1000, 8, 4), (2000, 4, 4), (1004, 8, 4), (2000, 4, 4));

        var groups = HostAliasGroups.Find(ranges, [InOut, InOut, InOut, InOut], "a,b,c,d");

        Assert.NotNull(groups);
        Assert.Equal(2, groups.GroupCount);
        Assert.Equal(groups.GetGroup(0), groups.GetGroup(2));
        Assert.Equal(groups.GetGroup(1), groups.GetGroup(3));
        Assert.NotEqual(groups.GetGroup(0), groups.GetGroup(1));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Find_OnlyInAndOnlyOutMembers_TakeTheirOwnDirectionsOnly()
    {
        var inOnly = HostAliasGroups.Find(Ranges((1000, 8, 4), (1004, 8, 4)), [In, In], "a,b");
        var outOnly = HostAliasGroups.Find(
            Ranges((1000, 8, 4), (1004, 8, 4)), [HostAliasGroups.CopiesBack, HostAliasGroups.CopiesBack], "a,b");

        Assert.NotNull(inOnly);
        Assert.NotNull(outOnly);
        Assert.Equal((true, false), (inOnly.GetGroupLayout(0).CopiesIn, inOnly.GetGroupLayout(0).CopiesBack));
        Assert.Equal((false, true), (outOnly.GetGroupLayout(0).CopiesIn, outOnly.GetGroupLayout(0).CopiesBack));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Find_LongViewFourBytesIntoTheGroup_ShiftsTheRegionToAlignIt()
    {
        // ints at 1004, longs at 1008: the leader is the int view, the long view starts 4 bytes in
        var ranges = Ranges((1004, 16, 4), (1008, 16, 8));

        var groups = HostAliasGroups.Find(ranges, [InOut, InOut], "ints,longs");

        Assert.NotNull(groups);
        Assert.Equal(8, groups.GetGroupLayout(0).Alignment);
        Assert.Equal((nint)4, groups.GetGroupLayout(0).Shift);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Find_TwoLongViewsFourBytesApart_ThrowsNamingTheArguments()
    {
        var ranges = Ranges((1000, 16, 8), (1004, 16, 8));

        var exception = Assert.Throws<NotSupportedException>(
            () => HostAliasGroups.Find(ranges, [InOut, InOut], "first,second"));

        Assert.Contains("'first', 'second'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("not aligned", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Find_ConvertedAndRawMembersInAGroup_ThrowsNamingTheArguments()
    {
        var ranges = FullRanges((1000, 24, 12, 16, 8, 1), (3000, 4, 4, 4, 4, 0), (1012, 12, 4, 4, 4, 0));

        var exception = Assert.Throws<NotSupportedException>(
            () => HostAliasGroups.Find(ranges, [InOut, InOut, InOut], "bags,other,slice"));

        Assert.Contains("'bags', 'slice'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("other", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void Find_ConvertedSlices_ScaleTheirOffsetsToDeviceElements()
    {
        // 12-byte host elements, 16 on the device: elements 0 to 2 and 1 to 3
        var ranges = FullRanges((1000, 36, 12, 16, 8, 1), (1012, 36, 12, 16, 8, 1));

        var groups = HostAliasGroups.Find(ranges, [InOut, In], "a,b");

        Assert.NotNull(groups);
        Assert.Equal(new HostAliasGroupLayout(48, 0, 8, true, true, 1), groups.GetGroupLayout(0));
        Assert.Equal((nint)12, groups.GetOffset(1));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Find_ConvertedSlicesBetweenElementBoundaries_Throws()
    {
        var ranges = FullRanges((1000, 36, 12, 16, 8, 1), (1004, 36, 12, 16, 8, 1));

        var exception = Assert.Throws<NotSupportedException>(
            () => HostAliasGroups.Find(ranges, [InOut, InOut], "a,b"));

        Assert.Contains("element boundaries", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Find_EightByteStructsOfFourByteAlignment_FourBytesApart_FormAGroup()
    {
        // A struct of two ints is 8 bytes but aligned to 4 on the device, so 4 bytes apart is addressable
        var ranges = FullRanges((1000, 8, 8, 8, 4, 0), (1004, 8, 8, 8, 4, 0));

        var groups = HostAliasGroups.Find(ranges, [InOut, InOut], "first,second");

        Assert.NotNull(groups);
        Assert.Equal(4, groups.GetGroupLayout(0).Alignment);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public unsafe void TryGetHostCopy_AllInGroup_SharesOneCopyAtEachMembersOffset()
    {
        int[] backing = [1, 2, 3, 4];
        fixed (int* data = backing)
        {
            var ranges = Ranges(((nint)data, 12, 4), ((nint)(data + 1), 12, 4));
            var groups = HostAliasGroups.Find(ranges, [In, In], "a,b");
            Assert.NotNull(groups);

            Assert.True(HostAliasCopies.TryGetHostCopy<int>(groups, 0, ranges, out var first, out var firstStart));
            Assert.True(HostAliasCopies.TryGetHostCopy<int>(groups, 1, ranges, out var second, out var secondStart));

            Assert.Same(first, second);
            Assert.NotSame(backing, first);
            Assert.Equal([1, 2, 3, 4], first);
            Assert.Equal((0, 1), (firstStart, secondStart));
        }
    }

    // mutant: HostAliasCopies.TryGetHostCopy drops the arm that reinterprets a copy of the same size (throws instead)
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public unsafe void TryGetHostCopy_IntCopyAskedForAsUint_ReinterpretsTheSameCopy()
    {
        int[] backing = [1, -2, 3];
        fixed (int* data = backing)
        {
            var ranges = Ranges(((nint)data, 12, 4), ((nint)(data + 1), 8, 4));
            var groups = HostAliasGroups.Find(ranges, [In, In], "ints,uints");
            Assert.NotNull(groups);

            Assert.True(HostAliasCopies.TryGetHostCopy<int>(groups, 0, ranges, out var ints, out _));
            Assert.True(HostAliasCopies.TryGetHostCopy<uint>(groups, 1, ranges, out var uints, out var start));

            Assert.Same(ints, uints);
            Assert.Equal(1, start);
            Assert.Equal(0xFFFFFFFEu, uints[start]);
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public unsafe void TryGetHostCopy_LongCopyAskedForAsInt_Throws()
    {
        long[] storage = [1, 2];
        fixed (long* data = storage)
        {
            var ranges = Ranges(((nint)data, 16, 8), ((nint)data, 8, 4));
            var groups = HostAliasGroups.Find(ranges, [In, In], "longs,ints");
            Assert.NotNull(groups);

            Assert.True(HostAliasCopies.TryGetHostCopy<long>(groups, 0, ranges, out _, out _));
            var error = Assert.Throws<NotSupportedException>(
                () => HostAliasCopies.TryGetHostCopy<int>(groups, 1, ranges, out _, out _));
            Assert.Contains("'longs', 'ints'", error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public unsafe void TryGetHostView_IntAndLongViews_ShareOneCopyAtTheCallersAlignment()
    {
        long[] storage = [0x0000000200000001, 0x0000000400000003, 0x0000000600000005];
        fixed (long* data = storage)
        {
            // ints 1 to 5 (starting 4 bytes into the array) and longs 1 to 2
            var ints = (int*)data + 1;
            var ranges = Ranges(((nint)ints, 20, 4), ((nint)(data + 1), 16, 8));
            var groups = HostAliasGroups.Find(ranges, [InView, InView], "ints,longs");
            Assert.NotNull(groups);

            Assert.True(HostAliasCopies.TryGetHostView<int>(groups, 0, ranges, out var intView));
            Assert.True(HostAliasCopies.TryGetHostView<long>(groups, 1, ranges, out var longView));
            intView[1] = 30;

            Assert.Equal([2, 30, 4, 5, 6], intView.ToArray());
            Assert.Equal(0x000000040000001E, longView[0]);
            Assert.Equal(0x0000000400000003, storage[1]);
            fixed (long* first = longView)
            {
                Assert.Equal(0, (nint)first % 8);
            }
        }
    }

    // mutant: HostAliasCopies.TryGetHostView types a copy of its own instead of viewing the group's copy (the write
    // through the view does not reach the array)
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public unsafe void TryGetHostView_IntViewBesideALongArray_ViewsTheArraysCopy()
    {
        long[] storage = [0x0000000200000001, 3];
        fixed (long* data = storage)
        {
            var ranges = Ranges(((nint)data, 16, 8), ((nint)data, 8, 4));
            var groups = HostAliasGroups.Find(ranges, [In, InView], "array,ints");
            Assert.NotNull(groups);

            Assert.True(HostAliasCopies.TryGetHostCopy<long>(groups, 0, ranges, out var array, out _));
            Assert.True(HostAliasCopies.TryGetHostView<int>(groups, 1, ranges, out var ints));
            ints[1] = 7;

            Assert.Equal(0x0000000700000001, array[0]);
            Assert.Equal(0x0000000200000001, storage[0]);
        }
    }

    // mutant: HostAliasCopies.TryGetHostMemory skips the MemoryManager and asks for a typed copy (throws, the copy
    // holds ints)
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public unsafe void TryGetHostMemory_LongMemoryBesideAnIntArray_ViewsTheArraysCopy()
    {
        int[] backing = [1, 2, 3, 4];
        fixed (int* data = backing)
        {
            var ranges = Ranges(((nint)data, 16, 4), ((nint)data, 16, 8));
            var groups = HostAliasGroups.Find(ranges, [In, In], "ints,longs");
            Assert.NotNull(groups);

            Assert.True(HostAliasCopies.TryGetHostCopy<int>(groups, 0, ranges, out var ints, out _));
            Assert.True(HostAliasCopies.TryGetHostMemory<long>(groups, 1, ranges, out var longs));
            longs.Span[1] = 0x0000000900000008;

            Assert.Equal(2, longs.Length);
            Assert.Equal([1, 2, 8, 9], ints);
            Assert.Equal([1, 2, 3, 4], backing);
            using (var handle = longs.Pin())
            {
                Assert.Equal(0x0000000200000001, *(long*)handle.Pointer);
            }
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public unsafe void TryGetHostMemory_SameElementType_IsOverTheCopyItself()
    {
        int[] backing = [1, 2, 3];
        fixed (int* data = backing)
        {
            var ranges = Ranges(((nint)data, 12, 4), ((nint)(data + 1), 8, 4));
            var groups = HostAliasGroups.Find(ranges, [In, In], "a,b");
            Assert.NotNull(groups);

            Assert.True(HostAliasCopies.TryGetHostCopy<int>(groups, 0, ranges, out var array, out _));
            Assert.True(HostAliasCopies.TryGetHostMemory<int>(groups, 1, ranges, out var memory));

            Assert.True(System.Runtime.InteropServices.MemoryMarshal.TryGetArray<int>(memory, out var segment));
            Assert.Same(array, segment.Array);
            Assert.Equal((1, 2), (segment.Offset, segment.Count));
        }
    }

    // mutant: HostAliasCopies.TryGetHostMemory leaves the IsAtBoundaries test out (a copy of longs cannot cover the
    // 12 bytes, and TryGetHostCopy throws)
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public unsafe void TryGetHostMemory_FirstMemberLongerThanTheGroupsElements_ViewsABufferOfBytes()
    {
        int[] backing = [1, 2, 3];
        fixed (int* data = backing)
        {
            var ranges = Ranges(((nint)data, 8, 8), ((nint)data, 12, 4));
            var groups = HostAliasGroups.Find(ranges, [In, InView], "longs,ints");
            Assert.NotNull(groups);

            Assert.True(HostAliasCopies.TryGetHostMemory<long>(groups, 0, ranges, out var longs));
            Assert.True(HostAliasCopies.TryGetHostView<int>(groups, 1, ranges, out var ints));
            ints[2] = 30;
            longs.Span[0] = 0x0000000500000004;

            Assert.Equal([4, 5, 30], ints.ToArray());
            Assert.Equal([1, 2, 3], backing);
        }
    }

    // The elements of a copy that holds references are never viewed as another element type
    // mutant: HostAliasCopies.TryGetHostView and TryGetHostMemory drop the HostCopyHasReferences check (the views over
    // the bits of the references are returned instead of the exception)
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public unsafe void TryGetHostView_CopyOfElementsThatHoldReferences_Throws()
    {
        // Zeroed memory is a valid array of structs that hold null references; a pinned array of them cannot exist
        var memory = (byte*)NativeMemory.AllocZeroed(32);
        try
        {
            var ranges = Ranges(((nint)memory, 32, 16), ((nint)memory, 16, 4));
            var groups = HostAliasGroups.Find(ranges, [In, InView], "holders,ints");
            Assert.NotNull(groups);

            Assert.True(HostAliasCopies.TryGetHostCopy<ReferenceHolder>(groups, 0, ranges, out _, out _));
            Assert.Throws<NotSupportedException>(() => HostAliasCopies.TryGetHostView<int>(groups, 1, ranges, out _));
            Assert.Throws<NotSupportedException>(() => HostAliasCopies.TryGetHostMemory<int>(groups, 1, ranges, out _));
        }
        finally
        {
            NativeMemory.Free(memory);
        }
    }

    // A buffer of bytes starts at the address that the caller's memory has modulo 16, so a typed request for the bytes
    // starts after that many bytes (CopyStart); the source starts at every offset of 16, so nearly all of them place it
    // away from the buffer's first byte
    // mutant: HostAliasCopies.TryGetHostCopy computes start as offset / size, without group.CopyStart (the elements
    // come from the wrong bytes for every source offset but one)
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public unsafe void TryGetHostCopy_BufferOfBytesPlacedAtTheCallersAlignment_StartsAfterTheCopyStart()
    {
        var raw = new byte[64];
        for (var i = 0; i < raw.Length; i++)
        {
            raw[i] = (byte)i;
        }

        fixed (byte* data = raw)
        {
            for (var skew = 0; skew < 16; skew++)
            {
                // A long at the first byte and ints 4 bytes into the group: 12 bytes, which no long copy can cover
                var source = data + skew;
                var ranges = Ranges(((nint)source, 8, 8), ((nint)(source + 4), 8, 4));
                var groups = HostAliasGroups.Find(ranges, [In, InView], "longs,ints");
                Assert.NotNull(groups);

                Assert.True(HostAliasCopies.TryGetHostMemory<long>(groups, 0, ranges, out _));
                Assert.True(HostAliasCopies.TryGetHostCopy<byte>(groups, 1, ranges, out var bytes, out var start));

                Assert.Equal(raw.AsSpan(skew + 4, 8).ToArray(), bytes.AsSpan(start, 8).ToArray());
            }
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void TryGetHostCopy_GroupWithAWritableMember_PassesTheCallersMemory()
    {
        var ranges = Ranges((1000, 8, 4), (1004, 8, 4));
        var groups = HostAliasGroups.Find(ranges, [In, InOut], "a,b");
        Assert.NotNull(groups);

        Assert.False(HostAliasCopies.TryGetHostCopy<int>(groups, 0, ranges, out var copy, out _));
        Assert.Null(copy);
    }
}
