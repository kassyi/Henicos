using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

// OwnerDevice and ReadingOwner are lowered with code of Other and OtherWriter copied in, which stands for an expansion
// that copies another method's body into the lowered method (an inlined iterator body): its 'v' is another method's
// parameter.
public static class ForeignParameterDemo
{
    [GpuDeviceFunction]
    public static int OwnerDevice(int v)
    {
        v = v + 1;
        Gpu.SyncThreads();
        return v;
    }

    [GpuDeviceFunction]
    public static int Other(int v)
    {
        int doubled = v * 2;
        return doubled;
    }

    // ReadingOwner never writes its 'v'; only the copied-in body of OtherWriter writes its own 'v'
    [GpuDeviceFunction]
    public static int ReadingOwner(int v)
    {
        int read = v;
        Gpu.SyncThreads();
        return read + v;
    }

    [GpuDeviceFunction]
    public static int OtherWriter(int v)
    {
        v = v * 2;
        return v;
    }
}
