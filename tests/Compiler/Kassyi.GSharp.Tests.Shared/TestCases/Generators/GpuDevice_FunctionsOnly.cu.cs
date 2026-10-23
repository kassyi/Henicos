using Kassyi.GSharp.Core;

namespace TestNamespace.DeviceOnly;

// Device functions without a kernel, one of which suspends at a barrier: the generator emits nothing for them
public static partial class DeviceOnlyFunctions
{
    [GpuDeviceFunction]
    public static float Twice(float value) => value * 2f;

    [GpuDeviceFunction]
    public static void AddAcrossBarrier(float[] data, int index)
    {
        data[index] = Twice(data[index]);
        Gpu.SyncThreads();
        data[index] = data[index] + 1f;
    }
}
