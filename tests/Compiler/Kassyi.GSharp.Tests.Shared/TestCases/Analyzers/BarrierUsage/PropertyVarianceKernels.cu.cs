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

    public struct Lanes
    {
        public int X;

        public Lanes(int x)
        {
            X = x + ThreadIndex.X;
        }

        public static int operator +(Lanes a, int b) => ThreadIndex.X + b;

        public static Lanes operator -(Lanes a, Lanes b) => new Lanes(0);

        public static int operator ~(Lanes a) => ThreadIndex.X;

        public static implicit operator int(Lanes a) => ThreadIndex.X;

        public void Set(int value)
        {
            X = value;
        }

        public readonly int Get() => X;
    }

    // A user-defined binary operator that returns the lane
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void BinaryOperatorKernel(int[] result)
    {
        var lanes = default(Lanes);
        if (lanes + 1 < 2)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        result[ThreadIndex.X] = 1;
    }

    // A user-defined operator that returns a struct whose constructor stores the lane
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void StructOperatorKernel(int[] result)
    {
        var a = default(Lanes);
        var b = default(Lanes);
        var difference = a - b;
        if (difference.X < 2)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        result[ThreadIndex.X] = 1;
    }

    // A user-defined unary operator that returns the lane
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void UnaryOperatorKernel(int[] result)
    {
        var lanes = default(Lanes);
        if (~lanes < 2)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        result[ThreadIndex.X] = 1;
    }

    // A user-defined implicit conversion that returns the lane
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void ConversionKernel(int[] result)
    {
        var lanes = default(Lanes);
        int converted = lanes;
        if (converted < 2)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        result[ThreadIndex.X] = 1;
    }

    // A constructor that stores the lane in a field
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void ConstructorKernel(int[] result)
    {
        var lanes = new Lanes(0);
        if (lanes.X < 2)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        result[ThreadIndex.X] = 1;
    }

    // A member that writes the lane into its receiver, read back through a field and through a readonly member
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void ReceiverWriteKernel(int[] result)
    {
        var lanes = default(Lanes);
        lanes.Set(ThreadIndex.X);
        if (lanes.X < 2)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        result[ThreadIndex.X] = 1;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void ReceiverWriteMemberReadKernel(int[] result)
    {
        var lanes = default(Lanes);
        lanes.Set(ThreadIndex.X);
        if (lanes.Get() < 2)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        result[ThreadIndex.X] = 1;
    }

    // Not reported: the same member with a uniform value
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void UniformReceiverWriteKernel(int[] result, int n)
    {
        var lanes = default(Lanes);
        lanes.Set(n);
        if (lanes.X < 2)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        result[ThreadIndex.X] = 1;
    }

    public struct Steps
    {
        public int V;

        public static Steps operator ++(Steps a)
        {
            var next = a;
            next.V = ThreadIndex.X;
            return next;
        }

        public static Steps operator +(Steps a, int b)
        {
            var next = a;
            next.V = ThreadIndex.X + b;
            return next;
        }

        public static Steps operator -(Steps a, int b)
        {
            var next = a;
            next.V = a.V - b;
            return next;
        }
    }

    public struct Plain
    {
        public int V;

        public Plain(int x)
        {
            V = x;
        }

        public static int operator +(Plain a, int b) => a.V + b;

        public static implicit operator int(Plain a) => a.V;
    }

    // A user-defined increment that stores the lane
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void IncrementOperatorKernel(int[] result)
    {
        var steps = default(Steps);
        steps++;
        if (steps.V < 2)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        result[ThreadIndex.X] = 1;
    }

    // A compound assignment through a user-defined operator that stores the lane
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void CompoundOperatorKernel(int[] result)
    {
        var steps = default(Steps);
        steps += 1;
        if (steps.V < 2)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        result[ThreadIndex.X] = 1;
    }

    // Not reported: a user-defined operator whose operands and result are uniform
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void UniformOperatorKernel(int[] result, int n)
    {
        var steps = default(Steps);
        steps.V = n;
        steps = steps - 1;
        if (steps.V < 2)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        result[ThreadIndex.X] = 1;
    }

    // Not reported: a constructor, an operator and a conversion of a struct that read uniform values only
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void UniformConstructorKernel(int[] result, int n)
    {
        var plain = new Plain(n);
        int converted = plain;
        if (plain.V < 2 || plain + 1 < 2 || converted < 2)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        result[ThreadIndex.X] = 1;
    }
}
