using System;
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public static partial class CpuDialectLibraryCallKernels
{
    [GpuKernel]
    public static void ElementAtKernel(int[] data, int[] result)
    {
        result[0] = data.AsStackEnumerable().ElementAt(1);
    }

    [GpuKernel]
    public static void ElementAtIndexKernel(int[] data, int index, int[] result)
    {
        result[0] = data.AsStackEnumerable().ElementAt(index);
    }

    [GpuKernel]
    public static void SortKernel(Span<int> data)
    {
        data.Sort();
    }

    [GpuKernel]
    public static void SelectAtRepeatedCurrentKernel(int[] data, int[] result)
    {
        result[0] = data.AsStackEnumerable()
            .Select((x, i) => x * 10 + i)
            .Where(v => v >= 20)
            .ElementAt(1);
    }

    [GpuKernel]
    public static void UnfusedFirstKernel(int[] data, int takeCount, int[] result)
    {
        result[0] = (data.AsStackEnumerable().Take(takeCount)).First();
    }

    [GpuKernel]
    public static void UnfusedLastKernel(int[] data, int takeCount, int[] result)
    {
        result[0] = (data.AsStackEnumerable().Take(takeCount)).Last();
    }

    [GpuKernel]
    public static void UnfusedSingleKernel(int[] data, int takeCount, int[] result)
    {
        result[0] = data.AsStackEnumerable().Take(takeCount).Single();
    }

    [GpuKernel]
    public static void UnfusedSingleOrDefaultKernel(int[] data, int takeCount, int[] result)
    {
        result[0] = data.AsStackEnumerable().Take(takeCount).SingleOrDefault();
    }
}
