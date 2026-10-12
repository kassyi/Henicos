using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

#pragma warning disable GSH0005 // GpuBigIntExpr Direct Assignment

/// <summary>
/// Provides extension methods for zero-allocation stack-based LINQ operations on GPU BigNum types.
/// </summary>
public static class GpuBigIntStackLinqExtensions
{
    /// <summary>
    /// Projects each element of a parallel sequence into a new deferred GPU BigInt expression.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">The sequence of elements to invoke a transform function on.</param>
    /// <param name="selector">A transform function to apply to each source element.</param>
    /// <returns>A new parallel sequence containing the deferred expressions.</returns>
    public static ParallelStackEnumerable<SelectEnumerator<TEnumerator, GpuBigInt, GpuBigIntExpr>, GpuBigIntExpr>
        Select<TEnumerator>(
            this ParallelStackEnumerable<TEnumerator, GpuBigInt> source,
            Func<GpuBigInt, GpuBigIntExpr> selector)
        where TEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new ParallelStackEnumerable<SelectEnumerator<TEnumerator, GpuBigInt, GpuBigIntExpr>, GpuBigIntExpr>(
            new SelectEnumerator<TEnumerator, GpuBigInt, GpuBigIntExpr>(source.Enumerator, selector));
    }

    /// <summary>
    /// Projects each element of a parallel sequence into a new deferred GPU BigInt expression.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">The sequence of elements to invoke a transform function on.</param>
    /// <param name="selector">A transform function to apply to each source element.</param>
    /// <returns>A new parallel sequence containing the deferred expressions.</returns>
    public static ParallelStackEnumerable<SelectEnumerator<TEnumerator, GpuBigIntExpr, GpuBigIntExpr>, GpuBigIntExpr>
        Select<TEnumerator>(
            this ParallelStackEnumerable<TEnumerator, GpuBigIntExpr> source,
            Func<GpuBigIntExpr, GpuBigIntExpr> selector)
        where TEnumerator : struct, IStackEnumerator<GpuBigIntExpr>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new ParallelStackEnumerable<SelectEnumerator<TEnumerator, GpuBigIntExpr, GpuBigIntExpr>, GpuBigIntExpr>(
            new SelectEnumerator<TEnumerator, GpuBigIntExpr, GpuBigIntExpr>(source.Enumerator, selector));
    }

    /// <summary>
    /// Computes the sum of a parallel sequence of <see cref="GpuBigInt"/> values by constructing a balanced binary evaluation tree.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A parallel sequence of values that are used to calculate a sum.</param>
    /// <returns>A deferred expression tree representing the parallel sum.</returns>
    public static GpuBigIntExpr Sum<TEnumerator>(
        this ParallelStackEnumerable<TEnumerator, GpuBigInt> source)
        where TEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (!enumerator.TryGetNext(out var first))
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }

        if (!enumerator.TryGetNext(out var second))
        {
            return first;
        }

        // Use ArrayPool to avoid large heap allocations during tree building
        var pool = ArrayPool<GpuBigIntExpr>.Shared;
        var capacity = 16;
        var buffer = pool.Rent(capacity);
        var count = 0;

        try
        {
            buffer[count++] = first;
            buffer[count++] = second;

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

                buffer[count++] = value;
            }

            return BuildBalancedTreeSum(buffer.AsSpan(0, count));
        }
        finally
        {
            pool.Return(buffer, clearArray: true);
        }
    }

    /// <summary>
    /// Computes the sum of a parallel sequence of <see cref="GpuBigIntExpr"/>
    /// expressions by constructing a balanced binary evaluation tree.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A parallel sequence of expressions that are used to calculate a sum.</param>
    /// <returns>A deferred expression tree representing the parallel sum.</returns>
    public static GpuBigIntExpr Sum<TEnumerator>(
        this ParallelStackEnumerable<TEnumerator, GpuBigIntExpr> source)
        where TEnumerator : struct, IStackEnumerator<GpuBigIntExpr>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (!enumerator.TryGetNext(out var first))
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }

        if (!enumerator.TryGetNext(out var second))
        {
            return first;
        }

        var pool = ArrayPool<GpuBigIntExpr>.Shared;
        var capacity = 16;
        var buffer = pool.Rent(capacity);
        var count = 0;

        try
        {
            buffer[count++] = first;
            buffer[count++] = second;

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

                buffer[count++] = value;
            }

            return BuildBalancedTreeSum(buffer.AsSpan(0, count));
        }
        finally
        {
            pool.Return(buffer, clearArray: true);
        }
    }

    /// <summary>
    /// Computes the maximum of a parallel sequence of <see cref="GpuBigInt"/> values by constructing a balanced binary evaluation tree.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A parallel sequence of values that are used to calculate a max.</param>
    /// <returns>A deferred expression tree representing the parallel max.</returns>
    public static GpuBigIntExpr Max<TEnumerator>(
        this ParallelStackEnumerable<TEnumerator, GpuBigInt> source)
        where TEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (!enumerator.TryGetNext(out var first))
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }

        if (!enumerator.TryGetNext(out var second))
        {
            return first;
        }

        var pool = ArrayPool<GpuBigIntExpr>.Shared;
        var capacity = 16;
        var buffer = pool.Rent(capacity);
        var count = 0;

        try
        {
            buffer[count++] = first;
            buffer[count++] = second;

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

                buffer[count++] = value;
            }

            return BuildBalancedTreeMax(buffer.AsSpan(0, count));
        }
        finally
        {
            pool.Return(buffer, clearArray: true);
        }
    }

    /// <summary>
    /// Computes the maximum of a parallel sequence of <see cref="GpuBigIntExpr"/>
    /// expressions by constructing a balanced binary evaluation tree.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A parallel sequence of expressions that are used to calculate a max.</param>
    /// <returns>A deferred expression tree representing the parallel max.</returns>
    public static GpuBigIntExpr Max<TEnumerator>(
        this ParallelStackEnumerable<TEnumerator, GpuBigIntExpr> source)
        where TEnumerator : struct, IStackEnumerator<GpuBigIntExpr>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (!enumerator.TryGetNext(out var first))
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }

        if (!enumerator.TryGetNext(out var second))
        {
            return first;
        }

        var pool = ArrayPool<GpuBigIntExpr>.Shared;
        var capacity = 16;
        var buffer = pool.Rent(capacity);
        var count = 0;

        try
        {
            buffer[count++] = first;
            buffer[count++] = second;

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

                buffer[count++] = value;
            }

            return BuildBalancedTreeMax(buffer.AsSpan(0, count));
        }
        finally
        {
            pool.Return(buffer, clearArray: true);
        }
    }

    /// <summary>
    /// Computes the minimum of a parallel sequence of <see cref="GpuBigInt"/> values by constructing a balanced binary evaluation tree.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A parallel sequence of values that are used to calculate a min.</param>
    /// <returns>A deferred expression tree representing the parallel min.</returns>
    public static GpuBigIntExpr Min<TEnumerator>(
        this ParallelStackEnumerable<TEnumerator, GpuBigInt> source)
        where TEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (!enumerator.TryGetNext(out var first))
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }

        if (!enumerator.TryGetNext(out var second))
        {
            return first;
        }

        var pool = ArrayPool<GpuBigIntExpr>.Shared;
        var capacity = 16;
        var buffer = pool.Rent(capacity);
        var count = 0;

        try
        {
            buffer[count++] = first;
            buffer[count++] = second;

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

                buffer[count++] = value;
            }

            return BuildBalancedTreeMin(buffer.AsSpan(0, count));
        }
        finally
        {
            pool.Return(buffer, clearArray: true);
        }
    }

    /// <summary>
    /// Computes the minimum of a parallel sequence of <see cref="GpuBigIntExpr"/>
    /// expressions by constructing a balanced binary evaluation tree.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A parallel sequence of expressions that are used to calculate a min.</param>
    /// <returns>A deferred expression tree representing the parallel min.</returns>
    public static GpuBigIntExpr Min<TEnumerator>(
        this ParallelStackEnumerable<TEnumerator, GpuBigIntExpr> source)
        where TEnumerator : struct, IStackEnumerator<GpuBigIntExpr>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (!enumerator.TryGetNext(out var first))
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }

        if (!enumerator.TryGetNext(out var second))
        {
            return first;
        }

        var pool = ArrayPool<GpuBigIntExpr>.Shared;
        var capacity = 16;
        var buffer = pool.Rent(capacity);
        var count = 0;

        try
        {
            buffer[count++] = first;
            buffer[count++] = second;

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

                buffer[count++] = value;
            }

            return BuildBalancedTreeMin(buffer.AsSpan(0, count));
        }
        finally
        {
            pool.Return(buffer, clearArray: true);
        }
    }

    private static GpuBigIntExpr BuildBalancedTreeSum(ReadOnlySpan<GpuBigIntExpr> span)
    {
        switch (span.Length)
        {
            case 1:
                return span[0];
            case 2:
                return span[0] + span[1];
        }

        var mid = span.Length / 2;
        var left = BuildBalancedTreeSum(span.Slice(0, mid));
        var right = BuildBalancedTreeSum(span.Slice(mid));
        return left + right;
    }

    private static GpuBigIntExpr BuildBalancedTreeMax(ReadOnlySpan<GpuBigIntExpr> span)
    {
        switch (span.Length)
        {
            case 1:
                return span[0];
            case 2:
                return GpuBigIntExpr.Max(span[0], span[1]);

        }

        var mid = span.Length / 2;
        var left = BuildBalancedTreeMax(span.Slice(0, mid));
        var right = BuildBalancedTreeMax(span.Slice(mid));
        return GpuBigIntExpr.Max(left, right);
    }

    private static GpuBigIntExpr BuildBalancedTreeMin(ReadOnlySpan<GpuBigIntExpr> span)
    {
        switch (span.Length)
        {
            case 1:
                return span[0];
            case 2:
                return GpuBigIntExpr.Min(span[0], span[1]);

        }

        var mid = span.Length / 2;
        var left = BuildBalancedTreeMin(span.Slice(0, mid));
        var right = BuildBalancedTreeMin(span.Slice(mid));
        return GpuBigIntExpr.Min(left, right);
    }
}
#pragma warning restore GSH0005 // GpuBigIntExpr Direct Assignment
