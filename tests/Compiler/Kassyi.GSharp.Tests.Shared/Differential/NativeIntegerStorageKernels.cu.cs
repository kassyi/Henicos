#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>A struct with native integer fields.</summary>
public struct NativeIntegerPair
{
    public byte Tag;
    public nint Signed;
    public nuint Unsigned;
}

/// <summary>
/// Kernels of <c>nint</c> and <c>nuint</c> as a constant, a struct field, an element of a span and an index.
/// </summary>
public static partial class NativeIntegerStorageKernels
{
    private const nint Negative = -7;

    private const nuint Wide = 0xFFFF_FFFF;

    [GpuKernel]
    public static void NativeIntegerNegativeConstantKernel(long[] a, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        dst[t] = a[t] * Negative;
    }

    [GpuKernel]
    public static void NativeIntegerWideConstantKernel(long[] a, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        dst[t] = (long)((nuint)a[t] * Wide);
    }

    [GpuKernel]
    public static void NativeIntegerStructFieldKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        var pair = new NativeIntegerPair { Tag = (byte)t, Signed = (nint)a[t], Unsigned = (nuint)b[t] };
        pair.Signed += 3;
        pair.Unsigned -= 1;
        dst[t * 2] = pair.Signed;
        dst[t * 2 + 1] = (long)pair.Unsigned + pair.Tag;
    }

    [GpuKernel]
    public static void NativeIntegerSpanElementKernel(long[] a, long[] b, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        Span<nint> s = [(nint)a[t], (nint)b[t], 0];
        s[2] = s[0] * 2 + s[1];
        s[0] -= s[1];
        dst[t * 2] = s[0];
        dst[t * 2 + 1] = s[2];
    }

    [GpuKernel]
    public static void NativeIntegerIndexKernel(long[] a, long[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        nint i = (nint)t;
        nuint j = (nuint)(n - 1 - t);
        dst[i] = a[j];
    }
}
