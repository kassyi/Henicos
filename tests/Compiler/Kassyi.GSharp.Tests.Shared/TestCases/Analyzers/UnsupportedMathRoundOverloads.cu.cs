using System;
using Kassyi.GSharp.Core;

public static partial class TestKernelContainer
{
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void UnsupportedRoundKernel(double[] data, float[] floats)
    {
        int tid = ThreadIndex.X;
        data[tid] = Math.Round(data[tid], 2);
        data[tid] = Math.Round(data[tid], MidpointRounding.AwayFromZero);
        data[tid] = Math.Round(data[tid], 2, MidpointRounding.ToEven);
        floats[tid] = MathF.Round(floats[tid], 1);
        floats[tid] = MathF.Round(floats[tid], MidpointRounding.ToZero);
    }
}
