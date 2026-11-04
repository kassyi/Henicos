using System.Threading;
using Kassyi.GSharp.Core;

public static partial class AtomicBarrierUniformityAuditKernels
{
    [GpuKernel]
    public static void GpuAtomicBranchWithBarrierKernel(int[] outMask)
    {
        int localSlot = 0;
        if (Gpu.AtomicAdd(ref localSlot, 1) == 0)
        {
            outMask[0] = (int)Warp.ActiveMask();
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    // #1072: Interlocked.Add return value is not classified as thread-variant in SyncIntrinsicClassifier,
    // so neither GSH0083 (for Gpu.SyncThreads() on a uniform local ref) nor GSH0101 (for Warp.ActiveMask() on CPU)
    // is reported inside the Interlocked.Add branch.
    [GpuKernel]
    public static void InterlockedBranchWithBarrierKernel(int[] outMask)
    {
        int localSlot = 0;
        if (Interlocked.Add(ref localSlot, 1) == 1)
        {
            outMask[0] = (int)Warp.ActiveMask();
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }
}
