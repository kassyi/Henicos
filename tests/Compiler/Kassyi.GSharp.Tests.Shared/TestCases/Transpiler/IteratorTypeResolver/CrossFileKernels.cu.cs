using Kassyi.GSharp.Core;

[SkipSnapshotConsistency("Needs its iterator from another file")]
public static partial class CrossFileKernels
{
    [GpuKernel]
    public static void SumKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        int sum = 0;
        foreach (var v in IterLib.Outer(tid))
        {
            sum += v;
        }
        output[tid] = sum;
    }
}
