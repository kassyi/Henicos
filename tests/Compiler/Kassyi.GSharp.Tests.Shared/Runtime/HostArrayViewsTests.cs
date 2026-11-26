#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Runtime.Interop;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// <see cref="HostArrayViews.Pin{T}"/>: the address the CPU launch hands to the native kernel for a view, and the
/// handles the launcher frees after the launch, one case per kind of memory behind the view.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Runtime)]
public class HostArrayViewsTests
{
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Pin_DefaultView_ReturnsZeroAndRecordsNoHandle()
    {
        var handles = new List<IntPtr>();

        var address = HostArrayViews.Pin(default(ReadOnlyMemory<int>), handles);

        Assert.Equal(IntPtr.Zero, address);
        Assert.Empty(handles);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Pin_SliceOfAnArray_ReturnsTheAddressOfTheFirstElementOfTheSlice()
    {
        int[] array = [10, 20, 30, 40, 50];
        var handles = new List<IntPtr>();
        try
        {
            var address = HostArrayViews.Pin<int>(array.AsMemory(2, 2), handles);

            Assert.Single(handles);
            Assert.Equal(30, Marshal.ReadInt32(address));
            Assert.Equal(40, Marshal.ReadInt32(address, sizeof(int)));
        }
        finally
        {
            Free(handles);
        }
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Pin_EmptySliceOfAnArray_ReturnsANonZeroAddress()
    {
        int[] array = [1, 2, 3];
        var handles = new List<IntPtr>();
        try
        {
            var address = HostArrayViews.Pin<int>(array.AsMemory(3, 0), handles);

            Assert.NotEqual(IntPtr.Zero, address);
            Assert.Single(handles);
        }
        finally
        {
            Free(handles);
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Pin_SliceOfAString_ReturnsTheAddressOfTheFirstCharacterOfTheSlice()
    {
        var handles = new List<IntPtr>();
        try
        {
            var address = HostArrayViews.Pin("hello world".AsMemory(2, 5), handles);

            Assert.Single(handles);
            Assert.Equal('l', (char)Marshal.ReadInt16(address));
            Assert.Equal('o', (char)Marshal.ReadInt16(address, 2 * sizeof(char)));
        }
        finally
        {
            Free(handles);
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Pin_MemoryOfAMemoryManager_ReturnsTheAddressItOwnsAndRecordsOneHandle()
    {
        int[] data = [7, 8, 9, 10];
        using var manager = new HostAliasMemoryManager<int>(data, sizeof(int), data.Length - 1);
        var handles = new List<IntPtr>();
        try
        {
            var address = HostArrayViews.Pin<int>(manager.Memory, handles);

            Assert.Single(handles);
            Assert.Equal(8, Marshal.ReadInt32(address));
        }
        finally
        {
            Free(handles);
        }
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Pin_NullHandleList_ThrowsArgumentNullException()
    {
        // Intentionally passing null to assert the ArgumentNullException guard
        Assert.Throws<ArgumentNullException>(() => HostArrayViews.Pin<int>(new int[1], null!));
    }

    private static void Free(List<IntPtr> handles)
    {
        foreach (var handle in handles)
        {
            GCHandle.FromIntPtr(handle).Free();
        }
    }
}
