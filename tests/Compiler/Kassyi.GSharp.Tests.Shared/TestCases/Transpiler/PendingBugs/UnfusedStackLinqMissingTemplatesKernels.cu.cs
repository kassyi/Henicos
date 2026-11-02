#nullable enable
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Transpiler.PendingBugs;

// #1081: Unfused StackLinq fallback emits calls to C++ templates that do not exist in StackLinq.cuh:
// 1) TakeLast / SkipLast followed by non-fused terminal ElementAtOrDefault / SingleOrDefault emits StackLinq::TakeLast / StackLinq::SkipLast.
// 2) Unfused query variable `var q = ...; q.Sum(x => x * 2)` and `q.LastOrDefault(x => x > 10)` emit 2-argument C++ calls missing in StackLinq.cuh.
[SkipSnapshotConsistency("Pending bug verification")]
public static partial class UnfusedStackLinqMissingTemplatesKernels
{
    [GpuKernel]
    public static void TakeLastElementAtOrDefaultKernel(int[] input, int[] output)
    {
        output[0] = input.AsStackEnumerable().TakeLast(2).ElementAtOrDefault(0);
        output[1] = input.AsStackEnumerable().SkipLast(1).SingleOrDefault();
    }

    [GpuKernel]
    public static void UnfusedSelectorSumAndPredicateLastOrDefaultKernel(int[] input, int[] output)
    {
        var query = input.AsStackEnumerable().Where(x => x > 0);
        output[0] = query.Sum(x => x * 2);
        output[1] = query.LastOrDefault(x => x > 10);
    }
}
