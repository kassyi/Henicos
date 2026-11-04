using System.Threading;
using Kassyi.GSharp.Core;

public static partial class Atomic64AndSharedEmissionKernels
{
    [GpuKernel]
    public static void Atomic64AndSharedEmissionKernel(long[] slot, int[] outInt)
    {
        var sBuf = new SharedMemory<int>(8);
        int ltZero = Interlocked.Add(ref slot[0], -5L) < 0L ? 1 : 0;
        int shifted = (int)(Interlocked.Exchange(ref slot[1], 42L) >> 62);
        if (ThreadIndex.X == 0)
        {
            sBuf[0] = ltZero + shifted;
        }

        Gpu.SyncThreads();
        outInt[ThreadIndex.X] = sBuf[0];
    }
}
