using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// Kernels whose <c>Gpu.SyncGrid(body)</c> bodies must run on every dialect, with a grid sync between their top-level
/// statements (#626). Each statement after a gap reads what other blocks wrote before it.
/// </summary>
public static partial class SyncGridBodyKernels
{
    // K1: the body runs
    [GpuKernel(Cooperative = true)]
    public static void BodyRunsKernel(int[] a)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        Gpu.SyncGrid(() =>
        {
            a[tid] = tid;
            a[tid] += 1;
        });
    }

    // K2: the grid is synchronized between the statements (thread 31 reads what block 1 wrote)
    [GpuKernel(Cooperative = true)]
    public static void NeighborKernel(int[] a, int[] b)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int n = GridDimension.X * BlockDimension.X;
        Gpu.SyncGrid(() =>
        {
            a[tid] = tid + 1;
            b[tid] = a[(tid + 1) % n];
        });
    }

    // K3: a nested block (a loop macro body, an if) is one statement; nothing inside it is synchronized. A grid sync
    // there would wait for threads that never reach it
    [GpuKernel(Cooperative = true)]
    public static void NestedKernel(int[] a, int n)
    {
        Gpu.SyncGrid(() =>
        {
            Gpu.GridStrideLoop(n, i =>
            {
                a[i] += 1;
                a[i] *= 2;
            });
            if (ThreadIndex.X == 0)
            {
                a[BlockIndex.X * 2] += 1;
                a[BlockIndex.X * 2 + 1] += 1;
            }
        });
    }

    // K4: empty statements, an explicit sync, an expression body and an empty nested body add no extra sync
    [GpuKernel(Cooperative = true)]
    public static void MixedKernel(int[] a, int[] b)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int n = GridDimension.X * BlockDimension.X;
        Gpu.SyncGrid(() =>
        {
            a[tid] = tid + 1;
            ;
            Gpu.SyncGrid();
            b[tid] = a[(tid + 1) % n];
            Gpu.SyncGrid(() => b[tid] += 100);
            Gpu.SyncGrid(() =>
            {
            });
        });
    }

    // K5: a local of the body lives across the gaps
    [GpuKernel(Cooperative = true)]
    public static void HoistedLocalKernel(int[] a, int[] b)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int n = GridDimension.X * BlockDimension.X;
        Gpu.SyncGrid(() =>
        {
            int mine = tid + 1;
            a[tid] = mine;
            b[tid] = a[(tid + 1) % n] + mine;
        });
    }

    // K6: a body inside a uniform loop syncs between its own statements in every iteration; the explicit sync at the end
    // keeps the next iteration's write after this iteration's read
    [GpuKernel(Cooperative = true)]
    public static void LoopNestedKernel(int[] a, int[] b)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int n = GridDimension.X * BlockDimension.X;
        for (int k = 0; k < 2; k++)
        {
            Gpu.SyncGrid(() =>
            {
                a[tid] = tid + 1 + k;
                b[tid] += a[(tid + 1) % n];
                Gpu.SyncGrid();
            });
        }
    }

    // K7: sibling bodies declare a local of the same name
    [GpuKernel(Cooperative = true)]
    public static void SiblingLocalsKernel(int[] a, int[] b)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        Gpu.SyncGrid(() =>
        {
            int v = tid;
            a[tid] = v;
            a[tid] += v;
        });
        Gpu.SyncGrid(() =>
        {
            int v = tid * 2;
            b[tid] = v;
            b[tid] += v;
        });
    }
}
