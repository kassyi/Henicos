using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Emission;

[SkipSnapshotConsistency("Stated failures checked by EmissionShapeTests")]
public partial class StatedFailureKernels
{
    private static int s_hostCounter;

    // An instance device function (GSH: instance device functions are not supported)
    [GpuDeviceFunction]
    public int InstanceHelper(int x) => x + 1;

    // A suspending device function that takes a function pointer
    [GpuDeviceFunction]
    public static unsafe int SuspendingWithFunctionPointer(delegate*<int, int> f, int x)
    {
        Gpu.SyncThreads();
        return x;
    }

    // A device function whose return type is not supported
    [GpuDeviceFunction]
    public static string ManagedResult(int x) => "";

    [GpuKernel]
    public static unsafe void CallerKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = s_hostCounter + SuspendingWithFunctionPointer(null, tid);
        var unused = ManagedResult(tid);
    }

    // A ref local is not supported
    [GpuKernel]
    public static void RefLocalKernel(int[] output)
    {
        ref int first = ref output[0];
        first = 1;
    }
}
