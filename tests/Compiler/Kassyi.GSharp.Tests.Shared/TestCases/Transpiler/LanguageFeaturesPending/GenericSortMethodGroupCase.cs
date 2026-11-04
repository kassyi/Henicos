using System;
using Kassyi.GSharp.Core;

public static partial class GenericSortComparisonCase
{
    [GpuDeviceFunction]
    public static int CompareGeneric<T>(T a, T b) where T : unmanaged => 0;

    [GpuKernel]
    public static void Kernel(int[] r)
    {
        Span<int> s = stackalloc int[4];
        s.Sort(CompareGeneric<int>);
        r[0] = s[0];
    }
}
