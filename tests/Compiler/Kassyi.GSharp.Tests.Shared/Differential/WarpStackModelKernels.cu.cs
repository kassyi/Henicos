using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Kernels whose lanes meet at the same instance of a warp collective by how often each reached it on the same static
/// call path: the same call site of a device function reached in different iterations, the copies of a
/// <see cref="Gpu.ForUnroll"/> body, and visits that one block makes more than the other. The state machine and the
/// stack models count the visits alike.
/// </summary>
[SkipSnapshotConsistency("Verified by WarpStackModelTests on every path, under both CPU models")]
public static partial class WarpStackModelKernels
{
    // A shuffle of the two lanes of the mask, reached only when take holds
    [GpuDeviceFunction]
    public static int MaybeShuffle(int v, bool take)
    {
        if (take)
        {
            v += Warp.ShuffleXorSync(0x3u, v, 1);
        }

        return v;
    }

    // Lane 0 reaches the shuffle in the first call, lane 1 in the second call of the same call site
    [GpuKernel]
    public static void SkewedCallsKernel(int[] input, int[] output)
    {
        int t = ThreadIndex.X;
        int v = input[t];
        for (int k = 0; k < 2; k++)
        {
            v = MaybeShuffle(v, (t == 0) == (k == 0));
        }

        output[t] = v;
    }

    // The same skew in the two copies of a Gpu.ForUnroll body, which share the site of the one shuffle
    [GpuKernel]
    public static void ForUnrollSkewedShuffleKernel(int[] input, int[] output)
    {
        int t = ThreadIndex.X;
        int v = input[t];
        Gpu.ForUnroll(0, 2, k =>
        {
            if ((t == 0) == (k == 0))
            {
                v += Warp.ShuffleXorSync(0x3u, v, 1);
            }
        });
        output[t] = v;
    }

    // Both lanes of each block read each other, then lane 0 of block 0 alone reads itself at the same site once more,
    // so that block 0 ends with counts that differ between its lanes; block 1 starts from counts of its own
    [GpuKernel]
    public static void StaleVisitsKernel(int[] input, int[] output)
    {
        int t = ThreadIndex.X;
        int i = BlockIndex.X * BlockDimension.X + t;
        int visits = BlockIndex.X == 0 && t == 0 ? 2 : 1;
        int v = input[i];
        for (int k = 0; k < visits; k++)
        {
            uint mask = k == 0 ? 0x3u : 0x1u;
            int source = k == 0 ? t ^ 1 : t;
            v = Warp.ShuffleSync(mask, v, source) + 1;
        }

        output[i] = v;
    }
}
