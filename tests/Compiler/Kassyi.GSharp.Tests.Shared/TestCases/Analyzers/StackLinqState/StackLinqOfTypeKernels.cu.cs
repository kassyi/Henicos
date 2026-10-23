using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

public static partial class TestKernelContainer
{
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void StackLinqOfTypeKernel(int[] data)
    {
        // OfType is host-only; a kernel has no type tests to filter on
        data[ThreadIndex.X] = data
            .AsStackEnumerable()
            .OfType<int>()
            .Count();
    }
}
