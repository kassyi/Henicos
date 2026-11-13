#nullable enable
using System;
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;
using S = Kassyi.ZeroAlloc.Linq.StackLinqExtensions;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Kernels that pass a zero-length stack span, which the transpiler declares as a span value and not as a C++ array, to
/// StackLinq calls that are not fused into one loop.
/// </summary>
public static partial class StackLinqEmptySpanKernels
{
    // The static form: the span is the argument of AsStackEnumerable
    [GpuKernel]
    public static void StaticFormKernel(int seed, int[] results)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        Span<int> empty = stackalloc int[0];
        var skipped = S.Skip(S.AsStackEnumerable(empty), 1);
        results[0] = S.Count(skipped, x => x > seed) + 7;
    }

    // The reduced form, stored in a local before its terminal
    [GpuKernel]
    public static void ReducedFormKernel(int seed, int[] results)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        Span<int> empty = stackalloc int[0];
        var skipped = empty.AsStackEnumerable().Skip(1);
        results[0] = skipped.Count(x => x > seed) + 7;
    }

    // A non-empty stack span still goes through the C++ array view
    [GpuKernel]
    public static void NonEmptyStaticFormKernel(int seed, int[] results)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        Span<int> values = stackalloc int[3];
        values[0] = 1;
        values[1] = 5;
        values[2] = 9;
        var skipped = S.Skip(S.AsStackEnumerable(values), 1);
        results[0] = S.Count(skipped, x => x > seed) + 7;
    }
}
