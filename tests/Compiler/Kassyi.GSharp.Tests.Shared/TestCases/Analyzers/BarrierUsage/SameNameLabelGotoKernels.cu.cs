using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

// Sibling blocks that declare the same label name: each goto jumps to the label of its own block, which C# binds, and
// is judged by that label, not by the first label of the method with its name
[SkipSnapshotConsistency("Sad Path")]
public static partial class SameNameLabelGotoKernels
{
    // Neither goto crosses the barrier between the blocks
    [GpuKernel]
    public static void SiblingLabelsKernel(int[] result)
    {
        int n = 0;
        int m = 0;
        {
        again:
            n++;
            if (n < 2)
            {
                goto again;
            }
        }

        Gpu.SyncThreads();
        {
        again:
            m++;
            if (m < 3)
            {
                goto again;
            }
        }

        result[ThreadIndex.X] = n + m;
    }

    // The goto of the second block crosses the barrier inside its own block
    [GpuKernel]
    public static void SiblingLabelsOwnBarrierKernel(int[] result)
    {
        int n = 0;
        int m = 0;
        {
        again:
            n++;
            if (n < 2)
            {
                goto again;
            }
        }

        {
        again:
            m++;
            Gpu.SyncThreads();
            if (m < 3)
            {
                goto again;
            }
        }

        result[ThreadIndex.X] = n + m;
    }

    // Chained labels in sibling blocks: the second goto reaches its own inner label across a shuffle only
    [GpuKernel]
    public static void SiblingChainedLabelsKernel(int[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        int v = t;
        {
        first:
        second:
            n++;
            if (n < 2)
            {
                goto second;
            }
        }

        Gpu.SyncThreads();
        {
        first:
        second:
            v += 1;
            v = Warp.ShuffleXorSync(0xFFFFFFFFu, v, 1);
            if (n++ < 3)
            {
                goto second;
            }
        }

        result[t] = v;
    }
}
