using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

// Gotos to the inner label of chained labels whose states are split by a barrier: one that crosses the barrier or a call
// that suspends is GSH0040's alone; one that crosses only a warp collective stays GSH0121's though the method has a
// barrier elsewhere
[SkipSnapshotConsistency("Sad Path")]
public static partial class ChainedLabelGotoBarrierKernels
{
    [GpuDeviceFunction]
    public static void Publish(int[] data, int value)
    {
        data[ThreadIndex.X] = value;
        Gpu.SyncThreads();
    }

    // A backward goto to the inner label across a block barrier
    [GpuKernel]
    public static void ChainedBarrierBackInnerKernel(int[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        int v = t;
    first:
    second:
        v += 1;
        Gpu.SyncThreads();
        if (n++ < 1)
        {
            goto second;
        }

        result[t] = v;
    }

    // A backward goto to the inner label across a call that suspends; the call is a statement of its own, so the
    // labels stay chained (a labeled statement that holds a split point is split into labels of their own)
    [GpuKernel]
    public static void ChainedSuspendingCallBackInnerKernel(int[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
    first:
    second:
        n += 1;
        Publish(result, n);
        if (n < 2)
        {
            goto second;
        }

        result[t] += 1;
    }

    // A barrier before the chain and a shuffle between the label and the goto: only the shuffle splits them
    [GpuKernel]
    public static void BarrierOutsideShuffleBackInnerKernel(int[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        int v = t;
        Gpu.SyncThreads();
    first:
    second:
        v += 1;
        v = Warp.ShuffleXorSync(0xFFFFFFFFu, v, 1);
        if (n++ < 1)
        {
            goto second;
        }

        result[t] = v;
    }
}
