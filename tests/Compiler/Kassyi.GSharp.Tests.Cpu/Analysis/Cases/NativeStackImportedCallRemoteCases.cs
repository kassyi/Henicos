using Kassyi.GSharp.Core;

namespace ConsumerApp;

public static partial class Kernels
{
    [GpuKernel]
    public static void CallRemoteKernel(int[] data)
    {
        ImportedStackLib.Helpers.RemoteBarrier(data);
    }
}
