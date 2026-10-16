using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Provides shared balanced-tree reduction and enumerator utilities for zero-allocation stack-based LINQ across GPU BigNum types.
/// </summary>
internal static class StackLinqReductionCore
{
    public static int CountElements<TEnumerator, TElement>(TEnumerator enumerator)
        where TEnumerator : struct, IStackEnumerator<TElement>, allows ref struct
        where TElement : struct
    {
        var count = 0;
        while (enumerator.TryGetNext(out _))
        {
            count++;
        }

        return count;
    }

    public static TExpr ReduceBalancedTree<TEnumerator, TSource, TExpr>(
        TEnumerator enumerator,
        Func<TSource, TExpr> converter,
        Func<TExpr, TExpr, TExpr> combiner)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
        where TSource : struct
        where TExpr : struct
    {
        if (!enumerator.TryGetNext(out var first))
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }

        if (!enumerator.TryGetNext(out var second))
        {
            return converter(first);
        }

        var pool = ArrayPool<TExpr>.Shared;
        var capacity = 16;
        var buffer = pool.Rent(capacity);
        var count = 0;

        try
        {
            buffer[count++] = converter(first);
            buffer[count++] = converter(second);

            while (enumerator.TryGetNext(out var value))
            {
                if (count == capacity)
                {
                    var newCapacity = capacity * 2;
                    var newBuffer = pool.Rent(newCapacity);
                    Array.Copy(buffer, newBuffer, count);
                    pool.Return(buffer, clearArray: true);
                    buffer = newBuffer;
                    capacity = newCapacity;
                }

                buffer[count++] = converter(value);
            }

            return BuildBalancedTree(buffer.AsSpan(0, count), combiner);
        }
        finally
        {
            pool.Return(buffer, clearArray: true);
        }
    }

    public static TExpr BuildBalancedTree<TExpr>(
        ReadOnlySpan<TExpr> span,
        Func<TExpr, TExpr, TExpr> combiner)
        where TExpr : struct
    {
        switch (span.Length)
        {
            case 1:
                return span[0];
            case 2:
                return combiner(span[0], span[1]);
        }

        var mid = span.Length / 2;
        var left = BuildBalancedTree(span.Slice(0, mid), combiner);
        var right = BuildBalancedTree(span.Slice(mid), combiner);
        return combiner(left, right);
    }
}
