#nullable enable
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.PendingBugs;

// #1083: StackLinq chains deeper than 50 stages containing StackEnumerable.OfType<T>() or static StackLinqExtensions.Where(...).
[SkipSnapshotConsistency("Pending bug verification")]
public static partial class StackLinqChainDepthBypassKernels
{
    [GpuKernel]
    public static void ChainBypassedViaOfType(int[] data, int[] output)
    {
        output[0] = data.AsStackEnumerable()
            .Where(x => x > 0).Where(x => x > 0).Where(x => x > 0).Where(x => x > 0).Where(x => x > 0)
            .Where(x => x > 0).Where(x => x > 0).Where(x => x > 0).Where(x => x > 0).Where(x => x > 0)
            .Where(x => x > 0).Where(x => x > 0).Where(x => x > 0).Where(x => x > 0).Where(x => x > 0)
            .Where(x => x > 0).Where(x => x > 0).Where(x => x > 0).Where(x => x > 0).Where(x => x > 0)
            .Where(x => x > 0).Where(x => x > 0).Where(x => x > 0).Where(x => x > 0).Where(x => x > 0)
            .Where(x => x > 0).Where(x => x > 0).Where(x => x > 0).Where(x => x > 0).Where(x => x > 0)
            .OfType<int>()
            .Select(x => x + 1).Select(x => x + 1).Select(x => x + 1).Select(x => x + 1).Select(x => x + 1)
            .Select(x => x + 1).Select(x => x + 1).Select(x => x + 1).Select(x => x + 1).Select(x => x + 1)
            .Select(x => x + 1).Select(x => x + 1).Select(x => x + 1).Select(x => x + 1).Select(x => x + 1)
            .Select(x => x + 1).Select(x => x + 1).Select(x => x + 1).Select(x => x + 1).Select(x => x + 1)
            .Select(x => x + 1).Select(x => x + 1).Select(x => x + 1).Select(x => x + 1).Select(x => x + 1)
            .Sum();
    }

    [GpuKernel]
    public static void ChainBypassedViaStaticInvocation(int[] data, int[] output)
    {
        output[0] = StackLinqExtensions.Where(
                data.AsStackEnumerable()
                    .Where(x => x > 0).Where(x => x > 0).Where(x => x > 0).Where(x => x > 0).Where(x => x > 0)
                    .Where(x => x > 0).Where(x => x > 0).Where(x => x > 0).Where(x => x > 0).Where(x => x > 0)
                    .Where(x => x > 0).Where(x => x > 0).Where(x => x > 0).Where(x => x > 0).Where(x => x > 0)
                    .Where(x => x > 0).Where(x => x > 0).Where(x => x > 0).Where(x => x > 0).Where(x => x > 0)
                    .Where(x => x > 0).Where(x => x > 0).Where(x => x > 0).Where(x => x > 0).Where(x => x > 0)
                    .Where(x => x > 0).Where(x => x > 0).Where(x => x > 0).Where(x => x > 0).Where(x => x > 0),
                x => x > 0)
            .Select(x => x + 1).Select(x => x + 1).Select(x => x + 1).Select(x => x + 1).Select(x => x + 1)
            .Select(x => x + 1).Select(x => x + 1).Select(x => x + 1).Select(x => x + 1).Select(x => x + 1)
            .Select(x => x + 1).Select(x => x + 1).Select(x => x + 1).Select(x => x + 1).Select(x => x + 1)
            .Select(x => x + 1).Select(x => x + 1).Select(x => x + 1).Select(x => x + 1).Select(x => x + 1)
            .Select(x => x + 1).Select(x => x + 1).Select(x => x + 1).Select(x => x + 1).Select(x => x + 1)
            .Sum();
    }
}
