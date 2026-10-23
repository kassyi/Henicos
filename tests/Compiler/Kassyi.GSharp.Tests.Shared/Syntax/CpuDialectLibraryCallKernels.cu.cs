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

    [GpuKernel]
    public static void FusedFirstKernel(int[] data, int takeCount, int[] result)
    {
        result[0] = data.AsStackEnumerable().Take(takeCount).First();
    }

    [GpuKernel]
    public static void FusedFirstPredicateKernel(int[] data, int threshold, int[] result)
    {
        result[0] = data.AsStackEnumerable().First(x => x > threshold);
    }

    [GpuKernel]
    public static void FusedLastKernel(int[] data, int takeCount, int[] result)
    {
        result[0] = data.AsStackEnumerable().Take(takeCount).Last();
    }

    [GpuKernel]
    public static void FusedLastPredicateKernel(int[] data, int threshold, int[] result)
    {
        result[0] = data.AsStackEnumerable().Last((int x) => x > threshold);
    }

    [GpuKernel]
    public static void FusedMaxKernel(int[] data, int takeCount, int[] result)
    {
        result[0] = data.AsStackEnumerable().Take(takeCount).Max();
    }

    [GpuKernel]
    public static void FusedMinKernel(int[] data, int takeCount, int[] result)
    {
        result[0] = data.AsStackEnumerable().Take(takeCount).Min();
    }

    [GpuKernel]
    public static void FusedAverageKernel(int[] data, int takeCount, double[] result)
    {
        result[0] = data.AsStackEnumerable().Take(takeCount).Average();
    }

    [GpuKernel]
    public static void FusedSeedlessAggregateKernel(int[] data, int takeCount, int[] result)
    {
        result[0] = data.AsStackEnumerable().Take(takeCount).Aggregate((int acc, int x) => acc + x);
    }

    [GpuKernel]
    public static void UnfusedMaxKernel(int[] data, int takeCount, int[] result)
    {
        result[0] = (data.AsStackEnumerable().Take(takeCount)).Max();
    }

    [GpuKernel]
    public static void UnfusedMinKernel(int[] data, int takeCount, int[] result)
    {
        result[0] = (data.AsStackEnumerable().Take(takeCount)).Min();
    }

    [GpuKernel]
    public static void UnfusedAverageKernel(int[] data, int takeCount, double[] result)
    {
        result[0] = (data.AsStackEnumerable().Take(takeCount)).Average();
    }

    [GpuKernel]
    public static void UnfusedSeedlessAggregateKernel(int[] data, int takeCount, int[] result)
    {
        result[0] = (data.AsStackEnumerable().Take(takeCount)).Aggregate((int acc, int x) => acc + x);
    }

    [GpuKernel]
    public static void EmptySequenceBoundaryKernel(int[] data, int[] results)
    {
        results[0] = data.AsStackEnumerable().Take(0).Sum();
        results[1] = data.AsStackEnumerable().Take(0).Count();
        results[2] = data.AsStackEnumerable().Take(0).Any() ? 1 : 0;
        results[3] = data.AsStackEnumerable().Take(0).All(x => x > 0) ? 1 : 0;
        results[4] = data.AsStackEnumerable().Take(0).Contains(7) ? 1 : 0;
        results[5] = data.AsStackEnumerable().Take(0).FirstOrDefault();
        results[6] = data.AsStackEnumerable().Take(0).LastOrDefault();
        results[7] = data.AsStackEnumerable().Take(0).SingleOrDefault();
        results[8] = data.AsStackEnumerable().Take(0).ElementAtOrDefault(0);
        results[9] = data.AsStackEnumerable().Take(0).Aggregate(42, (int acc, int x) => acc + x);
        results[10] = (data.AsStackEnumerable().Take(0)).Sum();
        results[11] = (data.AsStackEnumerable().Take(0)).Aggregate(42, (int acc, int x) => acc + x);
    }
}
