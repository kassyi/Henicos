using Kassyi.GSharp.Core;

namespace ArrivalBarrierCases;

// The inputs of ArrivalBarrierAnalysisTests: kernels that wait at a barrier counting a fixed number of arrivals
// directly, through a device function, or not at all
public static partial class Kernels
{
    [GpuDeviceFunction]
    public static void WaitAtMBarrier(ref ulong barrier)
    {
        Gpu.MBarrierArrive(ref barrier);
        Gpu.MBarrierWait(ref barrier, 0);
    }

    [GpuDeviceFunction]
    public static void CallsWaitAtMBarrier(ref ulong barrier) => WaitAtMBarrier(ref barrier);

    [GpuDeviceFunction]
    public static void SyncsTheBlock() => Gpu.SyncThreads();

    [GpuKernel]
    public static void MBarrierKernel(int[] result)
    {
        var bar = new SharedMemory<ulong>(1);
        if (ThreadIndex.X == 0)
        {
            Gpu.MBarrierInit(ref bar[0], (uint)BlockDimension.X);
        }

        Gpu.SyncThreads();
        Gpu.MBarrierArrive(ref bar[0]);
        Gpu.MBarrierWait(ref bar[0], 0);
        result[ThreadIndex.X] = 1;
    }

    [GpuKernel]
    public static void MBarrierThroughFunctionsKernel(int[] result)
    {
        var bar = new SharedMemory<ulong>(1);
        CallsWaitAtMBarrier(ref bar[0]);
        result[ThreadIndex.X] = 1;
    }

    [GpuKernel(Cooperative = true)]
    public static void GridSyncKernel(int[] result)
    {
        result[ThreadIndex.X] = 1;
        Gpu.SyncGrid();
        result[ThreadIndex.X] = 2;
    }

    [GpuKernel(Cooperative = true)]
    public static void CooperativeWithoutBarrierKernel(int[] result) => result[ThreadIndex.X] = 1;

    [GpuKernel]
    public static void BlockBarrierKernel(int[] result)
    {
        SyncsTheBlock();
        result[ThreadIndex.X] = 1;
    }
}
