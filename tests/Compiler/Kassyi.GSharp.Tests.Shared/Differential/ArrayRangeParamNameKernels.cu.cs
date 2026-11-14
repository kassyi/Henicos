#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Kernels whose <c>System.Array</c> and span calls fail a range check that C# words as an argument exception with a
/// parameter name: <c>Array.Fill</c> with a start or a count outside the array, <c>Array.Copy</c> with a source or a
/// destination that is too short, <c>Span.CopyTo</c> into a shorter span. <c>a</c> has 3 elements and <c>s</c> has 1.
/// The calls are an <c>if</c> chain, not a <c>switch</c>. <c>o[0]</c> is written before the call and <c>o[1]</c> after it.
/// </summary>
public static partial class ArrayRangeParamNameKernels
{
    /// <summary>The length of the longer array a launch passes.</summary>
    public const int LongLength = 3;

    [GpuKernel]
    public static void ArrayRangeCallKernel(int[] a, int[] s, int call, int[] o)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        o[0] = 1;
        if (call == RangeCalls.FillStartPastEnd)
        {
            Array.Fill(a, 7, 5, 0);
        }
        else if (call == RangeCalls.FillStartNegative)
        {
            Array.Fill(a, 7, -1, 1);
        }
        else if (call == RangeCalls.FillCountPastEnd)
        {
            Array.Fill(a, 7, 1, 5);
        }
        else if (call == RangeCalls.FillCountNegative)
        {
            Array.Fill(a, 7, 0, -1);
        }
        else if (call == RangeCalls.CopySourceTooShort)
        {
            Array.Copy(s, a, 3);
        }
        else if (call == RangeCalls.CopyDestinationTooShort)
        {
            Array.Copy(a, s, 3);
        }
        else if (call == RangeCalls.CopyRangeSourceTooShort)
        {
            Array.Copy(s, 0, a, 0, 3);
        }
        else if (call == RangeCalls.CopyRangeDestinationTooShort)
        {
            Array.Copy(a, 0, s, 0, 3);
        }
        else if (call == RangeCalls.SpanCopyToShorter)
        {
            a.AsSpan().CopyTo(s.AsSpan());
        }
        else if (call == RangeCalls.SpanSlicePastEnd)
        {
            o[1] = a.AsSpan().Slice(5).Length;
        }

        o[1] = 2;
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

    /// <summary>The number of calls.</summary>
    public const int Count = 10;
}
