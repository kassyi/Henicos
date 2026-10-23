using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// GSH0003 for a finalizer marked as GPU code, which the generator does not emit
public class GpuFinalizerResource
{
    private int _released;

    [GpuKernel]
    ~GpuFinalizerResource()
    {
        _released = 1;
    }
}

public class DeviceFinalizerResource
{
    [GpuDeviceFunction]
    ~DeviceFinalizerResource()
    {
    }
}

// Not reported: the finalizer of a class with kernels is host code
public partial class HostFinalizerKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void Run(int[] output)
    {
        output[0] = 1;
    }

    ~HostFinalizerKernels()
    {
    }
}
