#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Kernels whose <c>System.Array</c> and span calls fail a range check that C# words as an argument exception with a
/// parameter name: <c>Array.Fill</c> with a start or a count outside the array, <c>Array.Copy</c> with a negative
/// argument or a source or a destination that is too short, <c>Span.CopyTo</c> into a shorter span; and the calls on
/// the edge that must not fail. <c>ArrayRangeCallKernel</c> makes the calls itself (its checks are error sites of the
/// kernel); <c>ArrayRangeDeviceFunctionKernel</c> makes them in a device function (whose checks pass their messages).
/// <c>a</c> has 3 elements and <c>s</c> has 1. <c>o[0]</c> is written before the call and <c>o[1]</c> after it.
/// </summary>
public static partial class ArrayRangeParamNameKernels
{
    [GpuKernel]
    public static void ArrayRangeCallKernel(int[] a, int[] s, int call, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        o[0] = 1;
        switch (call)
        {
            case RangeCalls.FillStartPastEnd:
                Array.Fill(a, 7, 5, 0);
                break;
            case RangeCalls.FillStartNegative:
                Array.Fill(a, 7, -1, 1);
                break;
            case RangeCalls.FillCountPastEnd:
                Array.Fill(a, 7, 1, 5);
                break;
            case RangeCalls.FillCountNegative:
                Array.Fill(a, 7, 0, -1);
                break;
            case RangeCalls.CopySourceTooShort:
                Array.Copy(s, a, 3);
                break;
            case RangeCalls.CopyDestinationTooShort:
                Array.Copy(a, s, 3);
                break;
            case RangeCalls.CopyRangeSourceTooShort:
                Array.Copy(s, 0, a, 0, 3);
                break;
            case RangeCalls.CopyRangeDestinationTooShort:
                Array.Copy(a, 0, s, 0, 3);
                break;
            case RangeCalls.SpanCopyToShorter:
                a.AsSpan().CopyTo(s.AsSpan());
                break;
            case RangeCalls.SpanSlicePastEnd:
                o[1] = a.AsSpan().Slice(5).Length;
                break;
            case RangeCalls.CopyNegativeLength:
                Array.Copy(a, s, -1);
                break;
            case RangeCalls.CopyNegativeSourceIndex:
                Array.Copy(a, -1, s, 0, 1);
                break;
            case RangeCalls.CopyNegativeDestinationIndex:
                Array.Copy(a, 0, s, -1, 1);
                break;
            case RangeCalls.CopySourceTooShortAndDestinationIndexNegative:
                Array.Copy(s, 0, a, -1, 3);
                break;
            case RangeCalls.CopyBothIndexesNegative:
                Array.Copy(a, -1, s, -1, 1);
                break;
            case RangeCalls.CopyNegativeLengthAndIndex:
                Array.Copy(a, -1, s, 0, -1);
                break;
            case RangeCalls.FillStartAtEnd:
                Array.Fill(a, 7, 3, 0);
                break;
            case RangeCalls.CopyEmptyAtEnd:
                Array.Copy(a, 3, s, 0, 0);
                break;
            case RangeCalls.CopyEmptyDestinationAtEnd:
                Array.Copy(a, 0, s, 1, 0);
                break;
            case RangeCalls.SliceStartAtEnd:
                o[1] = a.AsSpan().Slice(3).Length;
                break;
            case RangeCalls.SliceCountToEnd:
                o[1] = a.AsSpan(1, 2).Length;
                break;
        }

        o[1] = 2;
    }

    [GpuKernel]
    public static void ArrayRangeDeviceFunctionKernel(int[] a, int[] s, int call, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        o[0] = 1;
        RangeCall(a, s, call, o);
        o[1] = 2;
    }

    [GpuDeviceFunction]
    public static void RangeCall(int[] a, int[] s, int call, int[] o)
    {
        switch (call)
        {
            case RangeCalls.FillStartPastEnd:
                Array.Fill(a, 7, 5, 0);
                break;
            case RangeCalls.FillStartNegative:
                Array.Fill(a, 7, -1, 1);
                break;
            case RangeCalls.FillCountPastEnd:
                Array.Fill(a, 7, 1, 5);
                break;
            case RangeCalls.FillCountNegative:
                Array.Fill(a, 7, 0, -1);
                break;
            case RangeCalls.CopySourceTooShort:
                Array.Copy(s, a, 3);
                break;
            case RangeCalls.CopyDestinationTooShort:
                Array.Copy(a, s, 3);
                break;
            case RangeCalls.CopyRangeSourceTooShort:
                Array.Copy(s, 0, a, 0, 3);
                break;
            case RangeCalls.CopyRangeDestinationTooShort:
                Array.Copy(a, 0, s, 0, 3);
                break;
            case RangeCalls.SpanCopyToShorter:
                a.AsSpan().CopyTo(s.AsSpan());
                break;
            case RangeCalls.SpanSlicePastEnd:
                o[1] = a.AsSpan().Slice(5).Length;
                break;
            case RangeCalls.CopyNegativeLength:
                Array.Copy(a, s, -1);
                break;
            case RangeCalls.CopyNegativeSourceIndex:
                Array.Copy(a, -1, s, 0, 1);
                break;
            case RangeCalls.CopyNegativeDestinationIndex:
                Array.Copy(a, 0, s, -1, 1);
                break;
            case RangeCalls.CopySourceTooShortAndDestinationIndexNegative:
                Array.Copy(s, 0, a, -1, 3);
                break;
            case RangeCalls.CopyBothIndexesNegative:
                Array.Copy(a, -1, s, -1, 1);
                break;
            case RangeCalls.CopyNegativeLengthAndIndex:
                Array.Copy(a, -1, s, 0, -1);
                break;
            case RangeCalls.FillStartAtEnd:
                Array.Fill(a, 7, 3, 0);
                break;
            case RangeCalls.CopyEmptyAtEnd:
                Array.Copy(a, 3, s, 0, 0);
                break;
            case RangeCalls.CopyEmptyDestinationAtEnd:
                Array.Copy(a, 0, s, 1, 0);
                break;
            case RangeCalls.SliceStartAtEnd:
                o[1] = a.AsSpan().Slice(3).Length;
                break;
            case RangeCalls.SliceCountToEnd:
                o[1] = a.AsSpan(1, 2).Length;
                break;
        }
    }
}

/// <summary>The calls of <see cref="ArrayRangeParamNameKernels.ArrayRangeCallKernel"/>.</summary>
public static class RangeCalls
{
    /// <summary><c>Array.Fill(a, 7, 5, 0)</c>: ArgumentOutOfRangeException "startIndex".</summary>
    public const int FillStartPastEnd = 0;

    /// <summary><c>Array.Fill(a, 7, -1, 1)</c>: ArgumentOutOfRangeException "startIndex".</summary>
    public const int FillStartNegative = 1;

    /// <summary><c>Array.Fill(a, 7, 1, 5)</c>: ArgumentOutOfRangeException "count".</summary>
    public const int FillCountPastEnd = 2;

    /// <summary><c>Array.Fill(a, 7, 0, -1)</c>: ArgumentOutOfRangeException "count".</summary>
    public const int FillCountNegative = 3;

    /// <summary><c>Array.Copy(s, a, 3)</c>: ArgumentException "sourceArray".</summary>
    public const int CopySourceTooShort = 4;

    /// <summary><c>Array.Copy(a, s, 3)</c>: ArgumentException "destinationArray".</summary>
    public const int CopyDestinationTooShort = 5;

    /// <summary><c>Array.Copy(s, 0, a, 0, 3)</c>: ArgumentException "sourceArray".</summary>
    public const int CopyRangeSourceTooShort = 6;

    /// <summary><c>Array.Copy(a, 0, s, 0, 3)</c>: ArgumentException "destinationArray".</summary>
    public const int CopyRangeDestinationTooShort = 7;

    /// <summary><c>a.AsSpan().CopyTo(s.AsSpan())</c>: ArgumentException "destination".</summary>
    public const int SpanCopyToShorter = 8;

    /// <summary><c>a.AsSpan().Slice(5)</c>: ArgumentOutOfRangeException without a parameter name.</summary>
    public const int SpanSlicePastEnd = 9;

    /// <summary><c>Array.Copy(a, s, -1)</c>: ArgumentOutOfRangeException "length".</summary>
    public const int CopyNegativeLength = 10;

    /// <summary><c>Array.Copy(a, -1, s, 0, 1)</c>: ArgumentOutOfRangeException "sourceIndex".</summary>
    public const int CopyNegativeSourceIndex = 11;

    /// <summary><c>Array.Copy(a, 0, s, -1, 1)</c>: ArgumentOutOfRangeException "destinationIndex".</summary>
    public const int CopyNegativeDestinationIndex = 12;

    /// <summary>
    /// <c>Array.Copy(s, 0, a, -1, 3)</c>: the source range is checked before the destination index (CopyImpl of
    /// .NET 10), ArgumentException "sourceArray".
    /// </summary>
    public const int CopySourceTooShortAndDestinationIndexNegative = 13;

    /// <summary><c>Array.Copy(a, -1, s, -1, 1)</c>: ArgumentOutOfRangeException "sourceIndex".</summary>
    public const int CopyBothIndexesNegative = 14;

    /// <summary><c>Array.Copy(a, -1, s, 0, -1)</c>: the length is checked first, "length".</summary>
    public const int CopyNegativeLengthAndIndex = 15;

    /// <summary><c>Array.Fill(a, 7, 3, 0)</c>: no exception: a start equal to the length is inside the array.</summary>
    public const int FillStartAtEnd = 16;

    /// <summary><c>Array.Copy(a, 3, s, 0, 0)</c>: no exception: an empty range at the end of the source.</summary>
    public const int CopyEmptyAtEnd = 17;

    /// <summary><c>Array.Copy(a, 0, s, 1, 0)</c>: no exception: an empty range at the end of the destination.</summary>
    public const int CopyEmptyDestinationAtEnd = 18;

    /// <summary><c>a.AsSpan().Slice(3)</c>: no exception: a start equal to the length.</summary>
    public const int SliceStartAtEnd = 19;

    /// <summary><c>a.AsSpan(1, 2)</c>: no exception: a range that ends at the end.</summary>
    public const int SliceCountToEnd = 20;

    /// <summary>The number of calls.</summary>
    public const int Count = 21;
}
