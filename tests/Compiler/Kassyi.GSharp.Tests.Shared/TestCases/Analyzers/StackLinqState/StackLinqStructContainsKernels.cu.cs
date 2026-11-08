using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

public struct StackLinqPoint
{
    public int X;
    public int Y;
}

public static partial class TestKernelContainer
{
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void StackLinqStructContainsKernel(StackLinqPoint[] points, int[] result)
    {
        // C# compares struct elements with EqualityComparer<T>.Default: fused and unfused
        var origin = default(StackLinqPoint);
        result[ThreadIndex.X] = points.AsStackEnumerable().Contains(origin) ? 1 : 0;
        var query = points.AsStackEnumerable();
        result[ThreadIndex.X] += query.Contains(origin) ? 1 : 0;
    }
}
