using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// GSH0003: a [GpuKernel] method called from a kernel, a device function or a lambda in device code
public static partial class KernelCallFromDeviceCodeKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void CalleeKernel(int[] data)
    {
        data[ThreadIndex.X] += 1;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void CallerKernel(int[] data)
    {
        CalleeKernel(data);
    }

    [GpuDeviceFunction]
    public static void CallerFunction(int[] data)
    {
        KernelCallFromDeviceCodeKernels.CalleeKernel(data);
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void LambdaCallerKernel(int[] data)
    {
        Gpu.ForUnroll(0, 2, i => CalleeKernel(data));
        CallerFunction(data);
    }
}
