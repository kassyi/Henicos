using Kassyi.GSharp.Core;
[SkipSnapshotConsistency("Generator case")]
public static partial class TopLevel_Kernels
{
    [GpuKernel]
    public static void Run(int[] data) { data[ThreadIndex.X] = 1; }
}
