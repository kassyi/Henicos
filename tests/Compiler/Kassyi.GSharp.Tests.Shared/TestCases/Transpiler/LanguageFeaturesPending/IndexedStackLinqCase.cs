using System;
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

public static partial class IndexedStackLinqCase
{
    [GpuKernel]
    public static void Kernel(int[] xs, int[] r)
    {
        var slice = xs.AsSpan().Slice(0, 4);
        int fused = slice.AsStackEnumerable().Where((x, i) => i < 2 && x > 0).Select((x, i) => x + i).Sum();
        var unfused = slice.AsStackEnumerable();
        int unfusedTw = unfused.TakeWhile((x, i) => i < 2).Sum();
        int unfusedSw = unfused.SkipWhile((x, i) => i < 1).Sum();
        r[0] = fused + unfusedTw + unfusedSw;
    }
}
