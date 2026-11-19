using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

[SkipSnapshotConsistency("Sad Path")]
public static partial class ChainedLabelGotoKernels
{
    // A backward goto to the inner label, from the state after the shuffle
    [GpuKernel]
    public static void ChainedShuffleBackInnerKernel(int[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        int v = t;
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

    // A forward goto to the inner label, from the state before the shuffle
    [GpuKernel]
    public static void ChainedShuffleForwardInnerKernel(int[] result)
    {
        int t = ThreadIndex.X;
        int v = t;
        if (v > 100)
        {
            goto second;
        }

        v = Warp.ShuffleXorSync(0xFFFFFFFFu, v, 1);
    first:
    second:
        v += 1;
        result[t] = v;
    }

    // Three labels on one statement: the innermost one is reached from another state
    [GpuKernel]
    public static void ChainedShuffleTripleInnerKernel(int[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        int v = t;
    first:
    second:
    third:
        v += 1;
        v = Warp.ShuffleXorSync(0xFFFFFFFFu, v, 1);
        if (n++ < 1)
        {
            goto third;
        }

        result[t] = v;
    }

    // Only the label 'mid', which starts a state, stands between the inner label and the goto: no split point does
    [GpuKernel]
    public static void ChainedLabelAcrossStateLabelKernel(int[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        int v = t;
        if (v < 0)
        {
            goto mid;
        }

        v = Warp.ShuffleXorSync(0xFFFFFFFFu, v, 1);
    first:
    second:
        v += 1;
    mid:
        v += 2;
        if (n++ < 1)
        {
            goto second;
        }

        result[t] = v;
    }

    // The same jump in a device function that suspends
    [GpuDeviceFunction]
    public static int ChainedShuffleBackInner(int v)
    {
        int n = 0;
    first:
    second:
        v += 1;
        v = Warp.ShuffleXorSync(0xFFFFFFFFu, v, 1);
        if (n++ < 1)
        {
            goto second;
        }

        return v;
    }

    [GpuKernel]
    public static void CallsChainedShuffleBackInnerKernel(int[] result)
    {
        int t = ThreadIndex.X;
        int r = ChainedShuffleBackInner(t);
        result[t] = r;
    }
}
