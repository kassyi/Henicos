using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public static class SYN17_UnresolvedTargetTypedNewCase
{
    [GpuKernel]
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
    public static void KernelWithUnresolvedTargetTypedNew(int[] data)
    {
        // Sad Path: Unresolvable target type context for new()
        var x = new();
    }
}
