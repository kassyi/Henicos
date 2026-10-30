using System;
using Kassyi.GSharp.Core;

public static partial class TestKernelContainer
{
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void ExplicitMathKernel(float[] data)
    {
        data[ThreadIndex.X] = (float)System.Math.Cbrt(data[ThreadIndex.X]);
    }
}
