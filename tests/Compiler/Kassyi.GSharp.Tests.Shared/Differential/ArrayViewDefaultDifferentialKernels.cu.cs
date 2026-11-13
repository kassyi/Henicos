#nullable enable
using System;
using System.Collections.Immutable;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for the members of <see cref="ArraySegment{T}"/> and <see cref="ImmutableArray{T}"/> that
/// device code calls on a default value (no array): <c>Slice</c> and <c>CopyTo</c> throw what C# throws
/// (<see cref="InvalidOperationException"/> for a segment, <see cref="NullReferenceException"/> for an immutable
/// array) instead of acting on an empty view, and <c>CopyTo</c> of a null destination array throws
/// <see cref="ArgumentNullException"/> before it compares the lengths. Also an <c>ImmutableArray&lt;T&gt;?</c>
/// parameter launched with <see langword="null"/>.
/// </summary>
/// <remarks>
/// Each kernel writes <c>dst[0]</c> before the call and <c>dst[1]</c> after it, so a thread that stops at the call
/// leaves <c>dst[1]</c> at 0. <c>mode[0]</c> picks the receiver: 0 the argument, 1 the default value, 2 an empty slice
/// of the argument.
/// </remarks>
public static partial class ArrayViewDefaultDifferentialKernels
{
    public const int ReceiverArgument = 0;
    public const int ReceiverDefault = 1;
    public const int ReceiverEmpty = 2;

    /// <summary><c>mode[1]</c>: 0 calls <c>Slice(1, 1)</c>, 1 calls <c>Slice(1)</c>.</summary>
    [GpuKernel]
    public static void SegmentSliceKernel(ArraySegment<int> segment, int[] mode, int[] dst)
    {
        ArraySegment<int> s = default;
        if (mode[0] == ReceiverArgument)
        {
            s = segment;
        }

        dst[0] = 1;
        ArraySegment<int> t = mode[1] == 0 ? s.Slice(1, 1) : s.Slice(1);
        // The native path runs on after the failed Slice (#1714 (the native CPU path keeps running a thread after a
        // failed C# check)) and does not check the index yet (#718 (array and span bounds are not checked as in C#)),
        // so t[0] of the empty view it returns would read address 0 and end the test host
        dst[1] = 100 + t.Count * 10 + (t.Count > 0 ? t[0] : 0);
    }

    [GpuKernel]
    public static void SegmentCopyToKernel(ArraySegment<int> segment, int[] mode, int[]? target, int[] dst)
    {
        ArraySegment<int> s = default;
        if (mode[0] == ReceiverArgument)
        {
            s = segment;
        }
        else if (mode[0] == ReceiverEmpty)
        {
            s = segment.Slice(0, 0);
        }

        dst[0] = 1;
#pragma warning disable CS8604 // A null destination is the case under test: C# throws ArgumentNullException
        s.CopyTo(target);
#pragma warning restore CS8604
        dst[1] = 100 + s.Count;
    }

    /// <summary><c>mode[1]</c>: 0 copies to the destination argument, 1 to a default segment.</summary>
    [GpuKernel]
    public static void SegmentCopyToSegmentKernel(ArraySegment<int> segment, ArraySegment<int> destination,
        int[] mode, int[] dst)
    {
        ArraySegment<int> s = default;
        if (mode[0] == ReceiverArgument)
        {
            s = segment;
        }

        ArraySegment<int> d = default;
        if (mode[1] == 0)
        {
            d = destination;
        }

        dst[0] = 1;
        s.CopyTo(d);
        dst[1] = 100 + s.Count;
    }

    [GpuKernel]
    public static void ImmutableCopyToKernel(ImmutableArray<int> immutable, int[] mode, int[]? target, int[] dst)
    {
        ImmutableArray<int> s = default;
        if (mode[0] == ReceiverArgument)
        {
            s = immutable;
        }

        dst[0] = 1;
#pragma warning disable CS8604 // A null destination is the case under test: C# throws ArgumentNullException
        s.CopyTo(target);
#pragma warning restore CS8604
        dst[1] = 100;
    }

    [GpuKernel]
    public static void NullableImmutableKernel(ImmutableArray<int>? s, int[] dst)
    {
        dst[0] = 1;
        // s.Value.Length reads the length of the view, not a member function of a user struct
        dst[1] = s.HasValue ? 10 + s.Value.Length : 2;
    }

    [GpuKernel]
    public static void NullableImmutableHasValueKernel(ImmutableArray<int>? s, int[] dst)
    {
        dst[0] = 1;
        dst[1] = s.HasValue ? 10 : 2;
    }
}
