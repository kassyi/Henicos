using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Emission;

[SkipSnapshotConsistency("Dynamic shared memory calls checked by EmissionShapeTests")]
public static partial class DynamicSharedMemoryCallKernels
{
    [GpuDeviceFunction]
    public static int PlainHelper(int n, int val)
    {
        var inner = new SharedMemory<int>(n);
        inner[ThreadIndex.X] = val * 2;
        return inner[ThreadIndex.X];
    }

    // A call qualified with the type name passes the table like an unqualified one
    [GpuKernel]
    public static void QualifiedCallKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        output[tid] = DynamicSharedMemoryCallKernels.PlainHelper(n, tid);
    }

    // An iterator expanded into the kernel calls a helper that takes the table
    [GpuDeviceFunction]
    public static IEnumerable<int> HelperValues(int n)
    {
        yield return PlainHelper(n, 1);
        yield return PlainHelper(n, 2);
    }

    [GpuKernel]
    public static void IteratorCallKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        int sum = 0;
        foreach (var v in HelperValues(n))
        {
            sum += v;
        }

        output[tid] = sum;
    }
}
