#nullable enable
using System;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public static partial class OptionalArrayKernels
{
    [GpuKernel]
    public static void IsNullBiasKernel(float[] x, float[]? bias)
    {
        int i = ThreadIndex.X;
        x[i] = bias is null ? x[i] : x[i] + bias[i];
    }

    [GpuKernel]
    public static void EqualsNullBiasKernel(float[] x, float[]? bias)
    {
        int i = ThreadIndex.X;
        x[i] = bias == null ? -1f : (null != bias ? bias[i] : -2f);
    }

    [GpuKernel]
    public static void LengthOptionalKernel(int[] o, float[]? bias)
    {
        if (ThreadIndex.X == 0)
        {
#pragma warning disable CS8602 // The device reads a null array as one of length 0
            o[0] = bias.Length;
#pragma warning restore CS8602
        }
    }

    [GpuKernel]
    public static void OutOptionalKernel([Out] float[]? bias, int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            o[0] = bias is null ? 0 : 1;
            if (bias is not null)
            {
                bias[0] = 5f;
            }
        }
    }

    [GpuKernel]
    public static void InOutOptionalKernel([In, Out] float[]? bias, int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            o[0] = bias is null ? 0 : (int)bias[0];
            if (bias is not null)
            {
                bias[0] = bias[0] + 1f;
            }
        }
    }

    [GpuKernel]
    public static void MemoryAddKernel(Memory<float> x, ReadOnlyMemory<float> y)
    {
        int i = ThreadIndex.X;
        x.Span[i] = x.Span[i] + y.Span[i];
    }

    [GpuKernel]
    public static void SegmentScaleKernel(ArraySegment<float> segment)
    {
        int i = ThreadIndex.X;
        segment[i] = segment[i] * 2f + segment.Count;
    }

    [GpuKernel]
    public static void ImmutableAddKernel(float[] x, ImmutableArray<float> y)
    {
        int i = ThreadIndex.X;
        x[i] = x[i] + y[i];
    }

    [GpuKernel]
    public static void OptionalMemoryKernel(float[] x, Memory<float>? bias)
    {
        int i = ThreadIndex.X;
        if (bias.HasValue)
        {
            x[i] = x[i] + bias.Value.Span[i];
        }
    }

    [GpuKernel]
    public static void OptionalSegmentKernel(float[] x, ArraySegment<float>? bias)
    {
        int i = ThreadIndex.X;
        if (bias is not null)
        {
            x[i] = x[i] + bias.Value[i];
        }
    }

    [GpuKernel]
    public static float ScaleKernel(float[] values)
    {
        return values[ThreadIndex.X] * 2f;
    }
}
