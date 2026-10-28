// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Grid syncs on the CPU paths (#664): the launcher of a cooperative kernel runs the grid in phases, each ending when every
/// block has reached <c>Gpu.SyncGrid</c>; a block keeps its shared memory across the phases. A block or a representative
/// thread that finishes while the others wait, and a grid sync in a kernel that is not cooperative, are errors there,
/// because the GPU never completes such a grid sync.
/// </summary>
public static partial class GridSyncPhaseKernels
{
    // Every block reads what the next block wrote before the grid sync, through a static and a dynamic shared memory
    // declaration that must survive the grid syncs of its own block
    [GpuKernel(Cooperative = true)]
    public static void SharedMemoryAcrossGridSyncKernel(int[] buffer, int[] output, int n)
    {
        var staticCopy = new SharedMemory<int>(32);
        var dynamicCopy = new SharedMemory<int>(n);
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int local = ThreadIndex.X;
        staticCopy[local] = 1000 * BlockIndex.X + local;
        dynamicCopy[local] = 2000 * BlockIndex.X + local;
        buffer[tid] = tid + 1;
        Gpu.SyncGrid();
        int next = buffer[(tid + BlockDimension.X) % (GridDimension.X * BlockDimension.X)];
        Gpu.SyncGrid();
        output[tid] = next + staticCopy[(local + 1) % BlockDimension.X] + dynamicCopy[(local + 2) % BlockDimension.X];
    }

    // Warp 0 of each block passes a shuffle before the grid sync and the other warps do not, so the threads of a block reach
    // the grid sync after different numbers of steps; the threads that arrive first must wait there for the rest
    [GpuKernel(Cooperative = true)]
    public static void ShuffleBeforeGridSyncKernel(int[] data, int[] output)
    {
        int tid = ThreadIndex.X;
        int total = GridDimension.X * BlockDimension.X;
        int gid = BlockIndex.X * BlockDimension.X + tid;
        int v = gid;
        if (tid < 32)
        {
            v = Warp.ShuffleDownSync(0xFFFFFFFFu, v, 0);
        }

        data[gid] = v + 1;
        Gpu.SyncGrid();
        output[gid] = data[(gid + 32) % total];
    }

    // C2: block 0 finishes while the other blocks wait at the grid sync
    [GpuKernel(Cooperative = true)]
    public static void BlockFinishesBeforeGridSyncKernel(int[] a)
    {
        if (BlockIndex.X == 0)
        {
            return;
        }

        a[BlockIndex.X * BlockDimension.X + ThreadIndex.X] = 1;
        Gpu.SyncGrid();
        a[BlockIndex.X * BlockDimension.X + ThreadIndex.X] = 2;
    }

    // The representative thread (thread 0) of block 1 finishes while the other threads wait at the grid sync
    [GpuKernel(Cooperative = true)]
    public static void RepresentativeFinishesBeforeGridSyncKernel(int[] a)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (BlockIndex.X == 1 && ThreadIndex.X == 0)
        {
            return;
        }

        a[tid] = 1;
        Gpu.SyncGrid();
        a[tid] = 2;
    }

    // A grid sync reached through a device function from a kernel that is not cooperative: GSH0030 sees only the body of
    // the kernel
    [GpuKernel]
    public static void NotCooperativeKernel(int[] a)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        a[tid] = 1;
        WaitForGrid();
        a[tid] = 2;
    }

    [GpuDeviceFunction]
    public static void WaitForGrid() => Gpu.SyncGrid();
}
