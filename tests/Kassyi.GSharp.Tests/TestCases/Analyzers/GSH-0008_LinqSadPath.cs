// ReSharper disable All
using System.Linq;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public class GSH_0008_LinqSadPath
{
    // 1. Sad Path: Simple query expression inside a [GpuKernel]
    [GpuKernel]
    public void SimpleQueryExpressionKernel(int[] array)
    {
        var query = from x in array select x;
    }

    // 2. Edge Case: Complex nested query expression inside a [GpuKernel]
    [GpuKernel]
    public void NestedQueryExpressionKernel(int[] array1, int[] array2)
    {
        var query = from x in array1
                    from y in array2
                    where x == y
                    select x * y;
    }

    // 3. Edge Case: Method-chaining LINQ inside a [GpuKernel]
    [GpuKernel]
    public void MethodChainingLinqKernel(int[] array)
    {
        var result = array.Where(x => x > 0).Select(x => x * 2);
    }
}
