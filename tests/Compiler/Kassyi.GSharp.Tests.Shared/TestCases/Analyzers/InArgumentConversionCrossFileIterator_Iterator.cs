using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

// Iterators whose body passes an argument that converts to an `in` parameter, called from another file
// (InArgumentConversionCrossFileIterator_Kernels.cu.cs)
public static partial class InArgumentConversionCrossFileIterator
{
    [GpuDeviceFunction]
    private static int Take(in long x) => (int)x;

    [GpuDeviceFunction]
    private static int TakeBoth(in long x, int y) => (int)x + y;

    [GpuDeviceFunction]
    public static IEnumerable<int> Converted(int n)
    {
        for (int i = 0; i < n; i++)
        {
            yield return Take(i);
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> ConvertedBeforeWrite(int n)
    {
        int k = 0;
        for (int i = 0; i < n; i++)
        {
            yield return TakeBoth(k, k++);
        }
    }
}
