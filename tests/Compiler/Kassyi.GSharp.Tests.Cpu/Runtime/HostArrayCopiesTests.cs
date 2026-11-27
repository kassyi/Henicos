#nullable enable
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// Checks that <see cref="HostArrayCopies"/> gives every holder of one array the same copy, gives a kept array no copy,
/// and reports a copy as new only once, so that the walk of an array its own elements hold ends.
/// </summary>
public class HostArrayCopiesTests
{
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void Copy_TwoHoldersOfOneArray_GetOneCopy()
    {
        var copies = new HostArrayCopies();
        int[] original = [1, 2];
        int[]? first = original;
        int[]? second = original;

        var firstIsNew = copies.Copy(ref first);
        var secondIsNew = copies.Copy(ref second);

        Assert.Equal((true, false), (firstIsNew, secondIsNew));
        Assert.Same(first, second);
        Assert.NotSame(original, first);
        Assert.Equal(original, first);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Copy_KeptArray_StaysTheCallersArray()
    {
        var copies = new HostArrayCopies();
        int[] kept = [1];
        int[]? holder = kept;

        copies.Keep(kept);
        var isNew = copies.Copy(ref holder);

        Assert.False(isNew);
        Assert.Same(kept, holder);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Copy_TheCopyItself_IsNotCopiedAgain()
    {
        var copies = new HostArrayCopies();
        int[]? holder = [3];
        copies.Copy(ref holder);
        var copy = holder;

        var isNew = copies.Copy(ref holder);

        Assert.False(isNew);
        Assert.Same(copy, holder);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Copy_NullArray_StaysNull()
    {
        var copies = new HostArrayCopies();
        int[]? holder = null;

        Assert.False(copies.Copy(ref holder));
        Assert.Null(holder);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void CopyView_UnregisteredMemoryAndSegment_CloneAndReturnTrue()
    {
        var copies = new HostArrayCopies();
        int[] original = [10, 20, 30];
        var memory = original.AsMemory(1, 2);
        var segment = new ArraySegment<int>(original, 1, 2);

        Assert.True(copies.CopyView(ref memory));
        Assert.True(copies.CopyView(ref segment));

        memory.Span[0] = 99;
        segment[1] = 88;
        Assert.Equal([10, 20, 30], original);
        Assert.Equal([99, 30], memory.ToArray());
        Assert.Equal([20, 88], segment.ToArray());
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void CopyView_DefaultMemoryAndNullSegment_StayEmptyAndReturnFalse()
    {
        var copies = new HostArrayCopies();
        Memory<int> memory = default;
        ArraySegment<int> segment = default;

        Assert.False(copies.CopyView(ref memory));
        Assert.False(copies.CopyView(ref segment));
        Assert.True(memory.IsEmpty);
        Assert.Null(segment.Array);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public unsafe void CopyView_SharedMemory_ReplacesWithSharedViewAndReturnsFalse()
    {
        var copies = new HostArrayCopies();
        int[] original = [1, 2, 3, 4];
        int[] sharedBacking = [10, 20, 30, 40];
        var view = original.AsMemory(1, 2);
        var sharedView = sharedBacking.AsMemory(1, 2);

        fixed (int* ptr = &original[1])
        {
            copies.ShareView((IntPtr)ptr, 2 * sizeof(int), sharedView);
            Assert.False(copies.CopyView(ref view));
        }

        Assert.Equal([20, 30], view.ToArray());
        view.Span[0] = 77;
        Assert.Equal(77, sharedBacking[1]);
        Assert.Equal(2, original[1]);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public unsafe void CopyView_KeptMemoryAndSegment_PreserveOriginalAndReturnFalse()
    {
        var copies = new HostArrayCopies();
        int[] original = [1, 2, 3, 4];
        var memory = original.AsMemory(1, 2);
        var segment = new ArraySegment<int>(original, 1, 2);

        fixed (int* ptr = &original[1])
        {
            copies.KeepView<int>((IntPtr)ptr, 2 * sizeof(int));
            Assert.False(copies.CopyView(ref memory));
            Assert.False(copies.CopyView(ref segment));
        }

        memory.Span[0] = 55;
        Assert.Equal(55, original[1]);
        Assert.Same(original, segment.Array);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public unsafe void CopyView_SharedArraySegment_ReplacesWithSharedSegmentAndReturnsFalse()
    {
        var copies = new HostArrayCopies();
        int[] original = [1, 2, 3, 4];
        int[] sharedBacking = [10, 20, 30, 40];
        var segment = new ArraySegment<int>(original, 1, 2);
        var sharedView = sharedBacking.AsMemory(1, 2);

        fixed (int* ptr = &original[1])
        {
            copies.ShareView((IntPtr)ptr, 2 * sizeof(int), sharedView);
            Assert.False(copies.CopyView(ref segment));
        }

        Assert.Same(sharedBacking, segment.Array);
        Assert.Equal(1, segment.Offset);
        Assert.Equal(2, segment.Count);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public unsafe void CopyView_SharedMemoryManagerForArraySegment_ThrowsNotSupportedException()
    {
        var copies = new HostArrayCopies();
        int[] original = [1, 2, 3, 4];
        byte[] sharedBytes = new byte[16];
        var manager = new HostAliasMemoryManager<int>(sharedBytes, 0, 2);
        var segment = new ArraySegment<int>(original, 0, 2);

        fixed (int* ptr = original)
        {
            copies.ShareView((IntPtr)ptr, 2 * sizeof(int), manager.Memory);
            Assert.Throws<NotSupportedException>(() => copies.CopyView(ref segment));
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public unsafe void CopyView_DifferentElementTypesAtSameAddressAndByteLength_DoNotCollide()
    {
        var copies = new HostArrayCopies();
        int[] backing = [1, 2, 3, 4];
        long[] sharedLongs = [100L, 200L];
        var intView = backing.AsMemory(0, 4);
        var longManager = new HostAliasMemoryManager<long>(backing, 0, 2);
        var longView = longManager.Memory;

        fixed (int* ptr = backing)
        {
            copies.ShareView((IntPtr)ptr, 4 * sizeof(int), sharedLongs.AsMemory());
            Assert.True(copies.CopyView(ref intView));
            Assert.False(copies.CopyView(ref longView));
        }

        Assert.Equal([100L, 200L], longView.ToArray());
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public unsafe void CopyView_SharedReadOnlyMemory_ReplacesWithSharedViewAndReturnsFalse()
    {
        var copies = new HostArrayCopies();
        int[] original = [1, 2, 3, 4];
        int[] sharedBacking = [10, 20, 30, 40];
        ReadOnlyMemory<int> view = original.AsMemory(1, 2);
        var sharedView = sharedBacking.AsMemory(1, 2);

        fixed (int* ptr = &original[1])
        {
            copies.ShareView((IntPtr)ptr, 2 * sizeof(int), sharedView);
            Assert.False(copies.CopyView(ref view));
        }

        Assert.Equal([20, 30], view.ToArray());
        sharedBacking[1] = 77;
        Assert.Equal(77, view.Span[0]);
        Assert.Equal(2, original[1]);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void CopyView_ReadOnlyMemoryWithCopyElements_ClonesAndReturnsTrue()
    {
        var copies = new HostArrayCopies();
        int[] original = [10, 20, 30];
        ReadOnlyMemory<int> view = original.AsMemory(1, 2);

        Assert.True(copies.CopyView(ref view, out var copy));
        Assert.NotNull(copy);
        copy[0] = 99;

        Assert.Equal([10, 20, 30], original);
        Assert.Equal([99, 30], view.ToArray());
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public unsafe void CopyView_SharedImmutableArray_ReplacesWithSharedArrayAndReturnsFalse()
    {
        var copies = new HostArrayCopies();
        int[] original = [1, 2, 3, 4];
        int[] sharedBacking = [10, 20, 30, 40];
        var imm = ImmutableCollectionsMarshal.AsImmutableArray(original);

        fixed (int* ptr = original)
        {
            copies.ShareView((IntPtr)ptr, original.Length * sizeof(int), sharedBacking.AsMemory());
            Assert.False(copies.CopyView(ref imm));
        }

        Assert.Same(sharedBacking, ImmutableCollectionsMarshal.AsArray(imm));
        Assert.Equal([10, 20, 30, 40], imm.ToArray());
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public unsafe void CopyView_SharedSubSliceForImmutableArray_ThrowsNotSupportedException()
    {
        var copies = new HostArrayCopies();
        int[] original = [1, 2, 3, 4];
        int[] largerShared = [10, 20, 30, 40, 50];
        var imm = ImmutableCollectionsMarshal.AsImmutableArray(original);

        fixed (int* ptr = original)
        {
            copies.ShareView((IntPtr)ptr, original.Length * sizeof(int), largerShared.AsMemory(1, 4));
            Assert.Throws<NotSupportedException>(() => copies.CopyView(ref imm));
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void CopyView_ImmutableArrayWithCopyElements_ClonesAndReturnsTrue()
    {
        var copies = new HostArrayCopies();
        int[] original = [10, 20, 30];
        var imm = ImmutableCollectionsMarshal.AsImmutableArray(original);

        Assert.True(copies.CopyView(ref imm, out var copy));
        Assert.NotNull(copy);
        copy[1] = 88;

        Assert.Equal([10, 20, 30], original);
        Assert.Equal([10, 88, 30], imm.ToArray());
    }
}
