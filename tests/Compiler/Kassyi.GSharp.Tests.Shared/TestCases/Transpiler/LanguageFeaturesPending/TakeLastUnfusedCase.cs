using System;
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

public static partial class TakeLastUnfusedCase
{
    [GpuKernel]
    public static void Kernel(int[] xs, int[] r)
    {
        var slice = xs.AsSpan().Slice(0, 4);
        r[0] = slice.AsStackEnumerable().TakeLast(1).Single();
    }
}
