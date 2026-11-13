using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

// Device functions that wait at a barrier and whose frames a kernel of another class cannot hold
public static partial class FrameOwner
{
    private struct Hidden
    {
        public int X;
    }

    // The frame holds Hidden, so it is private to FrameOwner
    [GpuDeviceFunction]
    internal static int HoldsPrivate(int[] data)
    {
        var hidden = new Hidden { X = data[0] };
        Gpu.SyncThreads();
        return hidden.X;
    }

    // Calls HoldsPrivate from FrameOwner itself, so a kernel of FrameOwner can call both
    [GpuDeviceFunction]
    internal static int CallsHoldsPrivate(int[] data) => HoldsPrivate(data) + 1;

    [GpuKernel]
    public static void OwnerKernel(int[] data) => data[ThreadIndex.X] = CallsHoldsPrivate(data);
}

public static partial class FrameRelay
{
    // Its frame would hold the private frame of HoldsPrivate, which FrameRelay cannot name
    [GpuDeviceFunction]
    internal static int Relays(int[] data) => FrameOwner.HoldsPrivate(data) * 2;
}

public static partial class FrameCaller
{
    [GpuKernel]
    public static void DirectKernel(int[] data) => data[ThreadIndex.X] = FrameOwner.HoldsPrivate(data);

    [GpuKernel]
    public static void RelayedKernel(int[] data) => data[ThreadIndex.X] = FrameRelay.Relays(data);

    [GpuKernel]
    public static void FileLocalKernel(int[] data) => data[ThreadIndex.X] = FileLocalHolder.HoldsFileLocal(data);
}

file struct FileLocalValue
{
    public int X;
}

public static partial class FileLocalHolder
{
    [GpuDeviceFunction]
    internal static int HoldsFileLocal(int[] data)
    {
        var value = new FileLocalValue { X = data[0] };
        Gpu.SyncThreads();
        return value.X;
    }
}

public static partial class FrameOwner
{
    // A kernel of a class nested in FrameOwner can hold the private frame
    public static partial class NestedCaller
    {
        [GpuKernel]
        public static void NestedKernel(int[] data) => data[ThreadIndex.X] = HoldsPrivate(data);
    }
}
