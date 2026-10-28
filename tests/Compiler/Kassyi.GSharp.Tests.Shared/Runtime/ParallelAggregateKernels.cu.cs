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

    // K2: a three-dimensional block; the threads share the elements by their linear rank
    [GpuKernel]
    public static void EveryThreadSum3DKernel(int[] data, int[] result)
    {
        int rank = ThreadIndex.X + BlockDimension.X * (ThreadIndex.Y + BlockDimension.Y * ThreadIndex.Z);
        int size = BlockDimension.X * BlockDimension.Y * BlockDimension.Z;
        int block = BlockIndex.X + GridDimension.X * (BlockIndex.Y + GridDimension.Y * BlockIndex.Z);
        result[block * size + rank] = data.AsStackEnumerable().AsParallel().Sum();
    }

    // K5: each block sums its own segment (uniform in the block, different between blocks)
    [GpuKernel]
    public static void SegmentSumKernel(int[] data, int m, int[] result)
    {
        result[BlockIndex.X * BlockDimension.X + ThreadIndex.X] =
            data.AsStackEnumerable().Skip(BlockIndex.X * m).Take(m).AsParallel().Sum();
    }

    // K6: Where and Select inside the shared loop
    [GpuKernel]
    public static void FilteredProjectedKernel(int[] data, int[] result)
    {
        result[BlockIndex.X * BlockDimension.X + ThreadIndex.X] =
            data.AsStackEnumerable().Where(x => x % 3 != 0).Select(x => x * 2).AsParallel().Sum();
    }

    // K7: the other element types the block strategy shares
    [GpuKernel]
    public static void LongSumKernel(long[] data, long[] result)
    {
        result[BlockIndex.X * BlockDimension.X + ThreadIndex.X] = data.AsStackEnumerable().AsParallel().Sum();
    }

    [GpuKernel]
    public static void UIntSumKernel(uint[] data, uint[] result)
    {
        result[BlockIndex.X * BlockDimension.X + ThreadIndex.X] = data.AsStackEnumerable().AsParallel().Sum();
    }

    [GpuKernel]
    public static void ULongSumKernel(ulong[] data, ulong[] result)
    {
        result[BlockIndex.X * BlockDimension.X + ThreadIndex.X] = data.AsStackEnumerable().AsParallel().Sum();
    }

    // K7 (float): summed in each thread (Warning A)
    [GpuKernel]
    public static void FloatSumKernel(float[] data, float[] result)
    {
        result[BlockIndex.X * BlockDimension.X + ThreadIndex.X] = data.AsStackEnumerable().AsParallel().Sum();
    }

    // K8: stages that depend on the elements before them run in each thread (Warning A)
    [GpuKernel]
    public static void OrderDependentSumKernel(int[] data, int[] result)
    {
        int gtid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        result[gtid * 2] = data.AsStackEnumerable().TakeWhile(x => x < 500).AsParallel().Sum();
        result[gtid * 2 + 1] = data.AsStackEnumerable().Where(x => x % 2 == 0).Take(5).AsParallel().Sum();
    }

    // K9: repeated calls reuse the shared slots of the reduction
    [GpuKernel]
    public static void RepeatedSumKernel(int[] a, int[] b, int[] result)
    {
        int total = 0;
        for (int k = 0; k < 3; k++)
        {
            total = total * 7 + a.AsStackEnumerable().AsParallel().Sum();
            total = total * 7 + b.AsStackEnumerable().Select(x => x + k).AsParallel().Sum();
        }

        result[BlockIndex.X * BlockDimension.X + ThreadIndex.X] = total;
    }

    // K10: a source that differs between threads runs in each thread (Warning A)
    [GpuKernel]
    public static void PrivateSourceKernel(int[] a, int[] b, int[] result)
    {
        int[] source = a;
        if (ThreadIndex.X % 2 == 1)
        {
            source = b;
        }

        result[BlockIndex.X * BlockDimension.X + ThreadIndex.X] = source.AsStackEnumerable().AsParallel().Sum();
    }

    // K11: a lambda with a side effect, and a lambda that reads a thread-variant value, run in each thread (Warning A)
    [GpuKernel]
    public static void ImpureLambdaKernel(int[] data, int[] counter, int[] result)
    {
        int gtid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        result[gtid * 2] = data.AsStackEnumerable().Select(x => System.Threading.Interlocked.Add(ref counter[0], 1) * 0 + x).AsParallel().Sum();
        int tid = ThreadIndex.X;
        result[gtid * 2 + 1] = data.AsStackEnumerable().Where(x => x > tid).AsParallel().Sum();
    }

    // Second review (B1): lambdas that read thread-dependent values directly run in each thread
    [GpuKernel]
    public static void LambdaThreadIndexKernel(int[] data, int[] result)
    {
        int gtid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        result[gtid * 3] = data.AsStackEnumerable().Select(x => x * (ThreadIndex.X + 1)).AsParallel().Sum();
        result[gtid * 3 + 1] = data.AsStackEnumerable().Where(x => x > ThreadIndex.X).AsParallel().Sum();
        result[gtid * 3 + 2] = data.AsStackEnumerable().Select(x => x + Tid()).AsParallel().Sum();
    }

    [GpuDeviceFunction]
    public static int Tid() => ThreadIndex.X;

    // Re-review (R1): a selector that passes its element to a device function is still shared by the block
    [GpuDeviceFunction]
    public static int Square(int v) => v * v;

    [GpuKernel]
    public static void DeviceFunctionSelectorKernel(int[] data, int[] result)
    {
        result[BlockIndex.X * BlockDimension.X + ThreadIndex.X] = data.AsStackEnumerable().Select(x => Square(x)).AsParallel().Sum();
    }
}
