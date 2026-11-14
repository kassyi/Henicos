using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

// Device functions that wait at a barrier: frames that hold a private type, which kernels of other classes reach
// through the generated entries, and frames that would hold a file-local type, which no generated file can name
public static partial class FrameOwner
{
    private struct Hidden
    {
        public int X;
    }

    // The frame holds Hidden, so it is private to FrameOwner; other classes call it through its entries
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
    // Its frame holds the private frame of HoldsPrivate as an object
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

    [GpuKernel]
    public static void RelayedFileLocalKernel(int[] data) => data[ThreadIndex.X] = FileLocalRelay.Relays(data);
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

public static partial class FileLocalRelay
{
    // Has no frame either: it would hold the frame of HoldsFileLocal, which is not emitted
    [GpuDeviceFunction]
    internal static int Relays(int[] data) => FileLocalHolder.HoldsFileLocal(data) + 1;
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

public static partial class FrameOwner
{
    // A private function: only FrameOwner calls it, so it needs no entries
    [GpuDeviceFunction]
    private static int PrivateHoldsPrivate(int[] data) => HoldsPrivate(data) - 1;
}

// A protected function of a base class that is not static, whose frame holds a private type of the base
public partial class FrameBase
{
    private struct Hidden
    {
        public int X;
    }

    [GpuDeviceFunction]
    protected static int HoldsProtected(int[] data)
    {
        var hidden = new Hidden { X = data[0] };
        Gpu.SyncThreads();
        return hidden.X;
    }
}

public partial class FrameDerived : FrameBase
{
    [GpuKernel]
    public static void DerivedKernel(int[] data) => data[ThreadIndex.X] = HoldsProtected(data);
}

// A frame whose only private value is a temporary that the lowering spills across a barrier: SpillMake(data) is held
// while SpillWait(data) waits, and no local, parameter or return value of SpillF has the type of the struct
public static partial class SpillOwner
{
    private struct Spilled
    {
        public int X;
    }

    [GpuDeviceFunction]
    private static Spilled SpillMake(int[] data)
    {
        Spilled spilled;
        spilled.X = data[ThreadIndex.X] * 2;
        return spilled;
    }

    [GpuDeviceFunction]
    private static int SpillUse(Spilled spilled, int waited) => spilled.X + waited;

    [GpuDeviceFunction]
    internal static int SpillWait(int[] data)
    {
        var value = data[ThreadIndex.X];
        Gpu.SyncThreads();
        return value;
    }

    [GpuDeviceFunction]
    internal static int SpillF(int[] data) => SpillUse(SpillMake(data), SpillWait(data));

    [GpuKernel]
    public static void SpillOwnerKernel(int[] data) => data[ThreadIndex.X] = SpillF(data);
}

public static partial class SpillCaller
{
    [GpuKernel]
    public static void SpillCallerKernel(int[] data) => data[ThreadIndex.X] = SpillOwner.SpillF(data);
}
