using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.TestCases;

public partial class StackLinqTestKernel
{
    [GpuKernel]
    public static void RunLinq(int[] data)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid == 0)
        {
            int count = data.AsStackEnumerable()
                            .Where(x => x > 5)
                            .Select(x => x * 2)
                            .Count();
            
            data[0] = count;
        }
    }

    [GpuKernel]
    public static void RunLinqAny(int[] data)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid == 0)
        {
            bool hasEven = data.AsStackEnumerable().Any(x => x % 2 == 0);
            data[0] = hasEven ? 1 : 0;
        }
    }

    [GpuKernel]
    public static void RunLinqSum(int[] data)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid == 0)
        {
            int sum = data.AsStackEnumerable().Select(x => x * 2).Sum();
            data[0] = sum;
        }
    }

    [GpuKernel]
    public static void RunLinqFirst(int[] data)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid == 0)
        {
            int first = data.AsStackEnumerable().FirstOrDefault(x => x > 10);
            data[0] = first;
        }
    }
}
