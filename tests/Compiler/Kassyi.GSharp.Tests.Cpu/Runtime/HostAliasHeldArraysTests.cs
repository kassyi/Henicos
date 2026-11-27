#nullable enable
using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// Unit tests for <see cref="HostAliasHeldArrays"/> covering <see cref="HostAliasHeldArrays.AddView{T}"/>,
/// deduplication, <see cref="HostAliasGroups.WholeArray"/> omission, comma-free <see cref="HostAliasHeldArrays.Combine(string)"/>,
/// pin lifecycle, <see cref="HostAliasHeldArrays.AddRegions"/>, and <see cref="HostAliasHeldArrays.ShareCopies"/>.
/// </summary>
public class HostAliasHeldArraysTests
{
    private sealed class TrackingMemoryManager<T>(T[] backing) : MemoryManager<T>
    {
        public int ActivePins { get; private set; }

        public int TotalPins { get; private set; }

        public override Span<T> GetSpan() => backing;

        public override unsafe MemoryHandle Pin(int elementIndex = 0)
        {
            ActivePins++;
            TotalPins++;
            var handle = GCHandle.Alloc(backing, GCHandleType.Pinned);
            var ptr = Unsafe.Add<T>((void*)handle.AddrOfPinnedObject(), elementIndex);
            return new MemoryHandle(ptr, handle, this);
        }

        public override void Unpin() => ActivePins--;

        protected override void Dispose(bool disposing)
        {
        }
    }

    private sealed class ReferenceHolder
    {
        public int Value;
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void AddView_EmptyDefaultAndReferenceElements_AreIgnored()
    {
        var held = new HostAliasHeldArrays();
        try
        {
            ReadOnlyMemory<int> emptyDefault = default;
            ReadOnlyMemory<int> emptyArray = Array.Empty<int>();
            ReadOnlyMemory<ReferenceHolder?> refs = new[] { new ReferenceHolder { Value = 1 } };

            held.AddView(emptyDefault, writable: true, deviceAlignment: 4);
            held.AddView(emptyArray, writable: true, deviceAlignment: 4);
            held.AddView(refs, writable: true, deviceAlignment: 0);

            Assert.Equal(0, held.Count);
        }
        finally
        {
            held.Free();
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void AddView_IdenticalViews_RetainsBothEntriesGroupsThemAndDisposesAllPins()
    {
        int[] backing = [1, 2, 3, 4];
        var manager = new TrackingMemoryManager<int>(backing);
        var held = new HostAliasHeldArrays();
        var regions = new DeviceArrayRegions();
        try
        {
            held.CopiesBack = false;
            held.AddView<int>(manager.Memory, writable: true, deviceAlignment: 4);
            Assert.Equal(1, manager.ActivePins);

            held.CopiesBack = true;
            held.AddView<int>(manager.Memory, writable: true, deviceAlignment: 4);

            Assert.Equal(2, held.Count);
            Assert.Equal(2, manager.TotalPins);
            Assert.Equal(2, manager.ActivePins);

            held.Pin();
            var ranges = held.Combine(ReadOnlySpan<nint>.Empty);
            var traits = held.Combine(Array.Empty<byte>());
            Assert.Equal(2, traits.Length);
            Assert.Equal(0, traits[0] & HostAliasGroups.CopiesBack);
            Assert.NotEqual(0, traits[1] & HostAliasGroups.CopiesBack);

            var groups = HostAliasGroups.Find(ranges, traits, held.Combine(""));
            Assert.NotNull(groups);
            Assert.Equal(1, groups.GroupCount);
            Assert.True(groups.IsMember(0));
            Assert.True(groups.IsMember(1));
            Assert.True(groups.GetGroupLayout(0).CopiesBack);

            ref var group = ref groups.GroupAt(0);
            group.Base = new IntPtr(0x10000);
            held.AddRegions(groups, 0, regions);

            Assert.True(regions.TryGetViewRegion<int>(backing.AsSpan(), out var region));
            Assert.Equal(new IntPtr(0x10000), region);
        }
        finally
        {
            held.Free();
        }

        Assert.Equal(0, manager.ActivePins);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void ShareCopies_IdenticalViewsInAllInGroup_SharesOneHostCopy()
    {
        int[] backing = [1, 2, 3, 4];
        var held = new HostAliasHeldArrays { CopiesBack = false };
        try
        {
            held.AddView<int>(backing.AsMemory(0, 3), writable: true, deviceAlignment: 4);
            held.AddView<int>(backing.AsMemory(0, 3), writable: true, deviceAlignment: 4);
            held.Pin();

            var ranges = held.Combine(ReadOnlySpan<nint>.Empty);
            var traits = held.Combine(Array.Empty<byte>());
            var groups = HostAliasGroups.Find(ranges, traits, held.Combine(""));
            Assert.NotNull(groups);
            Assert.Equal(1, groups.GroupCount);

            var copies = new HostArrayCopies();
            held.ShareCopies(groups, 0, ranges, copies);

            var first = backing.AsMemory(0, 3);
            var second = backing.AsMemory(0, 3);
            Assert.False(copies.CopyView(ref first));
            Assert.False(copies.CopyView(ref second));

            first.Span[1] = 77;
            Assert.Equal(77, second.Span[1]);
            Assert.Equal(2, backing[1]);
        }
        finally
        {
            held.Free();
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void ShareCopies_SameLayoutDistinctElementTypesInAllInGroup_SharesHostCopyForBothTypes()
    {
        int[] intBacking = [1, -2, 3, 4];
        var uintAlias = (uint[])(object)intBacking;
        var held = new HostAliasHeldArrays { CopiesBack = false };
        try
        {
            held.AddView<int>(intBacking.AsMemory(), writable: true, deviceAlignment: 4);
            held.AddView<uint>(uintAlias.AsMemory(), writable: true, deviceAlignment: 4);
            Assert.Equal(2, held.Count);

            held.Pin();
            var ranges = held.Combine(ReadOnlySpan<nint>.Empty);
            var traits = held.Combine(Array.Empty<byte>());
            var groups = HostAliasGroups.Find(ranges, traits, held.Combine(""));
            Assert.NotNull(groups);
            Assert.Equal(1, groups.GroupCount);

            var copies = new HostArrayCopies();
            held.ShareCopies(groups, 0, ranges, copies);

            var intView = intBacking.AsMemory();
            var uintView = uintAlias.AsMemory();
            Assert.False(copies.CopyView(ref intView));
            Assert.False(copies.CopyView(ref uintView));

            intView.Span[2] = -5;
            Assert.Equal(0xFFFFFFFBu, uintView.Span[2]);
            Assert.Equal(3, intBacking[2]);
        }
        finally
        {
            held.Free();
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void AddView_DifferentElementLayoutAtSameAddress_IsNotDeduplicated()
    {
        int[] backing = [1, 2, 3, 4];
        var manager = new HostAliasMemoryManager<long>(backing, 0, 2);
        var held = new HostAliasHeldArrays { CopiesBack = true };
        try
        {
            held.AddView<int>(backing.AsMemory(), writable: true, deviceAlignment: 4);
            held.AddView<long>(manager.Memory, writable: true, deviceAlignment: 8);

            Assert.Equal(2, held.Count);
        }
        finally
        {
            held.Free();
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void AddView_ReadOnlyView_OmitsCopiesBackEvenWhenHolderCopiesBack()
    {
        int[] backing = [1, 2, 3];
        var held = new HostAliasHeldArrays { CopiesBack = true };
        try
        {
            held.AddView<int>(backing.AsMemory(), writable: false, deviceAlignment: 4);
            held.Pin();
            var traits = held.Combine(Array.Empty<byte>());

            Assert.Single(traits);
            Assert.Equal(HostAliasGroups.CopiesIn, traits[0]);
        }
        finally
        {
            held.Free();
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void AddView_OmitsWholeArrayTraitAndGroupsWithSameRangeArray()
    {
        int[] backing = [1, 2, 3, 4];
        var held = new HostAliasHeldArrays { CopiesBack = true };
        try
        {
            held.Add(backing, deviceAlignment: 4);
            held.AddView<int>(backing.AsMemory(), writable: true, deviceAlignment: 4);
            held.Pin();

            var traits = held.Combine(Array.Empty<byte>());
            Assert.Equal(2, traits.Length);
            Assert.NotEqual(0, traits[0] & HostAliasGroups.WholeArray);
            Assert.Equal(0, traits[1] & HostAliasGroups.WholeArray);

            var ranges = held.Combine(ReadOnlySpan<nint>.Empty);
            var groups = HostAliasGroups.Find(ranges, traits, held.Combine(""));
            Assert.NotNull(groups);
            Assert.Equal(1, groups.GroupCount);
            Assert.True(groups.IsMember(0));
            Assert.True(groups.IsMember(1));
        }
        finally
        {
            held.Free();
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Combine_ZeroCandidatesWithMixedArrayAndView_StripsLeadingCommaInOrder()
    {
        int[] first = [1, 2];
        int[] second = [3, 4];
        var held = new HostAliasHeldArrays { CopiesBack = true };
        try
        {
            held.Add(first, deviceAlignment: 4);
            held.AddView<int>(second.AsMemory(), writable: true, deviceAlignment: 4);
            held.Pin();

            Assert.Equal(
                "an array held by a struct argument,a view held by a struct argument",
                held.Combine(""));
            Assert.Equal(
                "slice,an array held by a struct argument,a view held by a struct argument",
                held.Combine("slice"));
        }
        finally
        {
            held.Free();
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void Free_AfterAddView_DisposesPinsAndResetsCount()
    {
        int[] backing = [10, 20, 30];
        var manager = new TrackingMemoryManager<int>(backing);
        var held = new HostAliasHeldArrays { CopiesBack = true };
        held.AddView<int>(manager.Memory, writable: true, deviceAlignment: 4);
        held.Pin();

        Assert.Equal(1, manager.ActivePins);
        Assert.Equal(1, held.Count);

        held.Free();

        Assert.Equal(0, manager.ActivePins);
        Assert.Equal(0, held.Count);
        Assert.Equal("args", held.Combine("args"));
        Assert.Equal([HostAliasGroups.CopiesIn], held.Combine([HostAliasGroups.CopiesIn]));
        Assert.True(held.Combine(ReadOnlySpan<nint>.Empty).IsEmpty);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void ShareCopies_ViewInAllInAndWritableGroups_SharesOrKeepsInHostArrayCopies()
    {
        int[] inBacking = [1, 2, 3, 4];
        int[] rwBacking = [5, 6, 7, 8];
        var held = new HostAliasHeldArrays();
        try
        {
            held.CopiesBack = false;
            held.AddView<int>(inBacking.AsMemory(0, 3), writable: true, deviceAlignment: 4);
            held.AddView<int>(inBacking.AsMemory(1, 3), writable: true, deviceAlignment: 4);
            held.AddView<int>(rwBacking.AsMemory(0, 3), writable: true, deviceAlignment: 4);
            held.CopiesBack = true;
            held.AddView<int>(rwBacking.AsMemory(1, 3), writable: true, deviceAlignment: 4);
            held.Pin();

            var ranges = held.Combine(ReadOnlySpan<nint>.Empty);
            var traits = held.Combine(Array.Empty<byte>());
            var groups = HostAliasGroups.Find(ranges, traits, held.Combine(""));
            Assert.NotNull(groups);
            Assert.Equal(2, groups.GroupCount);

            var copies = new HostArrayCopies();
            held.ShareCopies(groups, 0, ranges, copies);

            var inFirst = inBacking.AsMemory(0, 3);
            var inSecond = inBacking.AsMemory(1, 3);
            Assert.False(copies.CopyView(ref inFirst));
            Assert.False(copies.CopyView(ref inSecond));
            inFirst.Span[2] = 99;
            Assert.Equal(99, inSecond.Span[1]);
            Assert.Equal(3, inBacking[2]);

            var rwFirst = rwBacking.AsMemory(0, 3);
            Assert.False(copies.CopyView(ref rwFirst));
            rwFirst.Span[0] = 55;
            Assert.Equal(55, rwBacking[0]);
        }
        finally
        {
            held.Free();
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void AddRegions_ViewMemberInGroup_RecordsSharedViewWithoutCudaCall()
    {
        int[] backing = [1, 2, 3, 4];
        var held = new HostAliasHeldArrays { CopiesBack = true };
        var regions = new DeviceArrayRegions();
        try
        {
            held.AddView<int>(backing.AsMemory(0, 3), writable: true, deviceAlignment: 4);
            held.AddView<int>(backing.AsMemory(1, 3), writable: true, deviceAlignment: 4);
            held.Pin();

            var ranges = held.Combine(ReadOnlySpan<nint>.Empty);
            var traits = held.Combine(Array.Empty<byte>());
            var groups = HostAliasGroups.Find(ranges, traits, held.Combine(""));
            Assert.NotNull(groups);

            // Simulate CopyToDevice setting Base/Offset to verify AddRegions records view offsets without CUDA calls.
            ref var group = ref groups.GroupAt(0);
            group.Base = new IntPtr(0x10000);
            held.AddRegions(groups, 0, regions);

            Assert.True(regions.TryGetViewRegion<int>(backing.AsSpan(0, 3), out var firstRegion));
            Assert.True(regions.TryGetViewRegion<int>(backing.AsSpan(1, 3), out var secondRegion));
            Assert.Equal(new IntPtr(0x10000), firstRegion);
            Assert.Equal(new IntPtr(0x10004), secondRegion);

            regions.Free(IntPtr.Zero);
            Assert.False(regions.TryGetViewRegion<int>(backing.AsSpan(0, 3), out _));
        }
        finally
        {
            held.Free();
        }
    }

    private struct ConvertedElement
    {
        public float X;
        public System.Numerics.Vector2 V;
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    [InlineData(1, 3)]
    [InlineData(2, 2)]
    public void Find_OverlappingConvertedHeldViews_ThrowsNotSupportedException(int secondStart, int secondLength)
    {
        ConvertedElement[] backing = new ConvertedElement[5];
        var held = new HostAliasHeldArrays { CopiesBack = true };
        try
        {
            held.AddView<ConvertedElement>(backing.AsMemory(0, 4), writable: true, deviceAlignment: 8, deviceSize: 0,
                converted: true);
            held.AddView<ConvertedElement>(backing.AsMemory(secondStart, secondLength), writable: true,
                deviceAlignment: 8, deviceSize: 0, converted: true);
            held.Pin();

            var ranges = held.Combine(ReadOnlySpan<nint>.Empty).ToArray();
            var traits = held.Combine(Array.Empty<byte>());
            var exception = Assert.Throws<NotSupportedException>(
                () => HostAliasGroups.Find(ranges, traits, held.Combine("")));

            Assert.Contains("converted between the host and device layouts", exception.Message,
                StringComparison.Ordinal);
            Assert.DoesNotContain("not aligned", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            held.Free();
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Find_OverlappingConvertedHeldArrayAndView_ThrowsNotSupportedException()
    {
        ConvertedElement[] backing = new ConvertedElement[4];
        var held = new HostAliasHeldArrays { CopiesBack = true };
        try
        {
            held.Add(backing, deviceAlignment: 8, deviceSize: 0, converted: true);
            held.AddView<ConvertedElement>(backing.AsMemory(1, 2), writable: true, deviceAlignment: 8, deviceSize: 0,
                converted: true);
            held.Pin();

            var ranges = held.Combine(ReadOnlySpan<nint>.Empty).ToArray();
            var traits = held.Combine(Array.Empty<byte>());
            var exception = Assert.Throws<NotSupportedException>(
                () => HostAliasGroups.Find(ranges, traits, held.Combine("")));

            Assert.Contains("converted between the host and device layouts", exception.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            held.Free();
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Find_DisjointConvertedHeldViews_ReturnsNull()
    {
        ConvertedElement[] backing = new ConvertedElement[6];
        var held = new HostAliasHeldArrays { CopiesBack = true };
        try
        {
            held.AddView<ConvertedElement>(backing.AsMemory(0, 3), writable: true, deviceAlignment: 8, deviceSize: 0,
                converted: true);
            held.AddView<ConvertedElement>(backing.AsMemory(3, 3), writable: true, deviceAlignment: 8, deviceSize: 0,
                converted: true);
            held.Pin();

            var ranges = held.Combine(ReadOnlySpan<nint>.Empty);
            var traits = held.Combine(Array.Empty<byte>());
            Assert.Null(HostAliasGroups.Find(ranges, traits, held.Combine("")));
        }
        finally
        {
            held.Free();
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void ShareCopies_MemoryViewBeforeArraySegmentInAllInGroup_SharesHostCopy()
    {
        int[] backing = [1, 2, 3, 4];
        var shortManager = new HostAliasMemoryManager<short>(backing, 0, backing.Length * 2);
        var intSeg = new ArraySegment<int>(backing, 0, backing.Length);
        var held = new HostAliasHeldArrays { CopiesBack = false };
        try
        {
            held.AddView<short>(shortManager.Memory, writable: true, deviceAlignment: 2, requiresTypedArray: false);
            held.AddView<int>(MemoryExtensions.AsMemory(intSeg), writable: true, deviceAlignment: 4,
                requiresTypedArray: true);
            held.Pin();

            var ranges = held.Combine(ReadOnlySpan<nint>.Empty);
            var traits = held.Combine(Array.Empty<byte>());
            var groups = HostAliasGroups.Find(ranges, traits, held.Combine(""));
            Assert.NotNull(groups);

            var copies = new HostArrayCopies();
            held.ShareCopies(groups, 0, ranges, copies);

            var shortMem = shortManager.Memory;
            Assert.False(copies.CopyView(ref shortMem));
            Assert.False(copies.CopyView(ref intSeg));

            shortMem.Span[0] = 0x1234;
            Assert.Equal(0x1234, intSeg[0]);
            Assert.Equal(1, backing[0]);
        }
        finally
        {
            held.Free();
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void ShareCopies_ArraySegmentTwoBytesAfterShortViewInAllInGroup_SharesAnIntCopyThatTheSegmentStartsIn()
    {
        int[] backing = [1, 2, 3, 4];
        var shortManager = new HostAliasMemoryManager<short>(backing, 2, 6);
        var intSeg = new ArraySegment<int>(backing, 1, 3);
        var held = new HostAliasHeldArrays { CopiesBack = false };
        try
        {
            held.AddView<short>(shortManager.Memory, writable: true, deviceAlignment: 2);
            held.AddView<int>(MemoryExtensions.AsMemory(intSeg), writable: true, deviceAlignment: 4,
                requiresTypedArray: true);
            held.Pin();

            var ranges = held.Combine(ReadOnlySpan<nint>.Empty);
            var groups = HostAliasGroups.Find(ranges, held.Combine(Array.Empty<byte>()), held.Combine(""));
            Assert.NotNull(groups);

            var copies = new HostArrayCopies();
            held.ShareCopies(groups, 0, ranges, copies);

            var shortMem = shortManager.Memory;
            Assert.False(copies.CopyView(ref shortMem));
            Assert.False(copies.CopyView(ref intSeg));

            Assert.Equal([2, 3, 4], intSeg.AsSpan().ToArray());
            shortMem.Span[2] = 10;
            Assert.Equal(2 + (10 << 16), intSeg[0]);
            Assert.Equal(2, backing[1]);
        }
        finally
        {
            held.Free();
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void CombineDeviceAlignments_ViewWithACudaAlignment_AppendsItAfterTheArguments()
    {
        float[] backing = [1f, 2f, 3f, 4f];
        var held = new HostAliasHeldArrays { CopiesBack = true };
        try
        {
            held.AddView<float>(backing.AsMemory(), writable: true, deviceAlignment: 4, cudaAlignment: 8);
            held.Add(new int[] { 1 }, deviceAlignment: 4);
            held.Pin();

            Assert.Equal([16, 8, 4], held.CombineDeviceAlignments([16]).ToArray());
        }
        finally
        {
            held.Free();
        }

        Assert.Equal([16], held.CombineDeviceAlignments([16]).ToArray());
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void ShareCopies_MemoryViewBeforeWholeArrayInAllInGroup_SharesHostCopy()
    {
        int[] backing = [1, 2, 3, 4];
        var shortManager = new HostAliasMemoryManager<short>(backing, 0, backing.Length * 2);
        var held = new HostAliasHeldArrays { CopiesBack = false };
        try
        {
            held.AddView<short>(shortManager.Memory, writable: true, deviceAlignment: 2, requiresTypedArray: false);
            held.Add(backing, deviceAlignment: 4);
            held.Pin();

            var ranges = held.Combine(ReadOnlySpan<nint>.Empty);
            var traits = held.Combine(Array.Empty<byte>());
            var groups = HostAliasGroups.Find(ranges, traits, held.Combine(""));
            Assert.NotNull(groups);

            var copies = new HostArrayCopies();
            held.ShareCopies(groups, 0, ranges, copies);

            int[]? arrCopy = backing;
            var shortMem = shortManager.Memory;
            Assert.False(copies.Copy(ref arrCopy));
            Assert.False(copies.CopyView(ref shortMem));

            shortMem.Span[0] = 0x5678;
            Assert.Equal(0x5678, arrCopy[0]);
            Assert.Equal(1, backing[0]);
        }
        finally
        {
            held.Free();
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void ShareCopies_MemoryViewBeforeImmutableArrayInAllInGroup_SharesHostCopy()
    {
        int[] backing = [1, 2, 3, 4];
        var imm = ImmutableCollectionsMarshal.AsImmutableArray(backing);
        var shortManager = new HostAliasMemoryManager<short>(backing, 0, backing.Length * 2);
        var held = new HostAliasHeldArrays { CopiesBack = false };
        try
        {
            held.AddView<short>(shortManager.Memory, writable: true, deviceAlignment: 2, requiresTypedArray: false);
            held.AddView<int>(imm.AsMemory(), writable: false, deviceAlignment: 4, requiresTypedArray: true);
            held.Pin();

            var ranges = held.Combine(ReadOnlySpan<nint>.Empty);
            var traits = held.Combine(Array.Empty<byte>());
            var groups = HostAliasGroups.Find(ranges, traits, held.Combine(""));
            Assert.NotNull(groups);

            var copies = new HostArrayCopies();
            held.ShareCopies(groups, 0, ranges, copies);

            var shortMem = shortManager.Memory;
            Assert.False(copies.CopyView(ref imm));
            Assert.False(copies.CopyView(ref shortMem));

            shortMem.Span[0] = 0x4321;
            Assert.Equal(0x4321, imm[0]);
            Assert.Equal(1, backing[0]);
        }
        finally
        {
            held.Free();
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void MapConversion_ConvertedArrayAndSliceRange_UsesMappedKeyAndDeviceSize()
    {
        ConvertedElement[] backing = new ConvertedElement[4];
        var held = new HostAliasHeldArrays { CopiesBack = true };
        try
        {
            held.MapConversion(typeof(ConvertedElement), key: 1, deviceSize: 16);
            held.Add(backing, deviceAlignment: 8, deviceSize: 0, converted: true);
            held.Pin();

            var backingHandle = GCHandle.Alloc(backing, GCHandleType.Pinned);
            try
            {
                var slicePtr = backingHandle.AddrOfPinnedObject() + Unsafe.SizeOf<ConvertedElement>();
                ReadOnlySpan<nint> candidateRanges =
                [
                    slicePtr,
                    2 * Unsafe.SizeOf<ConvertedElement>(),
                    Unsafe.SizeOf<ConvertedElement>(),
                    16,
                    8,
                    1,
                ];
                var combined = held.Combine(candidateRanges);
                Assert.Equal((nint)16, combined[HostAliasGroups.RangeFields + 3]);
                Assert.Equal((nint)1, combined[HostAliasGroups.RangeFields + 5]);

                var traits = held.Combine([HostAliasGroups.CopiesIn | HostAliasGroups.CopiesBack]);
                var groups = HostAliasGroups.Find(combined, traits, held.Combine("slice"));
                Assert.NotNull(groups);
                Assert.Equal(1, groups.GroupCount);
                Assert.Equal(1, groups.GetGroupLayout(0).ConversionKey);
            }
            finally
            {
                backingHandle.Free();
            }
        }
        finally
        {
            held.Free();
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void MapConversion_ConvertedViewAndSliceRange_UsesMappedKeyAndDeviceSize()
    {
        ConvertedElement[] backing = new ConvertedElement[4];
        var held = new HostAliasHeldArrays { CopiesBack = true };
        try
        {
            held.MapConversion(typeof(ConvertedElement), key: 1, deviceSize: 16);
            held.AddView<ConvertedElement>(backing.AsMemory(0, 3), writable: true, deviceAlignment: 8,
                deviceSize: 0, converted: true);
            held.Pin();

            var backingHandle = GCHandle.Alloc(backing, GCHandleType.Pinned);
            try
            {
                var slicePtr = backingHandle.AddrOfPinnedObject() + Unsafe.SizeOf<ConvertedElement>();
                ReadOnlySpan<nint> candidateRanges =
                [
                    slicePtr,
                    3 * Unsafe.SizeOf<ConvertedElement>(),
                    Unsafe.SizeOf<ConvertedElement>(),
                    16,
                    8,
                    1,
                ];
                var combined = held.Combine(candidateRanges);
                Assert.Equal((nint)16, combined[HostAliasGroups.RangeFields + 3]);
                Assert.Equal((nint)1, combined[HostAliasGroups.RangeFields + 5]);

                var traits = held.Combine([HostAliasGroups.CopiesIn | HostAliasGroups.CopiesBack]);
                var groups = HostAliasGroups.Find(combined, traits, held.Combine("slice"));
                Assert.NotNull(groups);
                Assert.Equal(1, groups.GroupCount);
                Assert.Equal(1, groups.GetGroupLayout(0).ConversionKey);
            }
            finally
            {
                backingHandle.Free();
            }
        }
        finally
        {
            held.Free();
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void MapConversion_UnmappedConvertedElement_FallsBackToHeldConversionKey()
    {
        ConvertedElement[] backing = new ConvertedElement[4];
        var held = new HostAliasHeldArrays { CopiesBack = true };
        try
        {
            held.MapConversion(typeof(int), key: 1, deviceSize: 4);
            held.Add(backing, deviceAlignment: 8, deviceSize: 0, converted: true);
            held.AddView<ConvertedElement>(backing.AsMemory(1, 2), writable: true, deviceAlignment: 8,
                deviceSize: 0, converted: true);
            held.Pin();

            var ranges = held.Combine(ReadOnlySpan<nint>.Empty).ToArray();
            Assert.Equal((nint)Unsafe.SizeOf<ConvertedElement>(), ranges[3]);
            Assert.Equal((nint)HostAliasHeldArrays.HeldConversionKey, ranges[5]);

            var traits = held.Combine(Array.Empty<byte>());
            var exception = Assert.Throws<NotSupportedException>(
                () => HostAliasGroups.Find(ranges, traits, held.Combine("")));
            Assert.Contains("converted between the host and device layouts", exception.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            held.Free();
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void MapConversion_ZeroDeviceSize_PropagatesZeroAndThrowsOnFind()
    {
        ConvertedElement[] backing = new ConvertedElement[4];
        var held = new HostAliasHeldArrays { CopiesBack = true };
        try
        {
            held.MapConversion(typeof(ConvertedElement), key: 1, deviceSize: 0);
            held.Add(backing, deviceAlignment: 8, deviceSize: 0, converted: true);
            held.AddView<ConvertedElement>(backing.AsMemory(1, 2), writable: true, deviceAlignment: 8,
                deviceSize: 0, converted: true);
            held.Pin();

            var ranges = held.Combine(ReadOnlySpan<nint>.Empty).ToArray();
            Assert.Equal((nint)0, ranges[3]);
            Assert.Equal((nint)1, ranges[5]);

            var traits = held.Combine(Array.Empty<byte>());
            var exception = Assert.Throws<NotSupportedException>(
                () => HostAliasGroups.Find(ranges, traits, held.Combine("")));
            Assert.Contains("converted between the host and device layouts", exception.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            held.Free();
        }
    }
}
