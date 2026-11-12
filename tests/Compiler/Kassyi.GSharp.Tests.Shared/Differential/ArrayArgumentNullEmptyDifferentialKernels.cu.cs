#nullable enable
using System;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Kernels that read an array-like argument that may be null, empty or populated, one read per <c>mode</c>
/// (<see cref="ArrayArgumentReads"/>): a launch passes the argument as C# does, so every path reads what the kernel's
/// own C# reads.
/// </summary>
#pragma warning disable CS8602, CS8604 // C#: NullReferenceException on a null array, which the tests check
public static partial class ArrayArgumentNullEmptyDifferentialKernels
{
    [GpuKernel]
    public static void NullableArrayKernel(int[]? a, int mode, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        switch (mode)
        {
            case ArrayArgumentReads.IsNull:
                o[0] = (a is null ? 1 : 0) + (a == null ? 2 : 0);
                break;
            case ArrayArgumentReads.Length:
                o[0] = a.Length;
                break;
            case ArrayArgumentReads.Element:
                o[0] = a[0];
                break;
            case ArrayArgumentReads.ForEach:
                o[0] = 100;
                foreach (var v in a)
                {
                    o[0] += v + 1;
                }

                break;
            case ArrayArgumentReads.AsSpanLength:
                o[0] = a.AsSpan().Length;
                break;
            case ArrayArgumentReads.SpanLength:
                o[0] = new Span<int>(a).Length;
                break;
            case ArrayArgumentReads.ToNonNullable:
                o[0] = NonNullableLength(a);
                break;
            case ArrayArgumentReads.InNullableFunction:
                o[0] = NullableLength(a);
                break;
        }
    }

    [GpuKernel]
    public static void NonNullableArrayKernel(int[] a, int mode, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        switch (mode)
        {
            case ArrayArgumentReads.IsNull:
                o[0] = (a is null ? 1 : 0) + (a == null ? 2 : 0);
                break;
            case ArrayArgumentReads.Length:
                o[0] = a.Length;
                break;
            case ArrayArgumentReads.Element:
                o[0] = a[0];
                break;
            case ArrayArgumentReads.ForEach:
                o[0] = 100;
                foreach (var v in a)
                {
                    o[0] += v + 1;
                }

                break;
            case ArrayArgumentReads.AsSpanLength:
                o[0] = a.AsSpan().Length;
                break;
            case ArrayArgumentReads.SpanLength:
                o[0] = new Span<int>(a).Length;
                break;
            case ArrayArgumentReads.ToNonNullable:
                o[0] = NonNullableLength(a);
                break;
            case ArrayArgumentReads.InNullableFunction:
                o[0] = NullableLength(a);
                break;
        }
    }

#nullable disable
    [GpuKernel]
    public static void ObliviousArrayKernel(int[] a, int mode, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        switch (mode)
        {
            case ArrayArgumentReads.IsNull:
                o[0] = (a is null ? 1 : 0) + (a == null ? 2 : 0);
                break;
            case ArrayArgumentReads.Length:
                o[0] = a.Length;
                break;
            case ArrayArgumentReads.Element:
                o[0] = a[0];
                break;
            case ArrayArgumentReads.ForEach:
                o[0] = 100;
                foreach (var v in a)
                {
                    o[0] += v + 1;
                }

                break;
            case ArrayArgumentReads.AsSpanLength:
                o[0] = a.AsSpan().Length;
                break;
            case ArrayArgumentReads.SpanLength:
                o[0] = new Span<int>(a).Length;
                break;
            case ArrayArgumentReads.ToNonNullable:
                o[0] = NonNullableLength(a);
                break;
            case ArrayArgumentReads.InNullableFunction:
                o[0] = NullableLength(a);
                break;
        }
    }
#nullable enable

    // The elements differ in layout on the device, so the launchers convert them
    [GpuKernel]
    public static void ConvertedArrayKernel([In] DiffDeepBag[]? a, int mode, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        switch (mode)
        {
            case ArrayArgumentReads.IsNull:
                o[0] = (a is null ? 1 : 0) + (a == null ? 2 : 0);
                break;
            case ArrayArgumentReads.Length:
                o[0] = a.Length;
                break;
            case ArrayArgumentReads.ForEach:
                o[0] = 100;
                foreach (var v in a)
                {
                    o[0] += v.Tag + 1;
                }

                break;
            case ArrayArgumentReads.AsSpanLength:
                o[0] = a.AsSpan().Length;
                break;
        }
    }

    // The reads after a barrier run in the state machine of the CPU paths
    [GpuKernel]
    public static void NullableArrayAfterBarrierKernel(int[]? a, int mode, int[] o)
    {
        Gpu.SyncThreads();
        if (ThreadIndex.X != 0)
        {
            return;
        }

        switch (mode)
        {
            case ArrayArgumentReads.IsNull:
                o[0] = a is null ? 1 : 0;
                break;
            case ArrayArgumentReads.Length:
                o[0] = a.Length;
                break;
            case ArrayArgumentReads.InNullableFunction:
                o[0] = NullableLength(a);
                break;
        }
    }

    [GpuDeviceFunction]
    public static int NonNullableLength(int[] x) => x.Length;

    [GpuDeviceFunction]
    public static int NullableLength(int[]? x) => x.Length;
}
#pragma warning restore CS8602, CS8604

/// <summary>The read of the argument that a kernel of <see cref="ArrayArgumentNullEmptyDifferentialKernels"/> makes.</summary>
public static class ArrayArgumentReads
{
    /// <summary><c>a is null</c> and <c>a == null</c>, or what tells a default span-like value from one over an array.</summary>
    public const int IsNull = 0;

    /// <summary><c>Length</c> (<c>Count</c> of a segment).</summary>
    public const int Length = 1;

    /// <summary><c>a[0]</c>.</summary>
    public const int Element = 2;

    /// <summary><c>foreach</c>.</summary>
    public const int ForEach = 3;

    /// <summary><c>a.AsSpan().Length</c>.</summary>
    public const int AsSpanLength = 4;

    /// <summary><c>new Span&lt;int&gt;(a).Length</c>.</summary>
    public const int SpanLength = 5;

    /// <summary>The array passed to a device function whose parameter is not nullable.</summary>
    public const int ToNonNullable = 6;

    /// <summary>The array read in a device function whose parameter is nullable.</summary>
    public const int InNullableFunction = 7;
}
