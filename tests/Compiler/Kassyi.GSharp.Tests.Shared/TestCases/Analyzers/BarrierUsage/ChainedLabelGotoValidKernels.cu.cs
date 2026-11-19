using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

[SkipSnapshotConsistency("Analyzer negative test cases")]
public static partial class ChainedLabelGotoValidKernels
{
    // The goto is in the state of the inner label, so C# finds the label in the same case block
    [GpuKernel]
    public static void ChainedSameStateInnerKernel(int[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        int v = t;
    first:
    second:
        v += 1;
        if (n++ < 1)
        {
            goto second;
        }

        v = Warp.ShuffleXorSync(0xFFFFFFFFu, v, 1);
        result[t] = v;
    }

    // A goto to the outermost label of the chain, which starts a state
    [GpuKernel]
    public static void ChainedShuffleBackOuterKernel(int[] result)
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
            goto first;
        }

        result[t] = v;
    }

    // Each label has a statement of its own, so both are top-level labels
    [GpuKernel]
    public static void SplitShuffleBackInnerKernel(int[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        int v = t;
    first:
        ;
    second:
        v += 1;
        v = Warp.ShuffleXorSync(0xFFFFFFFFu, v, 1);
        if (n++ < 1)
        {
            goto second;
        }

        result[t] = v;
    }

    // The unroll hint stays right before the chained labels of its loop on the CPU paths (no GSH0017)
    [GpuKernel]
    public static void UnrolledChainedLabelLoopKernel(int[] result)
    {
        int t = ThreadIndex.X;
        int acc = Warp.ShuffleXorSync(0xFFFFFFFFu, t, 1);
        if (acc < 0)
        {
            goto b;
        }

        Gpu.Hint.Unroll(2);
    a:
    b:
        for (int i = 0; i < 3; i++)
        {
            acc += i;
        }

        result[t] = acc;
    }
}
