// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

[SkipSnapshotConsistency]
public static partial class RefParameterAliasKernels
{
    public const int N = 4;

    [GpuDeviceFunction]
    public static void ReadRefAcrossBarrierNeighborWrite(ref int v, int[] buf, int[] o, int tid)
    {
        int first = v;
        Gpu.SyncThreads();
        buf[(tid + 1) % N] = 100 + (tid + 1) % N;
        Gpu.SyncThreads();
        int second = v;
        o[2 * tid] = first;
        o[2 * tid + 1] = second;
    }

    [GpuDeviceFunction]
    public static void ReadRefAfterSelfArrayWrite(ref int v, int[] buf, int[] o, int tid)
    {
        int first = v;
        buf[tid] = 200 + tid;
        int second = v;
        o[2 * tid] = first;
        o[2 * tid + 1] = second;
    }

    [GpuDeviceFunction]
    public static void ReadSharedRefAcrossBarrierNeighborWrite(ref int v, SharedMemory<int> shared, int[] o, int tid)
    {
        int first = v;
        Gpu.SyncThreads();
        shared[(tid + 1) % N] = 100 + (tid + 1) % N;
        Gpu.SyncThreads();
        int second = v;
        o[2 * tid] = first;
        o[2 * tid + 1] = second;
    }

    [GpuDeviceFunction]
    public static void ReadSharedRefReadOnlyAcrossBarrierNeighborWrite(ref readonly int v, SharedMemory<int> shared, int[] o, int tid)
    {
        int first = v;
        Gpu.SyncThreads();
        shared[(tid + 1) % N] = 100 + (tid + 1) % N;
        Gpu.SyncThreads();
        int second = v;
        o[2 * tid] = first;
        o[2 * tid + 1] = second;
    }

    [GpuKernel]
    public static void RefSharedBarrierNeighborWriteKernel(int[] o)
    {
        int tid = ThreadIndex.X;
        var shared = new SharedMemory<int>(N);
        shared[tid] = 10 + tid;
        Gpu.SyncThreads();
        ReadSharedRefAcrossBarrierNeighborWrite(ref shared[tid], shared, o, tid);
    }

    [GpuKernel]
    public static void RefReadOnlySharedBarrierNeighborWriteKernel(int[] o)
    {
        int tid = ThreadIndex.X;
        var shared = new SharedMemory<int>(N);
        shared[tid] = 10 + tid;
        Gpu.SyncThreads();
        ReadSharedRefReadOnlyAcrossBarrierNeighborWrite(in shared[tid], shared, o, tid);
    }

    [GpuKernel]
    public static void RefGlobalBarrierNeighborWriteKernel(int[] buf, int[] o)
    {
        int tid = ThreadIndex.X;
        ReadRefAcrossBarrierNeighborWrite(ref buf[tid], buf, o, tid);
    }

    [GpuKernel]
    public static void RefGlobalNoBarrierSelfWriteKernel(int[] buf, int[] o)
    {
        int tid = ThreadIndex.X;
        ReadRefAfterSelfArrayWrite(ref buf[tid], buf, o, tid);
    }
}
