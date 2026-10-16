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
    /// Filters a parallel sequence of values based on a predicate.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">An enumerable to filter.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns>A new parallel sequence that contains elements from the input sequence that satisfy the condition.</returns>
    public static ParallelStackEnumerable<WhereEnumerator<TEnumerator, GpuBigInt>, GpuBigInt>
        Where<TEnumerator>(
            this ParallelStackEnumerable<TEnumerator, GpuBigInt> source,
            Func<GpuBigInt, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new ParallelStackEnumerable<WhereEnumerator<TEnumerator, GpuBigInt>, GpuBigInt>(
            new WhereEnumerator<TEnumerator, GpuBigInt>(source.Enumerator, predicate));
    }

    /// <summary>
    /// Filters a parallel sequence of expressions based on a predicate.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">An enumerable to filter.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns>A new parallel sequence that contains elements from the input sequence that satisfy the condition.</returns>
    public static ParallelStackEnumerable<WhereEnumerator<TEnumerator, GpuBigIntExpr>, GpuBigIntExpr>
        Where<TEnumerator>(
            this ParallelStackEnumerable<TEnumerator, GpuBigIntExpr> source,
            Func<GpuBigIntExpr, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<GpuBigIntExpr>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new ParallelStackEnumerable<WhereEnumerator<TEnumerator, GpuBigIntExpr>, GpuBigIntExpr>(
            new WhereEnumerator<TEnumerator, GpuBigIntExpr>(source.Enumerator, predicate));
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

    /// <summary>
    /// Applies an accumulator function over a parallel sequence of <see cref="GpuBigInt"/> values by constructing a balanced binary evaluation tree.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A parallel sequence of values to aggregate.</param>
    /// <param name="func">An accumulator function to be invoked on each pair of expressions.</param>
    /// <returns>A deferred expression tree representing the parallel aggregation.</returns>
    public static GpuBigIntExpr Aggregate<TEnumerator>(
        this ParallelStackEnumerable<TEnumerator, GpuBigInt> source,
        Func<GpuBigIntExpr, GpuBigIntExpr, GpuBigIntExpr> func)
        where TEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(func);

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

            return BuildBalancedTreeAggregate(buffer.AsSpan(0, count), func);
        }
        finally
        {
            pool.Return(buffer, clearArray: true);
        }
    }

    /// <summary>
    /// Applies an accumulator function over a parallel sequence of <see cref="GpuBigIntExpr"/> expressions by constructing a balanced binary evaluation tree.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A parallel sequence of expressions to aggregate.</param>
    /// <param name="func">An accumulator function to be invoked on each pair of expressions.</param>
    /// <returns>A deferred expression tree representing the parallel aggregation.</returns>
    public static GpuBigIntExpr Aggregate<TEnumerator>(
        this ParallelStackEnumerable<TEnumerator, GpuBigIntExpr> source,
        Func<GpuBigIntExpr, GpuBigIntExpr, GpuBigIntExpr> func)
        where TEnumerator : struct, IStackEnumerator<GpuBigIntExpr>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(func);

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

            return BuildBalancedTreeAggregate(buffer.AsSpan(0, count), func);
        }
        finally
        {
            pool.Return(buffer, clearArray: true);
        }
    }

    private static GpuBigIntExpr BuildBalancedTreeAggregate(ReadOnlySpan<GpuBigIntExpr> span, Func<GpuBigIntExpr, GpuBigIntExpr, GpuBigIntExpr> func)
    {
        switch (span.Length)
        {
            case 1:
                return span[0];
            case 2:
                return func(span[0], span[1]);
        }

        var mid = span.Length / 2;
        var left = BuildBalancedTreeAggregate(span.Slice(0, mid), func);
        var right = BuildBalancedTreeAggregate(span.Slice(mid), func);
        return func(left, right);
    }

    /// <summary>
    /// Computes the average of a parallel sequence of <see cref="GpuBigInt"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A parallel sequence of values to calculate the average of.</param>
    /// <returns>A deferred expression tree representing the parallel average.</returns>
    public static GpuBigIntExpr Average<TEnumerator>(
        this ParallelStackEnumerable<TEnumerator, GpuBigInt> source)
        where TEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (!enumerator.TryGetNext(out var first))
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }

        var pool = ArrayPool<GpuBigIntExpr>.Shared;
        var capacity = 16;
        var buffer = pool.Rent(capacity);
        var count = 0;

        try
        {
            buffer[count++] = first;
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

            var sumTree = BuildBalancedTreeAggregate(buffer.AsSpan(0, count), (a, b) => a + b);
            
            var workspace = first.Value.Workspace;
            var numWords = first.Value.NumWords;
            var uintBuffer = new uint[numWords];
            uintBuffer[0] = (uint)count;
            var countBuffer = new GpuBigUInt(workspace, uintBuffer); 
            var countExpr = (GpuBigIntExpr)new GpuBigInt(countBuffer, false);

            return sumTree / countExpr;
        }
        finally
        {
            pool.Return(buffer, clearArray: true);
        }
    }

    /// <summary>
    /// Computes the number of elements in a parallel sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A parallel sequence.</param>
    /// <returns>The number of elements in the sequence.</returns>
    public static int Count<TEnumerator>(
        this ParallelStackEnumerable<TEnumerator, GpuBigInt> source)
        where TEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct
    {
        var enumerator = source.Enumerator;
        var count = 0;
        while (enumerator.TryGetNext(out _))
        {
            count++;
        }
        return count;
    }

    /// <summary>
    /// Computes the number of elements in a parallel sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A parallel sequence.</param>
    /// <returns>The number of elements in the sequence.</returns>
    public static int Count<TEnumerator>(
        this ParallelStackEnumerable<TEnumerator, GpuBigIntExpr> source)
        where TEnumerator : struct, IStackEnumerator<GpuBigIntExpr>, allows ref struct
    {
        var enumerator = source.Enumerator;
        var count = 0;
        while (enumerator.TryGetNext(out _))
        {
            count++;
        }
        return count;
    }

    /// <summary>
    /// Divides each element of a parallel sequence by a scalar.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">The parallel sequence of dividends.</param>
    /// <param name="scalar">The scalar divisor.</param>
    /// <returns>A new parallel sequence containing the deferred division expressions.</returns>
    public static ParallelStackEnumerable<SelectEnumerator<TEnumerator, GpuBigInt, GpuBigIntExpr>, GpuBigIntExpr>
        SelectDivide<TEnumerator>(
            this ParallelStackEnumerable<TEnumerator, GpuBigInt> source,
            GpuBigInt scalar)
        where TEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct =>
        source.Select(x => x / scalar);

    /// <summary>
    /// Divides each element of a parallel expression sequence by a scalar.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">The parallel sequence of dividend expressions.</param>
    /// <param name="scalar">The scalar divisor.</param>
    /// <returns>A new parallel sequence containing the deferred division expressions.</returns>
    public static ParallelStackEnumerable<SelectEnumerator<TEnumerator, GpuBigIntExpr, GpuBigIntExpr>, GpuBigIntExpr>
        SelectDivide<TEnumerator>(
            this ParallelStackEnumerable<TEnumerator, GpuBigIntExpr> source,
            GpuBigInt scalar)
        where TEnumerator : struct, IStackEnumerator<GpuBigIntExpr>, allows ref struct =>
        source.Select(x => x / scalar);

    /// <summary>
    /// Computes the remainder (modulo) of each element of a parallel sequence divided by a scalar.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">The parallel sequence of dividends.</param>
    /// <param name="scalar">The scalar divisor.</param>
    /// <returns>A new parallel sequence containing the deferred modulo expressions.</returns>
    public static ParallelStackEnumerable<SelectEnumerator<TEnumerator, GpuBigInt, GpuBigIntExpr>, GpuBigIntExpr>
        SelectModulo<TEnumerator>(
            this ParallelStackEnumerable<TEnumerator, GpuBigInt> source,
            GpuBigInt scalar)
        where TEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct =>
        source.Select(x => x % scalar);

    /// <summary>
    /// Computes the remainder (modulo) of each element of a parallel expression sequence divided by a scalar.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">The parallel sequence of dividend expressions.</param>
    /// <param name="scalar">The scalar divisor.</param>
    /// <returns>A new parallel sequence containing the deferred modulo expressions.</returns>
    public static ParallelStackEnumerable<SelectEnumerator<TEnumerator, GpuBigIntExpr, GpuBigIntExpr>, GpuBigIntExpr>
        SelectModulo<TEnumerator>(
            this ParallelStackEnumerable<TEnumerator, GpuBigIntExpr> source,
            GpuBigInt scalar)
        where TEnumerator : struct, IStackEnumerator<GpuBigIntExpr>, allows ref struct =>
        source.Select(x => x % scalar);

    /// <summary>
    /// Divides the first parallel sequence by the second parallel sequence element-wise.
    /// </summary>
    /// <typeparam name="TLeftEnumerator">The type of the left source enumerator.</typeparam>
    /// <typeparam name="TRightEnumerator">The type of the right source enumerator.</typeparam>
    /// <param name="left">The dividend parallel sequence.</param>
    /// <param name="right">The divisor parallel sequence.</param>
    /// <returns>A new parallel sequence containing the element-wise deferred division expressions.</returns>
    public static ParallelStackEnumerable<ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigInt, GpuBigInt, GpuBigIntExpr>, GpuBigIntExpr>
        ZipDivide<TLeftEnumerator, TRightEnumerator>(
            this ParallelStackEnumerable<TLeftEnumerator, GpuBigInt> left,
            ParallelStackEnumerable<TRightEnumerator, GpuBigInt> right)
        where TLeftEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct
        where TRightEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct =>
        new(new ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigInt, GpuBigInt, GpuBigIntExpr>(
            left.Enumerator, right.Enumerator, static (a, b) => a / b));

    /// <summary>
    /// Divides the first parallel expression sequence by the second parallel expression sequence element-wise.
    /// </summary>
    /// <typeparam name="TLeftEnumerator">The type of the left source enumerator.</typeparam>
    /// <typeparam name="TRightEnumerator">The type of the right source enumerator.</typeparam>
    /// <param name="left">The dividend parallel expression sequence.</param>
    /// <param name="right">The divisor parallel expression sequence.</param>
    /// <returns>A new parallel sequence containing the element-wise deferred division expressions.</returns>
    public static ParallelStackEnumerable<ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigIntExpr, GpuBigIntExpr, GpuBigIntExpr>, GpuBigIntExpr>
        ZipDivide<TLeftEnumerator, TRightEnumerator>(
            this ParallelStackEnumerable<TLeftEnumerator, GpuBigIntExpr> left,
            ParallelStackEnumerable<TRightEnumerator, GpuBigIntExpr> right)
        where TLeftEnumerator : struct, IStackEnumerator<GpuBigIntExpr>, allows ref struct
        where TRightEnumerator : struct, IStackEnumerator<GpuBigIntExpr>, allows ref struct =>
        new(new ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigIntExpr, GpuBigIntExpr, GpuBigIntExpr>(
            left.Enumerator, right.Enumerator, static (a, b) => a / b));

    /// <summary>
    /// Computes the remainder (modulo) of the first parallel sequence divided by the second parallel sequence element-wise.
    /// </summary>
    /// <typeparam name="TLeftEnumerator">The type of the left source enumerator.</typeparam>
    /// <typeparam name="TRightEnumerator">The type of the right source enumerator.</typeparam>
    /// <param name="left">The dividend parallel sequence.</param>
    /// <param name="right">The divisor parallel sequence.</param>
    /// <returns>A new parallel sequence containing the element-wise deferred modulo expressions.</returns>
    public static ParallelStackEnumerable<ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigInt, GpuBigInt, GpuBigIntExpr>, GpuBigIntExpr>
        ZipModulo<TLeftEnumerator, TRightEnumerator>(
            this ParallelStackEnumerable<TLeftEnumerator, GpuBigInt> left,
            ParallelStackEnumerable<TRightEnumerator, GpuBigInt> right)
        where TLeftEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct
        where TRightEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct =>
        new(new ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigInt, GpuBigInt, GpuBigIntExpr>(
            left.Enumerator, right.Enumerator, static (a, b) => a % b));

    /// <summary>
    /// Computes the remainder (modulo) of the first parallel expression sequence divided by the second parallel expression sequence element-wise.
    /// </summary>
    /// <typeparam name="TLeftEnumerator">The type of the left source enumerator.</typeparam>
    /// <typeparam name="TRightEnumerator">The type of the right source enumerator.</typeparam>
    /// <param name="left">The dividend parallel expression sequence.</param>
    /// <param name="right">The divisor parallel expression sequence.</param>
    /// <returns>A new parallel sequence containing the element-wise deferred modulo expressions.</returns>
    public static ParallelStackEnumerable<ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigIntExpr, GpuBigIntExpr, GpuBigIntExpr>, GpuBigIntExpr>
        ZipModulo<TLeftEnumerator, TRightEnumerator>(
            this ParallelStackEnumerable<TLeftEnumerator, GpuBigIntExpr> left,
            ParallelStackEnumerable<TRightEnumerator, GpuBigIntExpr> right)
        where TLeftEnumerator : struct, IStackEnumerator<GpuBigIntExpr>, allows ref struct
        where TRightEnumerator : struct, IStackEnumerator<GpuBigIntExpr>, allows ref struct =>
        new(new ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigIntExpr, GpuBigIntExpr, GpuBigIntExpr>(
            left.Enumerator, right.Enumerator, static (a, b) => a % b));

    /// <summary>
    /// Divides each element of a stack-allocated sequence by a scalar.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">The sequence of dividends.</param>
    /// <param name="scalar">The scalar divisor.</param>
    /// <returns>A new stack-allocated sequence containing the deferred division expressions.</returns>
    public static StackEnumerable<SelectEnumerator<TEnumerator, GpuBigInt, GpuBigIntExpr>, GpuBigIntExpr>
        SelectDivide<TEnumerator>(
            this StackEnumerable<TEnumerator, GpuBigInt> source,
            GpuBigInt scalar)
        where TEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct =>
        source.Select(x => x / scalar);

    /// <summary>
    /// Divides each element of a stack-allocated expression sequence by a scalar.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">The sequence of dividend expressions.</param>
    /// <param name="scalar">The scalar divisor.</param>
    /// <returns>A new stack-allocated sequence containing the deferred division expressions.</returns>
    public static StackEnumerable<SelectEnumerator<TEnumerator, GpuBigIntExpr, GpuBigIntExpr>, GpuBigIntExpr>
        SelectDivide<TEnumerator>(
            this StackEnumerable<TEnumerator, GpuBigIntExpr> source,
            GpuBigInt scalar)
        where TEnumerator : struct, IStackEnumerator<GpuBigIntExpr>, allows ref struct =>
        source.Select(x => x / scalar);

    /// <summary>
    /// Computes the remainder (modulo) of each element of a stack-allocated sequence divided by a scalar.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">The sequence of dividends.</param>
    /// <param name="scalar">The scalar divisor.</param>
    /// <returns>A new stack-allocated sequence containing the deferred modulo expressions.</returns>
    public static StackEnumerable<SelectEnumerator<TEnumerator, GpuBigInt, GpuBigIntExpr>, GpuBigIntExpr>
        SelectModulo<TEnumerator>(
            this StackEnumerable<TEnumerator, GpuBigInt> source,
            GpuBigInt scalar)
        where TEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct =>
        source.Select(x => x % scalar);

    /// <summary>
    /// Computes the remainder (modulo) of each element of a stack-allocated expression sequence divided by a scalar.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">The sequence of dividend expressions.</param>
    /// <param name="scalar">The scalar divisor.</param>
    /// <returns>A new stack-allocated sequence containing the deferred modulo expressions.</returns>
    public static StackEnumerable<SelectEnumerator<TEnumerator, GpuBigIntExpr, GpuBigIntExpr>, GpuBigIntExpr>
        SelectModulo<TEnumerator>(
            this StackEnumerable<TEnumerator, GpuBigIntExpr> source,
            GpuBigInt scalar)
        where TEnumerator : struct, IStackEnumerator<GpuBigIntExpr>, allows ref struct =>
        source.Select(x => x % scalar);

    /// <summary>
    /// Divides the first stack-allocated sequence by the second sequence element-wise.
    /// </summary>
    /// <typeparam name="TLeftEnumerator">The type of the left source enumerator.</typeparam>
    /// <typeparam name="TRightEnumerator">The type of the right source enumerator.</typeparam>
    /// <param name="left">The dividend sequence.</param>
    /// <param name="right">The divisor sequence.</param>
    /// <returns>A new stack-allocated sequence containing the element-wise deferred division expressions.</returns>
    public static StackEnumerable<ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigInt, GpuBigInt, GpuBigIntExpr>, GpuBigIntExpr>
        ZipDivide<TLeftEnumerator, TRightEnumerator>(
            this StackEnumerable<TLeftEnumerator, GpuBigInt> left,
            StackEnumerable<TRightEnumerator, GpuBigInt> right)
        where TLeftEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct
        where TRightEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct =>
        new(new ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigInt, GpuBigInt, GpuBigIntExpr>(
            left.Enumerator, right.Enumerator, static (a, b) => a / b));

    /// <summary>
    /// Divides the first stack-allocated expression sequence by the second expression sequence element-wise.
    /// </summary>
    /// <typeparam name="TLeftEnumerator">The type of the left source enumerator.</typeparam>
    /// <typeparam name="TRightEnumerator">The type of the right source enumerator.</typeparam>
    /// <param name="left">The dividend expression sequence.</param>
    /// <param name="right">The divisor expression sequence.</param>
    /// <returns>A new stack-allocated sequence containing the element-wise deferred division expressions.</returns>
    public static StackEnumerable<ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigIntExpr, GpuBigIntExpr, GpuBigIntExpr>, GpuBigIntExpr>
        ZipDivide<TLeftEnumerator, TRightEnumerator>(
            this StackEnumerable<TLeftEnumerator, GpuBigIntExpr> left,
            StackEnumerable<TRightEnumerator, GpuBigIntExpr> right)
        where TLeftEnumerator : struct, IStackEnumerator<GpuBigIntExpr>, allows ref struct
        where TRightEnumerator : struct, IStackEnumerator<GpuBigIntExpr>, allows ref struct =>
        new(new ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigIntExpr, GpuBigIntExpr, GpuBigIntExpr>(
            left.Enumerator, right.Enumerator, static (a, b) => a / b));

    /// <summary>
    /// Computes the remainder (modulo) of the first stack-allocated sequence divided by the second sequence element-wise.
    /// </summary>
    /// <typeparam name="TLeftEnumerator">The type of the left source enumerator.</typeparam>
    /// <typeparam name="TRightEnumerator">The type of the right source enumerator.</typeparam>
    /// <param name="left">The dividend sequence.</param>
    /// <param name="right">The divisor sequence.</param>
    /// <returns>A new stack-allocated sequence containing the element-wise deferred modulo expressions.</returns>
    public static StackEnumerable<ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigInt, GpuBigInt, GpuBigIntExpr>, GpuBigIntExpr>
        ZipModulo<TLeftEnumerator, TRightEnumerator>(
            this StackEnumerable<TLeftEnumerator, GpuBigInt> left,
            StackEnumerable<TRightEnumerator, GpuBigInt> right)
        where TLeftEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct
        where TRightEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct =>
        new(new ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigInt, GpuBigInt, GpuBigIntExpr>(
            left.Enumerator, right.Enumerator, static (a, b) => a % b));

    /// <summary>
    /// Computes the remainder (modulo) of the first stack-allocated expression sequence divided by the second expression sequence element-wise.
    /// </summary>
    /// <typeparam name="TLeftEnumerator">The type of the left source enumerator.</typeparam>
    /// <typeparam name="TRightEnumerator">The type of the right source enumerator.</typeparam>
    /// <param name="left">The dividend expression sequence.</param>
    /// <param name="right">The divisor expression sequence.</param>
    /// <returns>A new stack-allocated sequence containing the element-wise deferred modulo expressions.</returns>
    public static StackEnumerable<ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigIntExpr, GpuBigIntExpr, GpuBigIntExpr>, GpuBigIntExpr>
        ZipModulo<TLeftEnumerator, TRightEnumerator>(
            this StackEnumerable<TLeftEnumerator, GpuBigIntExpr> left,
            StackEnumerable<TRightEnumerator, GpuBigIntExpr> right)
        where TLeftEnumerator : struct, IStackEnumerator<GpuBigIntExpr>, allows ref struct
        where TRightEnumerator : struct, IStackEnumerator<GpuBigIntExpr>, allows ref struct =>
        new(new ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, GpuBigIntExpr, GpuBigIntExpr, GpuBigIntExpr>(
            left.Enumerator, right.Enumerator, static (a, b) => a % b));

    /// <summary>
    /// Computes the sum of a sequence of <see cref="GpuBigInt"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of values that are used to calculate a sum.</param>
    /// <returns>A deferred expression tree representing the sum.</returns>
    public static GpuBigIntExpr Sum<TEnumerator>(
        this StackEnumerable<TEnumerator, GpuBigInt> source)
        where TEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct =>
        source.AsParallel().Sum();

    /// <summary>
    /// Computes the sum of a sequence of <see cref="GpuBigIntExpr"/> expressions.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of expressions that are used to calculate a sum.</param>
    /// <returns>A deferred expression tree representing the sum.</returns>
    public static GpuBigIntExpr Sum<TEnumerator>(
        this StackEnumerable<TEnumerator, GpuBigIntExpr> source)
        where TEnumerator : struct, IStackEnumerator<GpuBigIntExpr>, allows ref struct =>
        source.AsParallel().Sum();

    /// <summary>
    /// Computes the maximum of a sequence of <see cref="GpuBigInt"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of values that are used to calculate a max.</param>
    /// <returns>A deferred expression tree representing the maximum.</returns>
    public static GpuBigIntExpr Max<TEnumerator>(
        this StackEnumerable<TEnumerator, GpuBigInt> source)
        where TEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct =>
        source.AsParallel().Max();

    /// <summary>
    /// Computes the maximum of a sequence of <see cref="GpuBigIntExpr"/> expressions.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of expressions that are used to calculate a max.</param>
    /// <returns>A deferred expression tree representing the maximum.</returns>
    public static GpuBigIntExpr Max<TEnumerator>(
        this StackEnumerable<TEnumerator, GpuBigIntExpr> source)
        where TEnumerator : struct, IStackEnumerator<GpuBigIntExpr>, allows ref struct =>
        source.AsParallel().Max();

    /// <summary>
    /// Computes the minimum of a sequence of <see cref="GpuBigInt"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of values that are used to calculate a min.</param>
    /// <returns>A deferred expression tree representing the minimum.</returns>
    public static GpuBigIntExpr Min<TEnumerator>(
        this StackEnumerable<TEnumerator, GpuBigInt> source)
        where TEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct =>
        source.AsParallel().Min();

    /// <summary>
    /// Computes the minimum of a sequence of <see cref="GpuBigIntExpr"/> expressions.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of expressions that are used to calculate a min.</param>
    /// <returns>A deferred expression tree representing the minimum.</returns>
    public static GpuBigIntExpr Min<TEnumerator>(
        this StackEnumerable<TEnumerator, GpuBigIntExpr> source)
        where TEnumerator : struct, IStackEnumerator<GpuBigIntExpr>, allows ref struct =>
        source.AsParallel().Min();

    /// <summary>
    /// Applies an accumulator function over a sequence of <see cref="GpuBigInt"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of values to aggregate.</param>
    /// <param name="func">An accumulator function to be invoked on each pair of expressions.</param>
    /// <returns>A deferred expression tree representing the aggregation.</returns>
    public static GpuBigIntExpr Aggregate<TEnumerator>(
        this StackEnumerable<TEnumerator, GpuBigInt> source,
        Func<GpuBigIntExpr, GpuBigIntExpr, GpuBigIntExpr> func)
        where TEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct =>
        source.AsParallel().Aggregate(func);

    /// <summary>
    /// Applies an accumulator function over a sequence of <see cref="GpuBigIntExpr"/> expressions.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of expressions to aggregate.</param>
    /// <param name="func">An accumulator function to be invoked on each pair of expressions.</param>
    /// <returns>A deferred expression tree representing the aggregation.</returns>
    public static GpuBigIntExpr Aggregate<TEnumerator>(
        this StackEnumerable<TEnumerator, GpuBigIntExpr> source,
        Func<GpuBigIntExpr, GpuBigIntExpr, GpuBigIntExpr> func)
        where TEnumerator : struct, IStackEnumerator<GpuBigIntExpr>, allows ref struct =>
        source.AsParallel().Aggregate(func);

    /// <summary>
    /// Computes the average of a sequence of <see cref="GpuBigInt"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of values to calculate the average of.</param>
    /// <returns>A deferred expression tree representing the average.</returns>
    public static GpuBigIntExpr Average<TEnumerator>(
        this StackEnumerable<TEnumerator, GpuBigInt> source)
        where TEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct =>
        source.AsParallel().Average();
}

/// <summary>
/// Combines two stack-allocated sequences element-wise with zero heap allocations.
/// </summary>
/// <typeparam name="TLeftEnumerator">The type of the first source enumerator.</typeparam>
/// <typeparam name="TRightEnumerator">The type of the second source enumerator.</typeparam>
/// <typeparam name="TLeft">The element type of the first sequence.</typeparam>
/// <typeparam name="TRight">The element type of the second sequence.</typeparam>
/// <typeparam name="TResult">The result element type.</typeparam>
public ref struct ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, TLeft, TRight, TResult> : IStackEnumerator<TResult>
    where TLeftEnumerator : struct, IStackEnumerator<TLeft>, allows ref struct
    where TRightEnumerator : struct, IStackEnumerator<TRight>, allows ref struct
    where TResult : struct
{
    private TLeftEnumerator _left;
    private TRightEnumerator _right;
    private readonly Func<TLeft, TRight, TResult> _selector;

    /// <summary>
    /// Initializes a new instance of the <see cref="ZipStackEnumerator{TLeftEnumerator, TRightEnumerator, TLeft, TRight, TResult}"/> struct.
    /// </summary>
    /// <param name="left">The first source enumerator.</param>
    /// <param name="right">The second source enumerator.</param>
    /// <param name="selector">A function that combines corresponding elements from both sequences.</param>
    public ZipStackEnumerator(TLeftEnumerator left, TRightEnumerator right, Func<TLeft, TRight, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _left = left;
        _right = right;
        _selector = selector;
    }

    /// <summary>
    /// Advances both enumerators and produces the combined element.
    /// </summary>
    /// <param name="current">When this method returns <see langword="true"/>, contains the combined element.</param>
    /// <returns><see langword="true"/> if both enumerators advanced; otherwise, <see langword="false"/>.</returns>
    public bool TryGetNext(out TResult current)
    {
        if (_left.TryGetNext(out var leftVal) && _right.TryGetNext(out var rightVal))
        {
            current = _selector(leftVal, rightVal);
            return true;
        }

        current = default;
        return false;
    }
}
#pragma warning restore GSH0005 // GpuBigIntExpr Direct Assignment
