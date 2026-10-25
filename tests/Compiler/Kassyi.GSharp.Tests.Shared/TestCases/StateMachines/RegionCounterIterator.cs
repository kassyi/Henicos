using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

public static class RegionCounterIterator
{
    [GpuDeviceFunction]
    public static IEnumerable<int> Range(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return i;
        }
    }

    [GpuKernel]
    public static void RegionCounterKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        foreach (var x in Range(3))
        {
            output[tid] += x;
        }
    }
}
