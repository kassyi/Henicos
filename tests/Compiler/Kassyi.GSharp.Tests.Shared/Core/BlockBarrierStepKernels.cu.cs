// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Block barriers on the CPU paths: the threads of a block can reach <c>Gpu.SyncThreads</c> after different numbers of
/// steps (a warp shuffle that only some warps run); a thread that arrives first waits there until every thread of its
/// block that has not finished has arrived.
/// </summary>
public static partial class BlockBarrierStepKernels
{
    // Warp 1 passes a shuffle before the barrier and warp 0 does not; every thread then reads what a thread of the other
    // warp wrote before the barrier
    [GpuKernel]
    public static void ShuffleInLaterWarpKernel(int[] output)
    {
        var s = new SharedMemory<int>(64);
        int tid = ThreadIndex.X;
        int v = tid;
        if (tid >= 32)
        {
            v = Warp.ShuffleDownSync(0xFFFFFFFFu, v, 0);
        }

        s[tid] = v + 1;
        Gpu.SyncThreads();
        output[BlockIndex.X * 64 + tid] = s[(tid + 32) % 64];
    }

    // Warp w runs w + 1 shuffles before the first barrier and 2 - w after it, so the warps reach both barriers after
    // different numbers of steps
    [GpuKernel]
    public static void ShuffleCountsAcrossBarriersKernel(int[] output)
    {
        var s = new SharedMemory<int>(64);
        int tid = ThreadIndex.X;
        int warp = tid / 32;
        int v = tid;
        for (int i = 0; i <= warp; i++)
        {
            v = Warp.ShuffleXorSync(0xFFFFFFFFu, v, 0) + 1;
        }

        s[tid] = v;
        Gpu.SyncThreads();
        int other = s[(tid + 32) % 64];
        Gpu.SyncThreads();
        for (int i = 0; i < 2 - warp; i++)
        {
            other = Warp.ShuffleXorSync(0xFFFFFFFFu, other, 0) + 10;
        }

        s[tid] = other;
        Gpu.SyncThreads();
        output[BlockIndex.X * 64 + tid] = s[(tid + 32) % 64];
    }

    // A cooperative kernel with both kinds of wait in one phase: warp 1 shuffles before the block barrier and warp 0
    // after it, then every thread reads what the next block wrote before the grid sync
    [GpuKernel(Cooperative = true)]
    public static void ShuffleBarrierAndGridSyncKernel(int[] data, int[] output)
    {
        var s = new SharedMemory<int>(64);
        int tid = ThreadIndex.X;
        int gid = BlockIndex.X * 64 + tid;
        int total = GridDimension.X * 64;
        int v = gid;
        if (tid >= 32)
        {
            v = Warp.ShuffleDownSync(0xFFFFFFFFu, v, 0);
        }

        s[tid] = v + 1;
        Gpu.SyncThreads();
        int w = s[(tid + 32) % 64];
        if (tid < 32)
        {
            w = Warp.ShuffleDownSync(0xFFFFFFFFu, w, 0);
        }

        data[gid] = w;
        Gpu.SyncGrid();
        output[gid] = data[(gid + 64) % total];
    }
}
