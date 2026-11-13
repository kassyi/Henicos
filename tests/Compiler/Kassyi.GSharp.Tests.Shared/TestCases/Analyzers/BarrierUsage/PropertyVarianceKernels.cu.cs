using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

// A property getter, an indexer getter or a struct member with a body in source returns what its body returns, as a
// device function does: a thread index read in the body makes the result thread-variant
public static partial class PropertyVarianceKernels
{
    public struct Ids
    {
        public int Offset;

        public int Tid => ThreadIndex.X;

        public int Shifted
        {
            get
            {
                return ThreadIndex.X + Offset;
            }
        }

        public int Base => Offset * 2;

        public int this[int i] => i == 0 ? ThreadIndex.X : Offset;

        public int Lane() => ThreadIndex.X;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void ExpressionGetterKernel(int[] result)
    {
        var ids = new Ids();
        if (ids.Tid < 2)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        result[ThreadIndex.X] = 1;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void BlockGetterKernel(int[] result, int offset)
    {
        var ids = new Ids { Offset = offset };
        if (ids.Shifted < 2)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        result[ThreadIndex.X] = 1;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void IndexerGetterKernel(int[] result, int index)
    {
        var ids = new Ids();
        if (ids[index] < 2)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        result[ThreadIndex.X] = 1;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void MemberKernel(int[] result)
    {
        var ids = new Ids();
        if (ids.Lane() < 2)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        result[ThreadIndex.X] = 1;
    }

    // A getter that reads only a field of a uniform receiver: nothing to report
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void UniformGetterKernel(int[] result, int offset)
    {
        var ids = new Ids { Offset = offset };
        if (ids.Base < 2)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        result[ThreadIndex.X] = 1;
    }

    // The same getter on a thread-variant receiver
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void VariantReceiverKernel(int[] result)
    {
        var ids = new Ids();
        ids.Offset = ThreadIndex.X;
        if (ids.Base < 2)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        result[ThreadIndex.X] = 1;
    }

    // The control: the same barrier under the lane read directly
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void DirectConditionKernel(int[] result)
    {
        if (ThreadIndex.X < 2)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        result[ThreadIndex.X] = 1;
    }

    public struct Scale
    {
        public int Factor;

        [GpuDeviceFunction]
        public int Times(int x) => x * Factor;
    }

    // A device function member of a thread-variant struct value, called with a uniform argument
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void DeviceFunctionMemberKernel(int[] result)
    {
        var scale = new Scale();
        scale.Factor = ThreadIndex.X;
        if (scale.Times(2) < 2)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        result[ThreadIndex.X] = 1;
    }
}
