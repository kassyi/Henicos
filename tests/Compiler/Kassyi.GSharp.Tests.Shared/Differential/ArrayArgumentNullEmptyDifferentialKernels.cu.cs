#nullable enable
using System;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Kernels that read an array-like argument that may be null, empty or populated, one read per <c>mode</c>
/// (<see cref="ArrayArgumentRead"/>): a launch passes the argument as C# does, so every path reads what the kernel's
/// own C# reads.
/// </summary>
#pragma warning disable CS8602, CS8604 // C#: NullReferenceException on a null array, which the tests check
public static partial class ArrayArgumentNullEmptyDifferentialKernels
{
    [GpuKernel]
    public static void NullableArrayKernel(int[]? a, ArrayArgumentRead mode, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        switch (mode)
        {
            case ArrayArgumentRead.IsNull:
                o[0] = (a is null ? 1 : 0) + (a == null ? 2 : 0);
                break;
            case ArrayArgumentRead.Length:
                o[0] = a.Length;
                break;
            case ArrayArgumentRead.Element:
                o[0] = a[0];
                break;
            case ArrayArgumentRead.ForEach:
                o[0] = 100;
                foreach (var v in a)
                {
                    o[0] += v + 1;
                }

                break;
            case ArrayArgumentRead.AsSpanLength:
                o[0] = a.AsSpan().Length;
                break;
            case ArrayArgumentRead.SpanLength:
                o[0] = new Span<int>(a).Length;
                break;
            case ArrayArgumentRead.ToNonNullable:
                o[0] = NonNullableLength(a);
                break;
            case ArrayArgumentRead.InNullableFunction:
                o[0] = NullableLength(a);
                break;
            case ArrayArgumentRead.StackLinqSum:
                o[0] = a.AsStackEnumerable().Sum();
                break;
            case ArrayArgumentRead.StackLinqStoredSum:
            {
                var sequence = a.AsStackEnumerable();
                o[0] = sequence.Sum();
                break;
            }
            case ArrayArgumentRead.StackLinqIndexedSum:
                o[0] = a.AsStackEnumerable().Where((v, i) => i >= 0).Sum();
                break;
            case ArrayArgumentRead.StackLinqInDeviceFunction:
                o[0] = NullableStackLinqSum(a);
                break;
            case ArrayArgumentRead.StackLinqStaticSum:
                o[0] = StackLinqExtensions.AsStackEnumerable(a).Sum();
                break;
        }
    }

    [GpuKernel]
    public static void NonNullableArrayKernel(int[] a, ArrayArgumentRead mode, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        switch (mode)
        {
            case ArrayArgumentRead.IsNull:
                o[0] = (a is null ? 1 : 0) + (a == null ? 2 : 0);
                break;
            case ArrayArgumentRead.Length:
                o[0] = a.Length;
                break;
            case ArrayArgumentRead.Element:
                o[0] = a[0];
                break;
            case ArrayArgumentRead.ForEach:
                o[0] = 100;
                foreach (var v in a)
                {
                    o[0] += v + 1;
                }

                break;
            case ArrayArgumentRead.AsSpanLength:
                o[0] = a.AsSpan().Length;
                break;
            case ArrayArgumentRead.SpanLength:
                o[0] = new Span<int>(a).Length;
                break;
            case ArrayArgumentRead.ToNonNullable:
                o[0] = NonNullableLength(a);
                break;
            case ArrayArgumentRead.InNullableFunction:
                o[0] = NullableLength(a);
                break;
        }
    }

#nullable disable
    [GpuKernel]
    public static void ObliviousArrayKernel(int[] a, ArrayArgumentRead mode, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        switch (mode)
        {
            case ArrayArgumentRead.IsNull:
                o[0] = (a is null ? 1 : 0) + (a == null ? 2 : 0);
                break;
            case ArrayArgumentRead.Length:
                o[0] = a.Length;
                break;
            case ArrayArgumentRead.Element:
                o[0] = a[0];
                break;
            case ArrayArgumentRead.ForEach:
                o[0] = 100;
                foreach (var v in a)
                {
                    o[0] += v + 1;
                }

                break;
            case ArrayArgumentRead.AsSpanLength:
                o[0] = a.AsSpan().Length;
                break;
            case ArrayArgumentRead.SpanLength:
                o[0] = new Span<int>(a).Length;
                break;
            case ArrayArgumentRead.ToNonNullable:
                o[0] = NonNullableLength(a);
                break;
            case ArrayArgumentRead.InNullableFunction:
                o[0] = NullableLength(a);
                break;
        }
    }
#nullable enable

    // The elements differ in layout on the device, so the launchers convert them
    [GpuKernel]
    public static void ConvertedArrayKernel([In] DiffDeepBag[]? a, ArrayArgumentRead mode, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        switch (mode)
        {
            case ArrayArgumentRead.IsNull:
                o[0] = (a is null ? 1 : 0) + (a == null ? 2 : 0);
                break;
            case ArrayArgumentRead.Length:
                o[0] = a.Length;
                break;
            case ArrayArgumentRead.ForEach:
                o[0] = 100;
                foreach (var v in a)
                {
                    o[0] += v.Tag + 1;
                }

                break;
            case ArrayArgumentRead.AsSpanLength:
                o[0] = a.AsSpan().Length;
                break;
        }
    }

    // The reads after a barrier run in the state machine of the CPU paths
    [GpuKernel]
    public static void NullableArrayAfterBarrierKernel(int[]? a, ArrayArgumentRead mode, int[] o)
    {
        Gpu.SyncThreads();
        if (ThreadIndex.X != 0)
        {
            return;
        }

        switch (mode)
        {
            case ArrayArgumentRead.IsNull:
                o[0] = a is null ? 1 : 0;
                break;
            case ArrayArgumentRead.Length:
                o[0] = a.Length;
                break;
            case ArrayArgumentRead.InNullableFunction:
                o[0] = NullableLength(a);
                break;
        }
    }

    // A barrier in the body lowers the foreach into an indexed loop, whose source the CPU paths keep in the frame of
    // the state machine; a null array throws at the loop entry as in the plain loop
    [GpuKernel]
    public static void NullableArrayForEachBarrierKernel(int[]? a, int[] o)
    {
        o[0] = 100;
        foreach (var v in a)
        {
            Gpu.SyncThreads();
            o[0] += v + 1;
        }
    }

    [GpuDeviceFunction]
    public static int NonNullableLength(int[] x) => x.Length;

    [GpuDeviceFunction]
    public static int NullableLength(int[]? x) => x.Length;

    // A device function has no error site table: its check must still give ArgumentNullException its ParamName
    [GpuDeviceFunction]
    public static int NullableStackLinqSum(int[]? x) => x.AsStackEnumerable().Sum();
}
#pragma warning restore CS8602, CS8604

/// <summary>
/// The read of the argument that a kernel of <see cref="ArrayArgumentNullEmptyDifferentialKernels"/> makes.
/// </summary>
public enum ArrayArgumentRead
{
    /// <summary>
    /// <c>a is null</c> and <c>a == null</c>, or what tells a default span-like value from one over an array.
    /// </summary>
    IsNull,

    /// <summary><c>Length</c> (<c>Count</c> of a segment).</summary>
    Length,

    /// <summary><c>a[0]</c>.</summary>
    Element,

    /// <summary><c>foreach</c>.</summary>
    ForEach,

    /// <summary><c>a.AsSpan().Length</c>.</summary>
    AsSpanLength,

    /// <summary><c>new Span&lt;int&gt;(a).Length</c>.</summary>
    SpanLength,

    /// <summary>The array passed to a device function whose parameter is not nullable.</summary>
    ToNonNullable,

    /// <summary>The array read in a device function whose parameter is nullable.</summary>
    InNullableFunction,

    /// <summary><c>IsEmpty</c> of a <c>Memory&lt;T&gt;</c> or an <c>ImmutableArray&lt;T&gt;</c>.</summary>
    IsEmpty,

    /// <summary><c>IsDefault</c> of an <c>ImmutableArray&lt;T&gt;</c>.</summary>
    IsDefault,

    /// <summary><c>IsDefaultOrEmpty</c> of an <c>ImmutableArray&lt;T&gt;</c>.</summary>
    IsDefaultOrEmpty,

    /// <summary><c>a.AsStackEnumerable().Sum()</c>: StackLinq over an array.</summary>
    StackLinqSum,

    /// <summary>The same query from a sequence stored in a local, which is not fused into one loop.</summary>
    StackLinqStoredSum,

    /// <summary>
    /// <c>a.AsStackEnumerable().Where((v, i) =&gt; i &gt;= 0).Sum()</c>: an indexed lambda, not fused.
    /// </summary>
    StackLinqIndexedSum,

    /// <summary>The same query in a device function, which has no error site table.</summary>
    StackLinqInDeviceFunction,

    /// <summary><c>StackLinqExtensions.AsStackEnumerable(a).Sum()</c>: the static form of the call.</summary>
    StackLinqStaticSum,

    /// <summary>
    /// <c>IsDefault</c> and <c>IsDefaultOrEmpty</c> of a <c>default(ImmutableArray&lt;T&gt;)</c> literal.
    /// </summary>
    DefaultLiteral,
}
