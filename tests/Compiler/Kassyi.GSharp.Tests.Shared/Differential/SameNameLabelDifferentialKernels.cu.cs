using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public static partial class SameNameLabelDifferentialKernels
{
    // Sibling blocks with the same label name on both sides of a barrier: each goto loops in its own block
    [GpuKernel]
    public static void SiblingLabelsKernel(int[] result)
    {
        int t = ThreadIndex.X;
        int n = 0;
        int m = 0;
        {
        again:
            n++;
            if (n < 2 + (t & 1))
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

        result[t] = n * 10 + m;
    }
}
