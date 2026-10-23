using Kassyi.GSharp.Core;

public static partial class WarpShuffleNativeShuffleKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void ShuffleDownKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        uint mask = 0xffffffff;
        int val = input[tid];
        int res = Warp.ShuffleDownSync(mask, val, 16);
        output[tid] = res;
    }
}
