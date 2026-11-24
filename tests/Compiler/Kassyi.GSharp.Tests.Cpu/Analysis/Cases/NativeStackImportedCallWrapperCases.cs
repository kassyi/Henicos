using Kassyi.GSharp.Core;

namespace ConsumerApp;

public static partial class Kernels
{
    [GpuDeviceFunction]
    public static void LocalWrapper(int[] data)
    {
        ImportedStackLib.Helpers.RemoteBarrier(data);
    }

    [GpuKernel]
    public static void CallLocalWrapperKernel(int[] data)
    {
        LocalWrapper(data);
    }
}
