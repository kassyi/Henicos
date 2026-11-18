using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

// A device function that waits at a barrier and holds a protected type: its frame is protected, so a derived class
// names it and an unrelated class of the same assembly runs the function through the entries
public partial class ProtectedFrameBase
{
    protected struct Hidden
    {
        public int X;
    }

    [GpuDeviceFunction]
    internal static int HoldsProtectedType(int[] data)
    {
        var hidden = new Hidden { X = data[0] };
        Gpu.SyncThreads();
        return hidden.X;
    }

    // A protected function holds the same type: a frame with internal fields is still private protected
    [GpuDeviceFunction]
    protected static int HoldsProtectedTypeProtected(int[] data)
    {
        var hidden = new Hidden { X = data[0] };
        Gpu.SyncThreads();
        return hidden.X;
    }
}

public partial class ProtectedFrameDerived : ProtectedFrameBase
{
    [GpuKernel]
    public static void DerivedKernel(int[] data) => data[ThreadIndex.X] = HoldsProtectedType(data);
}

public static partial class ProtectedFrameUnrelated
{
    [GpuKernel]
    public static void UnrelatedKernel(int[] data) => data[ThreadIndex.X] = ProtectedFrameBase.HoldsProtectedType(data);
}
