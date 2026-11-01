using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public static partial class KernelReservedLaunchParameter_SadPath
{
    [GpuKernel]
    public static void GridDimXKernel(int gridDimX, int[] output)
    {
        int idx = ThreadIndex.X;
        if (idx < output.Length)
        {
            output[idx] = gridDimX;
        }
    }

    [GpuKernel]
    public static void StreamAndSharedMemBytesKernel(int hStream, int sharedMemBytes, int[] output)
    {
        int idx = ThreadIndex.X;
        if (idx < output.Length)
        {
            output[idx] = hStream + sharedMemBytes;
        }
    }

    // Device functions have no host launch wrappers, so reserved launch parameter names are allowed there.
    [GpuDeviceFunction]
    public static int DeviceHelper(int gridDimX, int hStream) => gridDimX + hStream;
}
