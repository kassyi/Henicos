using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// <c>AsParallel().Sum()</c> in cooperative kernels (#624 F1): the threads of the whole grid share the elements and every
/// thread receives the sum of the whole sequence.
/// </summary>
public static partial class ParallelAggregateGridKernels
{
    // G1 / G2: every thread of a one- or two-dimensional grid writes the sum it received
    [GpuKernel(Cooperative = true)]
    public static void GridSumKernel(int[] data, int[] result)
    {
        int rank = ThreadIndex.X + BlockDimension.X * (ThreadIndex.Y + BlockDimension.Y * ThreadIndex.Z);
        int size = BlockDimension.X * BlockDimension.Y * BlockDimension.Z;
        int block = BlockIndex.X + GridDimension.X * (BlockIndex.Y + GridDimension.Y * BlockIndex.Z);
        result[block * size + rank] = data.AsStackEnumerable().Where(x => x % 3 != 0).Select(x => x * 2).AsParallel().Sum();
    }

    // G3: a long partial takes 8 bytes of a 16-byte slot
    [GpuKernel(Cooperative = true)]
    public static void GridLongSumKernel(long[] data, long[] result)
    {
        result[BlockIndex.X * BlockDimension.X + ThreadIndex.X] = data.AsStackEnumerable().AsParallel().Sum();
    }

    // G4: repeated calls reuse one scratch
    [GpuKernel(Cooperative = true)]
    public static void GridRepeatedSumKernel(int[] a, int[] b, int[] result)
    {
        int total = 0;
        for (int k = 0; k < 3; k++)
        {
            total = total * 7 + a.AsStackEnumerable().AsParallel().Sum();
            total = total * 7 + b.AsStackEnumerable().Select(x => x + k).AsParallel().Sum();
        }

        result[BlockIndex.X * BlockDimension.X + ThreadIndex.X] = total;
    }

    // An input that differs between the blocks of a cooperative kernel keeps the Block strategy
    [GpuKernel(Cooperative = true)]
    public static void GridSegmentSumKernel(int[] data, int m, int[] result)
    {
        result[BlockIndex.X * BlockDimension.X + ThreadIndex.X] =
            data.AsStackEnumerable().Skip(BlockIndex.X * m).Take(m).AsParallel().Sum();
    }

    // Second review (B1): a lambda that reads BlockIndex keeps the Block strategy in a cooperative kernel
    [GpuKernel(Cooperative = true)]
    public static void GridLambdaBlockIndexKernel(int[] data, int[] result)
    {
        result[BlockIndex.X * BlockDimension.X + ThreadIndex.X] =
            data.AsStackEnumerable().Select(x => x + BlockIndex.X).AsParallel().Sum();
    }
}
