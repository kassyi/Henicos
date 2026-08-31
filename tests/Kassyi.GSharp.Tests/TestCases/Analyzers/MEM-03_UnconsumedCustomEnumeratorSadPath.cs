using System;
using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public static class UnconsumedEnumeratorTest
{
    public static IEnumerable<int> Generate()
    {
        yield return 1;
    }

    [GpuKernel]
    public static void KernelWithUnconsumedEnumerator()
    {
        var enumerator = Generate().GetEnumerator();
        enumerator.MoveNext();
        // Error: Unconsumed custom enumerator missing Dispose()
    }
}
