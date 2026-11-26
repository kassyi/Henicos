using System;
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

    [GpuKernel]
    public static void PassRemoteMethodGroupKernel(int[] data)
    {
        Gpu.SyncThreads();
        Action<int[]> step = ImportedStackLib.Helpers.RemoteBarrier;
        step(data);
    }
}
