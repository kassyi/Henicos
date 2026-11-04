#nullable enable
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Transpiler.PendingBugs;

// #1086: An unfused StackLinq query held in a local, whose stage lambda captures a local, emits its C# enumerator
// type (StackEnumerable<TakeEnumerator<SelectEnumerator<...>>>) as a user struct with a static_assert size check
// that mismatches the C++ closure field size (failing native build with C2338).
[SkipSnapshotConsistency("Pending bug verification")]
public static partial class UnfusedStackLinqCapturingLambdaLocalKernels
{
    [GpuKernel]
    public static void UnfusedQueryWithCapturingLambdaInLocalKernel(int[] data, int[] output)
    {
        Span<int> slice = data;
        int sideEffects = 0;
        var query = slice.AsStackEnumerable()
            .Select(x =>
            {
                sideEffects++;
                return x * 2;
            })
            .Take(0);
        output[0] = query.Sum() + sideEffects;
    }
}
