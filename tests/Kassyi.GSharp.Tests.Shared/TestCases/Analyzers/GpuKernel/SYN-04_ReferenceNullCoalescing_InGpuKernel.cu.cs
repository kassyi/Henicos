using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers.GpuKernel;

public static class SYN_04_ReferenceNullCoalescing_InGpuKernel
{
    [GpuKernel]
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
    public static void ReferenceNullCoalescingKernel(string[] textArray)
    {
        int tid = ThreadIndex.X;
        // Null coalescing on reference type string
        string s = textArray[tid] ?? "default";
    }
}
