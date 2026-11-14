using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

// A value that the control flow graph builds through flow captures (a conditional, a switch expression, a with
// expression, an object initializer) is followed to the variable the capture stands for: an element write whose value
// branches still writes the array, and a field written by an initializer still writes the struct
public static partial class FlowCaptureWriteKernels
{
    public struct InitPair
    {
        public int X { get; init; }
    }

    public struct Holder
    {
        public int Offset;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void ConditionalValueKernel(int[] a)
    {
        a[ThreadIndex.X] = ThreadIndex.X > 0 ? 1 : 2;
        if (a[0] == 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void WithValueKernel(InitPair[] a)
    {
        a[ThreadIndex.X] = a[ThreadIndex.X] with { X = 1 };
        if (a[0].X == 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void SwitchValueKernel(int[] a)
    {
        a[ThreadIndex.X] = ThreadIndex.X switch
        {
            0 => 1,
            _ => 2
        };
        if (a[0] == 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void CompoundConditionalKernel(int[] a, bool flag)
    {
        a[ThreadIndex.X] += flag ? 1 : 2;
        if (a[0] == 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void InitializerKernel(int[] a)
    {
        int lane = ThreadIndex.X;
        var holder = new Holder { Offset = lane };
        if (holder.Offset < 16)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        a[ThreadIndex.X] = holder.Offset;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void WithLocalKernel(int[] a)
    {
        var pair = default(InitPair) with { X = ThreadIndex.X };
        if (pair.X < 16)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        a[ThreadIndex.X] = pair.X;
    }

    // Not reported: a barrier between the write and the read, and an initializer with a uniform value
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void BarrierBetweenKernel(int[] a, int n)
    {
        a[ThreadIndex.X] = ThreadIndex.X > 0 ? 1 : 2;
        Gpu.SyncThreads();
        var holder = new Holder { Offset = n };
        if (a[0] == 0 && holder.Offset < 16)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    // The control: the same write with a plain value
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void PlainValueKernel(int[] a)
    {
        a[ThreadIndex.X] = 1;
        if (a[0] == 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    // A local that holds one of two arrays, chosen by a conditional, refers to both
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void ConditionalAliasKernel(int[] a, int[] b, bool flag)
    {
        int[] alias = flag ? a : b;
        alias[ThreadIndex.X] = 1;
        if (b[0] == 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    public struct Cell
    {
        public int X;
    }

    // A field of an element, written with a conditional value
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void ElementFieldConditionalKernel(Cell[] a, bool flag)
    {
        a[ThreadIndex.X].X = flag ? 1 : 2;
        if (a[0].X == 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    // Not reported: a with expression copies the element into a local and writes no memory
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void WithCopyKernel(InitPair[] a, int[] result)
    {
        var copy = a[ThreadIndex.X] with { X = 1 };
        if (a[0].X == 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        result[ThreadIndex.X] = copy.X;
    }

    public struct AutoInit
    {
        public int V { get; init; }
    }

    public struct BodyInit
    {
        private int _v;

        public int V
        {
            get => _v;
            init => _v = value;
        }
    }

    // An auto-property set by an object initializer
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void InitAutoPropertyKernel(int[] a)
    {
        int lane = ThreadIndex.X;
        var init = new AutoInit { V = lane };
        if (init.V < 16)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        a[ThreadIndex.X] = init.V;
    }

    // An init accessor with a body, set by an object initializer
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void InitAccessorBodyKernel(int[] a)
    {
        int lane = ThreadIndex.X;
        var init = new BodyInit { V = lane };
        if (init.V < 16)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
        a[ThreadIndex.X] = init.V;
    }
}
