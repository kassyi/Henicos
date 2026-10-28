using System;
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.ParallelAggregate;

[SkipSnapshotConsistency("Analyzer case")]
public static partial class ParallelAggregateWarnings
{
    [GpuKernel]
    public static void SharedKernel(int[] data, int k, int[] result)
    {
        result[ThreadIndex.X] = data.AsStackEnumerable().Skip(k).Where(x => x > k).Select(x => x * 2).AsParallel().Sum();
    }

    [GpuDeviceFunction]
    public static int InDeviceFunction(int[] data) => data.AsStackEnumerable().AsParallel().Sum();

    [GpuKernel]
    public static void InsideLocalFunctionKernel(int[] data, int[] result)
    {
        int Total() => data.AsStackEnumerable().AsParallel().Sum();
        result[ThreadIndex.X] = Total();
    }

    [GpuKernel]
    public static void DivergentKernel(int[] data, int[] result)
    {
        if (ThreadIndex.X == 0)
        {
            result[BlockIndex.X] = data.AsStackEnumerable().AsParallel().Sum();
        }
    }

    [GpuKernel]
    public static void EarlyExitKernel(int[] data, int n, int[] result)
    {
        if (ThreadIndex.X >= n)
        {
            return;
        }

        result[ThreadIndex.X] = data.AsStackEnumerable().AsParallel().Sum();
    }

    [GpuKernel]
    public static void PrivateSourceKernel(int[] a, int[] b, int[] result)
    {
        int[] source = a;
        source = b;
        result[ThreadIndex.X] = source.AsStackEnumerable().AsParallel().Sum();
    }

    [GpuKernel]
    public static void OrderDependentKernel(int[] data, int[] result)
    {
        result[ThreadIndex.X] = data.AsStackEnumerable().TakeWhile(x => x < 5).AsParallel().Sum();
    }

    [GpuKernel]
    public static void SideEffectKernel(int[] data, int[] counter, int[] result)
    {
        result[ThreadIndex.X] = data.AsStackEnumerable().Select(x => Gpu.AtomicAdd(ref counter[0], 1)).AsParallel().Sum();
    }

    [GpuKernel]
    public static void VariantInputKernel(int[] data, int[] result)
    {
        int tid = ThreadIndex.X;
        result[tid] = data.AsStackEnumerable().Where(x => x > tid).AsParallel().Sum();
    }

    [GpuKernel]
    public static void FloatKernel(float[] data, float[] result)
    {
        result[ThreadIndex.X] = data.AsStackEnumerable().AsParallel().Sum();
    }

    // No warning: the grid shares it (cooperative), or the input differs between blocks (each block has its own sum)
    [GpuKernel(Cooperative = true)]
    public static void CooperativeKernel(int[] data, int[] result)
    {
        result[ThreadIndex.X] = data.AsStackEnumerable().AsParallel().Sum();
    }

    [GpuKernel]
    public static void BlockDependentKernel(int[] data, int m, int[] result)
    {
        result[ThreadIndex.X] = data.AsStackEnumerable().Skip(BlockIndex.X * m).AsParallel().Sum();
    }

    // Lambdas that read thread-dependent values directly (second review, B1)
    [GpuKernel]
    public static void LambdaThreadIndexKernel(int[] data, int[] result)
    {
        result[ThreadIndex.X] = data.AsStackEnumerable().Select(x => x * (ThreadIndex.X + 1)).AsParallel().Sum();
    }

    [GpuKernel]
    public static void WhereThreadIndexKernel(int[] data, int[] result)
    {
        result[ThreadIndex.X] = data.AsStackEnumerable().Where(x => x > ThreadIndex.X).AsParallel().Sum();
    }

    [GpuDeviceFunction]
    public static int Tid() => ThreadIndex.X;

    [GpuKernel]
    public static void LambdaDeviceCallKernel(int[] data, int[] result)
    {
        result[ThreadIndex.X] = data.AsStackEnumerable().Select(x => x + Tid()).AsParallel().Sum();
    }

    // A block-dependent lambda in a kernel that is not cooperative: shared by the block, no GSH0100
    [GpuKernel]
    public static void LambdaBlockIndexKernel(int[] data, int[] result)
    {
        result[ThreadIndex.X] = data.AsStackEnumerable().Select(x => x + BlockIndex.X).AsParallel().Sum();
    }

    // Positions that only some threads reach (second review, B2)
    [GpuKernel]
    public static void SwitchArmKernel(int[] data, int[] result)
    {
        result[ThreadIndex.X] = ThreadIndex.X switch { 0 => data.AsStackEnumerable().AsParallel().Sum(), _ => -1 };
    }

    [GpuKernel]
    public static void ForIncrementorKernel(int[] data, int[] result)
    {
        int s = 0;
        for (int i = 0; i < ThreadIndex.X; i = i + 1 + (s = data.AsStackEnumerable().AsParallel().Sum()) * 0)
        {
        }

        result[ThreadIndex.X] = s;
    }

    // A thread leaves the loop that holds the call (second review, M2)
    [GpuKernel]
    public static void LoopBreakKernel(int[] data, int[] result)
    {
        int s = 0;
        for (int k = 0; k < 4; k++)
        {
            s += data.AsStackEnumerable().AsParallel().Sum();
            if (ThreadIndex.X == k)
            {
                break;
            }
        }

        result[ThreadIndex.X] = s;
    }

    [GpuKernel]
    public static void LoopContinueKernel(int[] data, int[] result)
    {
        int s = 0;
        for (int k = 0; k < 4; k++)
        {
            if (ThreadIndex.X == k)
            {
                continue;
            }

            s += data.AsStackEnumerable().AsParallel().Sum();
        }

        result[ThreadIndex.X] = s;
    }

    [GpuKernel]
    public static void LoopReturnAfterCallKernel(int[] data, int[] result)
    {
        for (int k = 0; k < 4; k++)
        {
            result[ThreadIndex.X] += data.AsStackEnumerable().AsParallel().Sum();
            if (ThreadIndex.X == k)
            {
                return;
            }
        }
    }

    // Re-review (R1): a selector that passes its element to a device function is shared (by the grid here), no warning
    [GpuDeviceFunction]
    public static int Square(int v) => v * v;

    [GpuKernel(Cooperative = true)]
    public static void DeviceFunctionSelectorKernel(int[] data, int[] result)
    {
        result[ThreadIndex.X] = data.AsStackEnumerable().Select(x => Square(x)).AsParallel().Sum();
    }
}
