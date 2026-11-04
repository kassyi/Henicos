using System;
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

public static partial class AggregateBindingCase
{
    [GpuKernel]
    public static void Kernel(int[] xs, int[] r)
    {
        var slice = xs.AsSpan().Slice(0, 4);
        int staticCall = StackLinqExtensions.Aggregate(slice.AsStackEnumerable(), (a, b) => a + b);
        int namedReordered = slice.AsStackEnumerable().Aggregate(func: (a, b) => a + b, seed: 10);
        r[0] = staticCall + namedReordered;
    }
}
