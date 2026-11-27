#nullable enable
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// Unit tests for <see cref="DeviceArrayRegions"/> covering shared view registration
/// (<see cref="DeviceArrayRegions.AddSharedView"/>), lookup (<see cref="DeviceArrayRegions.TryGetViewRegion{T}"/>),
/// and cleanup in <see cref="DeviceArrayRegions.Free"/>.
/// </summary>
public class DeviceArrayRegionsTests
{
    private sealed class ReferenceHolder
    {
        public int Value;
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public unsafe void TryGetViewRegion_MatchingAddressAndByteLength_ReturnsTrueWithRegion()
    {
        int[] backing = [1, 2, 3, 4];
        var regions = new DeviceArrayRegions();
        var expected = new IntPtr(0x20000);
        fixed (int* ptr = backing)
        {
            regions.AddSharedView((IntPtr)ptr, 4 * sizeof(int), expected);

            Assert.True(regions.TryGetViewRegion<int>(backing.AsSpan(), out var actual));
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public unsafe void TryGetViewRegion_SameAddressDifferentLength_ReturnsFalse()
    {
        int[] backing = [1, 2, 3, 4];
        var regions = new DeviceArrayRegions();
        fixed (int* ptr = backing)
        {
            regions.AddSharedView((IntPtr)ptr, 4 * sizeof(int), new IntPtr(0x20000));

            Assert.False(regions.TryGetViewRegion<int>(backing.AsSpan(0, 2), out var actual));
            Assert.Equal(IntPtr.Zero, actual);
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public unsafe void TryGetViewRegion_SameLengthDifferentAddress_ReturnsFalse()
    {
        int[] backing = [1, 2, 3, 4];
        var regions = new DeviceArrayRegions();
        fixed (int* ptr = backing)
        {
            regions.AddSharedView((IntPtr)ptr, 2 * sizeof(int), new IntPtr(0x20000));

            Assert.False(regions.TryGetViewRegion<int>(backing.AsSpan(1, 2), out var actual));
            Assert.Equal(IntPtr.Zero, actual);
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public unsafe void TryGetViewRegion_EmptyOrDefaultSpan_ReturnsFalse()
    {
        int[] backing = [1, 2];
        var regions = new DeviceArrayRegions();
        fixed (int* ptr = backing)
        {
            regions.AddSharedView((IntPtr)ptr, 0, new IntPtr(0x20000));
        }

        Assert.False(regions.TryGetViewRegion(ReadOnlySpan<int>.Empty, out var emptyRegion));
        Assert.Equal(IntPtr.Zero, emptyRegion);
        Assert.False(regions.TryGetViewRegion<int>(backing.AsSpan(0, 0), out var zeroSliceRegion));
        Assert.Equal(IntPtr.Zero, zeroSliceRegion);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void TryGetViewRegion_ReferenceContainingElement_ReturnsFalse()
    {
        var regions = new DeviceArrayRegions();
        regions.AddSharedView(new IntPtr(0x1234), 8, new IntPtr(0x20000));
        ReferenceHolder?[] items = [new ReferenceHolder { Value = 1 }];

        Assert.False(regions.TryGetViewRegion<ReferenceHolder?>(items, out var region));
        Assert.Equal(IntPtr.Zero, region);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public unsafe void Free_WhenOnlySharedEntriesExist_ClearsArraysAndViewsWithoutOwnedRegions()
    {
        int[] backing = [1, 2, 3, 4];
        var regions = new DeviceArrayRegions();
        fixed (int* ptr = backing)
        {
            regions.AddShared(backing, new IntPtr(0x10000));
            regions.AddSharedView((IntPtr)ptr, 4 * sizeof(int), new IntPtr(0x20000));

            Assert.True(regions.TryGetRegion(backing, out _));
            Assert.True(regions.TryGetViewRegion<int>(backing.AsSpan(), out _));

            regions.Free(IntPtr.Zero);

            Assert.False(regions.TryGetRegion(backing, out _));
            Assert.False(regions.TryGetViewRegion<int>(backing.AsSpan(), out _));
        }
    }
}
