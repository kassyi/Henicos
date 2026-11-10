// ReSharper disable All
using System;
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for unfused StackLinq queries with lambda stages stored in a local before their terminal:
/// the local holds a StackLinq template on the device, not a definition of the C# enumerator struct (#1086). Each
/// thread reads a 4-element slice of <c>xs</c>.
/// </summary>
public static partial class StackLinqLocalQueryDifferentialKernels
{
    public const int SliceLength = 4;

    public static readonly string[] LocalQueryColumns =
    [
        "Select(capturing).Take(0).Sum + calls",
        "Select(capturing).Take(2).Sum + calls",
        "Where(x>0).Count",
        "Where(x>bound).Sum",
        "Select(x*k).Where(odd).Max",
    ];

    [GpuKernel]
    public static void LocalQueryWithLambdaStagesKernel(int[] xs, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if ((t * SliceLength) + SliceLength > xs.Length)
        {
            return;
        }

        Span<int> slice = xs.AsSpan().Slice(t * SliceLength, SliceLength);
        int o = t * 5;

        int calls = 0;
        var none = slice.AsStackEnumerable()
            .Select(x =>
            {
                calls++;
                return x & 0xFFFF;
            })
            .Take(0);
        r[o + 0] = none.Sum() + (calls * 1000);

        int calls2 = 0;
        var two = slice.AsStackEnumerable()
            .Select(x =>
            {
                calls2++;
                return x & 0xFFFF;
            })
            .Take(2);
        r[o + 1] = two.Sum() + (calls2 * 1000);

        var positive = slice.AsStackEnumerable().Where(x => x > 0);
        r[o + 2] = positive.Count();

        int bound = xs[t * SliceLength] >> 1;
        var above = slice.AsStackEnumerable().Where(x => x > bound);
        r[o + 3] = above.Select(x => x & 0xFF).Sum();

        int k = (t & 3) + 1;
        var odd = slice.AsStackEnumerable().Select(x => (x & 0xFFF) * k).Where(x => (x & 1) == 1);
        r[o + 4] = odd.Any() ? odd.Max() : -1;
    }
}
