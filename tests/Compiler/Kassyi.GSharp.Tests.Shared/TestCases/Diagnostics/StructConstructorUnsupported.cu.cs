using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// GSH0003: a constructor that is neither emitted as a factory function nor mapped to the device (#499)
public static partial class StructConstructorUnsupportedKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void SpanVectorKernel(float[] data)
    {
        ReadOnlySpan<float> components = data;
        var v = new System.Numerics.Vector3(components);
        data[0] = v.X;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void MetadataStructKernel(int[] data)
    {
        var context = new GpuThreadContext(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);
        data[0] = context.ThreadIdxX;
    }
}
