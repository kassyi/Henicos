using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Emission;

[SkipSnapshotConsistency("Dynamic shared memory method group checked by EmissionShapeTests")]
public static partial class DynamicSharedMemoryDelegateKernels
{
    [GpuDeviceFunction]
    public static int PlainHelper(int n, int val)
    {
        var inner = new SharedMemory<int>(n);
        inner[ThreadIndex.X] = val * 2;
        return inner[ThreadIndex.X];
    }

    // The hidden table argument cannot be passed through a delegate
    [GpuKernel]
    public static void MethodGroupKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        Func<int, int, int> helper = PlainHelper;
        output[tid] = helper(n, tid);
    }
}
