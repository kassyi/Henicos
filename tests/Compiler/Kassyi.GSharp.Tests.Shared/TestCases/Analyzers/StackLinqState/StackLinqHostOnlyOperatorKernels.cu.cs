using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

public static partial class TestKernelContainer
{
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void StackLinqHostOnlyOperatorKernel(int[] data)
    {
        // IndexOf is host-only; a kernel writes the search as a loop
        data[ThreadIndex.X] = data
            .AsStackEnumerable()
            .IndexOf(static x => x > 0);
    }
}
