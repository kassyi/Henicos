using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

public static partial class TestKernelContainer
{
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void StackLinqStateOverloadKernel(int[] data, int threshold)
    {
        // The state overload is host-only; a kernel lambda reads threshold directly instead
        data[ThreadIndex.X] = data.AsStackEnumerable().Count(threshold, static (x, t) => x > t);
    }
}
