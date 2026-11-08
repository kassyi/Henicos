using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

// The consumer names the arguments of the iterator out of order, so IteratorInliner spills them in the order written
public static class NamedArgumentIterator
{
    [GpuDeviceFunction]
    public static IEnumerable<int> Range(int start, int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return start + i;
        }
    }

    [GpuKernel]
    public static void NamedArgumentKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        foreach (var x in Range(count: 3, start: tid))
        {
            output[tid] += x;
        }
    }
}
