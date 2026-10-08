namespace Kassyi.ZeroAlloc.Linq;

/// <summary>
/// Provides a set of <see langword="static"/> extension methods for zero-allocation, stack-based LINQ operations over arrays, spans, and struct enumerators.
/// </summary>
/// <remarks>
/// <para><b>Performance:</b> Methods in this class operate via <see langword="ref struct"/> wrappers, avoiding boxing, interface dispatches, and heap allocations.</para>
/// <para><b>Execution Target:</b> Usable on CPU host (ideal for Roslyn Source Generators and high-throughput loops) and transpiled natively to C++/CUDA inside GPU kernels.</para>
/// </remarks>
public static class StackLinqExtensions
{
    /// <summary>
    /// Wraps a managed array into a zero-allocation <see cref="StackEnumerable{TEnumerator, T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of the elements in the array.</typeparam>
    /// <param name="source">The source array to wrap.</param>
    /// <returns>A stack-allocated enumerable wrapping an <see cref="ArrayEnumerator{T}"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Operates in $O(1)$ time with zero heap allocations.</para>
    /// </remarks>
    public static StackEnumerable<ArrayEnumerator<T>, T> AsStackEnumerable<T>(this T[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(new ArrayEnumerator<T>(source));
    }

    /// <summary>
    /// Wraps a <see cref="Span{T}"/> into a zero-allocation <see cref="StackEnumerable{TEnumerator, T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of the elements in the span.</typeparam>
    /// <param name="source">The source span to wrap.</param>
    /// <returns>A stack-allocated enumerable wrapping a <see cref="SpanEnumerator{T}"/>.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> Operates in $O(1)$ time with zero heap allocations.</para>
    /// </remarks>
    public static StackEnumerable<SpanEnumerator<T>, T> AsStackEnumerable<T>(this Span<T> source) =>
        new(new SpanEnumerator<T>(source));

    /// <summary>
    /// Wraps a <see cref="ReadOnlySpan{T}"/> into a zero-allocation <see cref="StackEnumerable{TEnumerator, T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of the elements in the span.</typeparam>
    /// <param name="source">The source read-only span to wrap.</param>
    /// <returns>A stack-allocated enumerable wrapping a <see cref="SpanEnumerator{T}"/>.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> Operates in $O(1)$ time with zero heap allocations.</para>
    /// </remarks>
    public static StackEnumerable<SpanEnumerator<T>, T> AsStackEnumerable<T>(this ReadOnlySpan<T> source) =>
        new(new SpanEnumerator<T>(source));

    /// <summary>
    /// Filters a stack-allocated sequence of values based on a predicate.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence to filter.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns>A <see cref="StackEnumerable{TEnumerator, TSource}"/> that contains elements from the input sequence that satisfy the condition.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Evaluated lazily with zero heap allocations.</para>
    /// </remarks>
    public static StackEnumerable<WhereEnumerator<TEnumerator, TSource>, TSource> Where<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new StackEnumerable<WhereEnumerator<TEnumerator, TSource>, TSource>(
            new WhereEnumerator<TEnumerator, TSource>(source.Enumerator, predicate));
    }

    /// <summary>
    /// Projects each element of a stack-allocated sequence into a new form.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the source sequence.</typeparam>
    /// <typeparam name="TResult">The type of the value returned by <paramref name="selector"/>.</typeparam>
    /// <param name="source">The sequence of elements to invoke a transform function on.</param>
    /// <param name="selector">A transform function to apply to each element.</param>
    /// <returns>A <see cref="StackEnumerable{TEnumerator, TResult}"/> whose elements are the result of invoking the transform function on each element of <paramref name="source"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="selector"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Evaluated lazily with zero heap allocations.</para>
    /// </remarks>
    public static StackEnumerable<SelectEnumerator<TEnumerator, TSource, TResult>, TResult> Select<TEnumerator, TSource,
        TResult>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, TResult> selector)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new StackEnumerable<SelectEnumerator<TEnumerator, TSource, TResult>, TResult>(
            new SelectEnumerator<TEnumerator, TSource, TResult>(source.Enumerator, selector));
    }

    /// <summary>
    /// Returns the number of elements in a stack-allocated sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of elements in the sequence.</typeparam>
    /// <param name="source">The sequence that contains elements to be counted.</param>
    /// <returns>The number of elements in the input sequence.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> Consumes the enumerator in $O(N)$ time with zero heap allocations.</para>
    /// </remarks>
    public static int Count<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        var count = 0;
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out _))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Determines whether a stack-allocated sequence contains any elements.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of elements in the sequence.</typeparam>
    /// <param name="source">The sequence to check for non-emptiness.</param>
    /// <returns><see langword="true"/> if the source sequence contains any elements; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> Short-circuits on the first element ($O(1)$) with zero heap allocations.</para>
    /// </remarks>
    public static bool Any<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        var enumerator = source.Enumerator;
        return enumerator.TryGetNext(out _);
    }

    /// <summary>
    /// Determines whether any element of a stack-allocated sequence satisfies a condition.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of elements in the sequence.</typeparam>
    /// <param name="source">The sequence whose elements to apply the predicate to.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns><see langword="true"/> if any element satisfies the condition; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Short-circuits upon the first matching element with zero heap allocations.</para>
    /// </remarks>
    public static bool Any<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var value))
        {
            if (predicate(value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether all elements of a stack-allocated sequence satisfy a condition.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of elements in the sequence.</typeparam>
    /// <param name="source">The sequence whose elements to apply the predicate to.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns><see langword="true"/> if every element of the source sequence passes the test in the specified predicate, or if the sequence is empty; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Short-circuits upon the first non-matching element with zero heap allocations.</para>
    /// </remarks>
    public static bool All<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var value))
        {
            if (!predicate(value))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns the first element of a stack-allocated sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of elements in the sequence.</typeparam>
    /// <param name="source">The sequence to return the first element of.</param>
    /// <returns>The first element in the sequence.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the source sequence is empty.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Operates in $O(1)$ time with zero heap allocations.</para>
    /// </remarks>
    public static TSource First<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (enumerator.TryGetNext(out var value))
        {
            return value;
        }

        throw new InvalidOperationException("Sequence contains no elements");
    }

    /// <summary>
    /// Returns the first element in a stack-allocated sequence that satisfies a specified condition.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of elements in the sequence.</typeparam>
    /// <param name="source">The sequence to return an element from.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns>The first element in the sequence that matches the condition.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no element satisfies the condition, or if the source sequence is empty.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Short-circuits upon the first match with zero heap allocations.</para>
    /// </remarks>
    public static TSource First<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var value))
        {
            if (predicate(value))
            {
                return value;
            }
        }

        throw new InvalidOperationException("Sequence contains no matching element");
    }

    /// <summary>
    /// Returns the first element of a stack-allocated sequence, or a default value if the sequence contains no elements.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of elements in the sequence.</typeparam>
    /// <param name="source">The sequence to return the first element of.</param>
    /// <returns>The default value of <typeparamref name="TSource"/> if <paramref name="source"/> is empty; otherwise, the first element.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> Operates in $O(1)$ time with zero heap allocations.</para>
    /// </remarks>
    public static TSource? FirstOrDefault<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (enumerator.TryGetNext(out var value))
        {
            return value;
        }

        return default;
    }

    /// <summary>
    /// Returns the first element of the sequence that satisfies a condition, or a default value if no such element is found.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of elements in the sequence.</typeparam>
    /// <param name="source">The sequence to return an element from.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns>The default value of <typeparamref name="TSource"/> if <paramref name="source"/> is empty or if no element passes the test; otherwise, the first element that passes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Short-circuits upon the first matching element with zero heap allocations.</para>
    /// </remarks>
    public static TSource? FirstOrDefault<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var value))
        {
            if (predicate(value))
            {
                return value;
            }
        }

        return default;
    }

    /// <summary>
    /// Computes the sum of a sequence of <see cref="int"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of <see cref="int"/> values to calculate the sum of.</param>
    /// <returns>The sum of the values in the sequence.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> Consumes the enumerator in $O(N)$ time with zero heap allocations.</para>
    /// </remarks>
    public static int Sum<TEnumerator>(
        this StackEnumerable<TEnumerator, int> source)
        where TEnumerator : struct, IStackEnumerator<int>, allows ref struct
    {
        var sum = 0;
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var value))
        {
            sum += value;
        }

        return sum;
    }

    /// <summary>
    /// Computes the sum of a sequence of <see cref="float"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of <see cref="float"/> values to calculate the sum of.</param>
    /// <returns>The sum of the values in the sequence.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> Consumes the enumerator in $O(N)$ time with zero heap allocations.</para>
    /// </remarks>
    public static float Sum<TEnumerator>(
        this StackEnumerable<TEnumerator, float> source)
        where TEnumerator : struct, IStackEnumerator<float>, allows ref struct
    {
        float sum = 0;
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var value))
        {
            sum += value;
        }

        return sum;
    }

    /// <summary>
    /// Computes the sum of a sequence of <see cref="double"/> values.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <param name="source">A sequence of <see cref="double"/> values to calculate the sum of.</param>
    /// <returns>The sum of the values in the sequence.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> Consumes the enumerator in $O(N)$ time with zero heap allocations.</para>
    /// </remarks>
    public static double Sum<TEnumerator>(
        this StackEnumerable<TEnumerator, double> source)
        where TEnumerator : struct, IStackEnumerator<double>, allows ref struct
    {
        double sum = 0;
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var value))
        {
            sum += value;
        }

        return sum;
    }

    /// <summary>
    /// Creates an array from a <see cref="StackEnumerable{TEnumerator, TSource}"/>.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The sequence to create an array from.</param>
    /// <returns>An array that contains the elements from the input sequence.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> Allocates only the final return array. Intermediate buffers are pooled via <see cref="System.Buffers.ArrayPool{T}.Shared"/> to eliminate heap fragmentation.</para>
    /// <para><b>Execution Target:</b> Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.</para>
    /// </remarks>
    public static TSource[] ToArray<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        var pool = System.Buffers.ArrayPool<TSource>.Shared;
        var buffer = pool.Rent(16);
        var count = 0;
        var enumerator = source.Enumerator;

        try
        {
            while (enumerator.TryGetNext(out var value))
            {
                if (count == buffer.Length)
                {
                    var newBuffer = pool.Rent(buffer.Length * 2);
                    Array.Copy(buffer, newBuffer, count);
                    pool.Return(buffer, clearArray: true);
                    buffer = newBuffer;
                }

                buffer[count++] = value;
            }

            var result = new TSource[count];
            Array.Copy(buffer, result, count);
            return result;
        }
        finally
        {
            pool.Return(buffer, clearArray: true);
        }
    }

    /// <summary>
    /// Copies the elements of a stack-allocated sequence into a destination span without any heap allocations.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The sequence whose elements are to be copied.</param>
    /// <param name="destination">The span that is the destination of the elements copied from the sequence.</param>
    /// <returns>The number of elements successfully copied into <paramref name="destination"/>.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> True zero-allocation extraction. Iteration stops when <paramref name="destination"/> is full or <paramref name="source"/> is exhausted.</para>
    /// </remarks>
    public static int CopyTo<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Span<TSource> destination)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        var count = 0;
        var enumerator = source.Enumerator;
        while (count < destination.Length && enumerator.TryGetNext(out var value))
        {
            destination[count++] = value;
        }

        return count;
    }
}
