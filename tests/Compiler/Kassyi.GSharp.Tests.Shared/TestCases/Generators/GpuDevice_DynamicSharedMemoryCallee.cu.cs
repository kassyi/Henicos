using Kassyi.GSharp.Core;

namespace TestNamespace.DynamicSharedMemoryCaching;

// The test turns the fixed count into the dynamic count n
public static class DynamicSharedMemoryHelpers
{
    [GpuDeviceFunction]
    public static int Scratch(int n, int value)
    {
        var scratch = new SharedMemory<int>(32);
        scratch[ThreadIndex.X] = value + n;
        return scratch[ThreadIndex.X];
    }
}
