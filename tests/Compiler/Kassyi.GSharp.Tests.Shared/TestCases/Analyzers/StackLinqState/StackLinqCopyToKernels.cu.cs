using System;
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

public static partial class TestKernelContainer
{
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void StackLinqCopyToKernel(int[] data, int[] result)
    {
        // CopyTo has no device implementation; a kernel writes the copy as a loop
        result[0] = data
            .AsStackEnumerable()
            .Where(static x => x > 0)
            .CopyTo(result.AsSpan());
    }
}
