using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers.HostIntrinsic;

// Host code that reads, calls or names a GPU-only member; on the host each one throws GpuOnlyException.
public static class GpuOnlyHostReferences
{
    private static readonly int s_initialized = ThreadIndex.X;

    public static int Coordinates() => ThreadIndex.X + BlockIndex.Y + BlockDimension.X + GridDimension.Z;

    public static void Calls()
    {
        Gpu.SyncThreads();
        int lane = Gpu.LaneId();
        int constant = Gpu.Constant("N");
        Gpu.ForUnroll(0, 2, static i => { });
    }

    public static unsafe void MethodGroups()
    {
        Action sync = Gpu.SyncThreads;
        Func<int> lane = Gpu.LaneId;
        delegate*<void> pointer = &Gpu.SyncThreads;
    }

    public static void SharedMemoryAndDim3(Dim3 size)
    {
        var shared = new SharedMemory<int>(4);
        shared[0] = size.X;
    }

    public static int NestedInHostCode()
    {
        Func<int> read = () => BlockIndex.X;
        int Local() => GridDimension.X;
        return read() + Local();
    }
}
