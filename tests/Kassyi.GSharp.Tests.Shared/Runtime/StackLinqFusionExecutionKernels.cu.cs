using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

[SkipSnapshotConsistency]
public partial class StackLinqFusionExecutionKernels
{
    [GpuKernel]
    public static void SimpleWhereSelectSum(int[] data, int[] result)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid == 0)
        {
            int sum = data.AsStackEnumerable()
                .Where(x => x > 5)
                .Select(x => x * 2)
                .Sum();
            result[0] = sum;
        }
    }

    [GpuKernel]
    public static void MultipleWhere(int[] data, int[] result)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid == 0)
        {
            int sum = data.AsStackEnumerable()
                .Where(x => x > 5)
                .Where(x => x % 2 == 0)
                .Sum();
            result[0] = sum;
        }
    }

    [GpuKernel]
    public static void MultipleSelect(int[] data, int[] result)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid == 0)
        {
            int sum = data.AsStackEnumerable()
                .Select(x => x * 2)
                .Select(x => x + 1)
                .Sum();
            result[0] = sum;
        }
    }

    [GpuKernel]
    public static void HellPattern(int[] data, int[] result)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid == 0)
        {
            int sum = data.AsStackEnumerable()
                .SkipLast(2)
                .TakeLast(95)
                .Skip(3)
                .Take(90)
                .Where(x => x % 2 == 0)
                .SkipWhile(x => x < 20)
                .TakeWhile(x => x <= 80)
                .Select(x => x * 2)
                .Select(x => x + 1)
                .Where(x => x < 100)
                .Sum();
            result[0] = sum;
        }
    }

    [GpuKernel]
    public static void TestCount(int[] data, int[] result)
    {
        if (ThreadIndex.X == 0) result[0] = data.AsStackEnumerable().Count(x => x > 5);
    }

    [GpuKernel]
    public static void TestAny(int[] data, int[] result)
    {
        if (ThreadIndex.X == 0) result[0] = data.AsStackEnumerable().Any(x => x > 5) ? 1 : 0;
    }

    [GpuKernel]
    public static void TestAll(int[] data, int[] result)
    {
        if (ThreadIndex.X == 0) result[0] = data.AsStackEnumerable().All(x => x > 5) ? 1 : 0;
    }

    [GpuKernel]
    public static void TestFirst(int[] data, int[] result)
    {
        if (ThreadIndex.X == 0) result[0] = data.AsStackEnumerable().First(x => x > 5);
    }

    [GpuKernel]
    public static void TestFirstOrDefault(int[] data, int[] result)
    {
        if (ThreadIndex.X == 0) result[0] = data.AsStackEnumerable().FirstOrDefault(x => x > 100);
    }

    [GpuKernel]
    public static void TestLast(int[] data, int[] result)
    {
        if (ThreadIndex.X == 0) result[0] = data.AsStackEnumerable().Last((int x) => x < 5);
    }

    [GpuKernel]
    public static void TestLastOrDefault(int[] data, int[] result)
    {
        if (ThreadIndex.X == 0) result[0] = data.AsStackEnumerable().LastOrDefault((int x) => x > 100);
    }

    [GpuKernel]
    public static void TestMax(int[] data, int[] result)
    {
        if (ThreadIndex.X == 0) result[0] = data.AsStackEnumerable().Max();
    }

    [GpuKernel]
    public static void TestMin(int[] data, int[] result)
    {
        if (ThreadIndex.X == 0) result[0] = data.AsStackEnumerable().Min();
    }

    [GpuKernel]
    public static void TestAverage(int[] data, int[] result)
    {
        if (ThreadIndex.X == 0) result[0] = (int)data.AsStackEnumerable().Average();
    }

    [GpuKernel]
    public static void TestAggregate(int[] data, int[] result)
    {
        if (ThreadIndex.X == 0) result[0] = data.AsStackEnumerable().Aggregate((int acc, int x) => acc + x);
    }

    [GpuKernel]
    public static void TestContains(int[] data, int[] result)
    {
        if (ThreadIndex.X == 0) result[0] = data.AsStackEnumerable().Contains(5) ? 1 : 0;
    }

    [GpuKernel]
    public static void TestTake(int[] data, int[] result)
    {
        if (ThreadIndex.X == 0) result[0] = data.AsStackEnumerable().Take(3).Sum();
    }

    [GpuKernel]
    public static void TestSkip(int[] data, int[] result)
    {
        if (ThreadIndex.X == 0) result[0] = data.AsStackEnumerable().Skip(3).Sum();
    }

    [GpuKernel]
    public static void TestTakeWhile(int[] data, int[] result)
    {
        if (ThreadIndex.X == 0) result[0] = data.AsStackEnumerable().TakeWhile(x => x < 5).Sum();
    }

    [GpuKernel]
    public static void TestSkipWhile(int[] data, int[] result)
    {
        if (ThreadIndex.X == 0) result[0] = data.AsStackEnumerable().SkipWhile(x => x < 5).Sum();
    }

    [GpuKernel]
    public static void TestTakeLast(int[] data, int[] result)
    {
        if (ThreadIndex.X == 0) result[0] = data.AsStackEnumerable().TakeLast(3).Sum();
    }

    [GpuKernel]
    public static void TestSkipLast(int[] data, int[] result)
    {
        if (ThreadIndex.X == 0) result[0] = data.AsStackEnumerable().SkipLast(3).Sum();
    }
}



