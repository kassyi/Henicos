using System;
using static System.Math;
using Kassyi.GSharp.Core;

public static partial class TestKernelContainer
{
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void UsingStaticKernel(float[] data)
    {
        data[ThreadIndex.X] = (float)Cbrt(data[ThreadIndex.X]);
    }
}
