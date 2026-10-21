using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.TestCases;

public partial class StackLinqFusionTestCases
{
    [GpuKernel]
    public static void SimpleWhereSelectSum(int[] data)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid == 0)
        {
            int sum = data.AsStackEnumerable()
                .Where(x => x > 5)
                .Select(x => x * 2)
                .Sum();
            data[0] = sum;
        }
    }

    [GpuKernel]
    public static void MultipleWhere(int[] data)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid == 0)
        {
            int sum = data.AsStackEnumerable()
                .Where(x => x > 5)
                .Where(x => x % 2 == 0)
                .Sum();
            data[0] = sum;
        }
    }

    [GpuKernel]
    public static void MultipleSelect(int[] data)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid == 0)
        {
            int sum = data.AsStackEnumerable()
                .Select(x => x * 2)
                .Select(x => x + 1)
                .Sum();
            data[0] = sum;
        }
    }

    [GpuKernel]
    public static void HellPattern(int[] data)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid == 0)
        {
            // HELL Pattern: Extreme chain of Where and Select
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
            data[0] = sum;
        }
    }
}
