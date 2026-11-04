using Kassyi.GSharp.Core;

public static partial class GenericDeviceFunctionCase
{
    [GpuDeviceFunction]
    public static T Pick<T>(T a, T b, bool takeFirst) where T : unmanaged =>
        takeFirst ? a : b;

    [GpuKernel]
    public static void Kernel(int[] xs, int[] r)
    {
        int tid = ThreadIndex.X;
        r[tid] = Pick<int>(xs[tid], 42, true);
    }
}
