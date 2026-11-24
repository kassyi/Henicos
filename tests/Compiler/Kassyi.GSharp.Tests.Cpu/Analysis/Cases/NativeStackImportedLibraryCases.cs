using Kassyi.GSharp.Core;

namespace ImportedStackLib;

public static partial class Helpers
{
    [GpuDeviceFunction]
    public static void RemoteBarrier(int[] data)
    {
        Gpu.SyncThreads();
        data[0] = 1;
    }
}
