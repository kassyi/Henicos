#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Emission;

[SkipSnapshotConsistency("Pointer launcher shapes checked line by line by EmissionShapeTests")]
public static partial class PointerLaunchShapeKernels
{
    public struct Pair
    {
        public int First;
        public int Second;
    }

    // Each kind of parameter of the pointer launcher: a non-optional array, an optional array, a span, a ref scalar, a
    // by-value struct and a scalar
    [GpuKernel]
    public static void EveryKindKernel(int[] values, float[]? extra, Span<uint> words, ref int total, Pair pair, int count)
    {
        int i = ThreadIndex.X;
        if (i < count && i < values.Length && i < words.Length)
        {
            values[i] += pair.First + (int)words[i];
        }

        if (i == 0)
        {
            total = values.Length + (extra is null ? 0 : extra.Length) + pair.Second;
        }
    }

    // Overloads: the public pointer launcher takes GpuBuffer<T> and forwards each array with its length
    [GpuKernel]
    public static void FillKernel(int[] output, ref int marker, int[]? optional = null)
    {
        output[ThreadIndex.X] = optional is null ? 0 : optional.Length;
        marker = 1;
    }

    [GpuKernel]
    public static void FillKernel(float[] output, ref int marker, float[]? optional = null)
    {
        output[ThreadIndex.X] = optional is null ? 0 : optional.Length;
        marker = 1;
    }
}
