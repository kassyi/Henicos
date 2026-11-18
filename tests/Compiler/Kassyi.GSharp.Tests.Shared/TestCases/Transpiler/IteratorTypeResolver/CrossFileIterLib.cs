using System.Collections.Generic;
using Kassyi.GSharp.Core;

public static partial class IterLib
{
    [GpuDeviceFunction]
    public static IEnumerable<int> Inner(int n)
    {
        yield return n;
        yield return n + 1;
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> Outer(int n)
    {
        var it = Inner(n);
        foreach (var x in it)
        {
            yield return x * 10;
        }
    }
}
