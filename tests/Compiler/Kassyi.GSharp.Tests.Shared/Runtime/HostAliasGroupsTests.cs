#nullable enable
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

    // (address, byte length, element size) per argument
    private static nint[] Ranges(params (nint Address, nint Length, nint Size)[] ranges)
    {
        var values = new nint[ranges.Length * HostAliasGroups.RangeFields];
        for (var i = 0; i < ranges.Length; i++)
        {
            values[i * 3] = ranges[i].Address;
            values[(i * 3) + 1] = ranges[i].Length;
            values[(i * 3) + 2] = ranges[i].Size;
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
        Assert.Equal(new HostAliasGroupLayout(28, 0, 4, CopiesIn: true, CopiesBack: true), groups.GetGroupLayout(0));
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
    public void Find_ConvertedMemberInAGroup_ThrowsNamingTheArguments()
    {
        var ranges = Ranges((1000, 16, 8), (3000, 4, 4), (1008, 8, 8));

        var exception = Assert.Throws<NotSupportedException>(() => HostAliasGroups.Find(
            ranges, [InOut, InOut, InOut | HostAliasGroups.Unsupported], "bags,other,slice"));

        Assert.Contains("'bags', 'slice'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("other", exception.Message, StringComparison.Ordinal);
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

            Assert.True(groups.TryGetHostCopy<int>(0, ranges, out var first, out var firstStart));
            Assert.True(groups.TryGetHostCopy<int>(1, ranges, out var second, out var secondStart));

            Assert.Same(first, second);
            Assert.NotSame(backing, first);
            Assert.Equal([1, 2, 3, 4], first);
            Assert.Equal((0, 1), (firstStart, secondStart));
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

            Assert.True(groups.TryGetHostView<int>(0, ranges, out var intView));
            Assert.True(groups.TryGetHostView<long>(1, ranges, out var longView));
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

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public unsafe void TryGetHostView_ViewBesideAnArrayOfAnotherType_Throws()
    {
        long[] storage = [1, 2];
        fixed (long* data = storage)
        {
            var ranges = Ranges(((nint)data, 16, 8), ((nint)data, 8, 4));
            var groups = HostAliasGroups.Find(ranges, [In, InView], "array,ints");
            Assert.NotNull(groups);

            Assert.True(groups.TryGetHostCopy<long>(0, ranges, out _, out _));
            Assert.Throws<NotSupportedException>(() => groups.TryGetHostView<int>(1, ranges, out _));
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

        Assert.False(groups.TryGetHostCopy<int>(0, ranges, out var copy, out _));
        Assert.Null(copy);
    }
}
