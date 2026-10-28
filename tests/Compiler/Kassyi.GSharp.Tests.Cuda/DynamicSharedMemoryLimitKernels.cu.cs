using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Cuda;

[SkipSnapshotConsistency]
public static partial class DynamicSharedMemoryLimitKernels
{
    // Fills n ints of dynamic shared memory and writes them back reversed. n * 4 must equal sharedMemBytes.
    [GpuKernel]
    public static void BigSmemKernel(int[] output, int n)
    {
        var s = new SharedMemory<int>(n);
        int tid = ThreadIndex.X;
        for (int i = tid; i < n; i += BlockDimension.X)
        {
            s[i] = i;
        }

        Gpu.SyncThreads();
        output[tid] = s[n - 1 - tid];
    }
}
