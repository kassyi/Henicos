using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

#pragma warning disable CS0164 // The outer label of each chain stays so the inner label is a chained one; the gotos name the inner label
/// <summary>
/// Gotos to chained labels that the CPU state machine can lower: the first label, a label with a statement of its own,
/// and an inner label from its own state (GSH0121 reports the other ones).
/// </summary>
public static partial class ChainedLabelGotoDifferentialKernels
{
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
}

#pragma warning restore CS0164
