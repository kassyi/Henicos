using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// <c>AsParallel().Sum()</c> in kernels (#624): every thread that calls it receives the sum of the whole sequence, as in C#.
/// </summary>
public static partial class ParallelAggregateKernels
{
    // K1: every thread of every block writes the sum it received
    [GpuKernel]
    public static void EveryThreadSumKernel(int[] data, int[] result)
    {
        int sum = data.AsStackEnumerable().AsParallel().Sum();
        result[BlockIndex.X * BlockDimension.X + ThreadIndex.X] = sum;
    }

    // K3: called by one thread of each block (the form of #315)
    [GpuKernel]
    public static void GuardedSumKernel(int[] data, int[] result)
    {
        if (ThreadIndex.X == 0)
        {
            result[BlockIndex.X] = data.AsStackEnumerable().AsParallel().Sum();
        }
    }

    // K4: the threads past n leave before the call
    [GpuKernel]
    public static void EarlyExitSumKernel(int[] data, int[] result, int n)
    {
        int gtid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (gtid >= n)
        {
            return;
        }

        result[gtid] = data.AsStackEnumerable().AsParallel().Sum();
    }
}
