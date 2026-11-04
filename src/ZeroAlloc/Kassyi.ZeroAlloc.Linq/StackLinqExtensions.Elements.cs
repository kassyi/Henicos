using System.Buffers;

namespace Kassyi.ZeroAlloc.Linq;

public static partial class StackLinqExtensions
{
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
        var pool = ArrayPool<TSource>.Shared;
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

    /// <summary>
    /// Determines whether a stack-allocated sequence contains a specified element by using the default equality comparer.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence in which to locate a value.</param>
    /// <param name="value">The value to locate in the sequence.</param>
    /// <returns><see langword="true"/> if the source sequence contains an element with the specified value; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> Linear search in $O(N)$ time with zero heap allocations.</para>
    /// </remarks>
    public static bool Contains<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        TSource value)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var current))
        {
            if (EqualityComparer<TSource>.Default.Equals(current, value))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns the element at a specified index in a stack-allocated sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence to return an element from.</param>
    /// <param name="index">The zero-based index of the element to retrieve.</param>
    /// <returns>The element at the specified position in the source sequence.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is less than 0 or greater than or equal to the number of elements in <paramref name="source"/>.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> $O(1)$ indexing for span/array sources; otherwise $O(N)$ with zero heap allocations.</para>
    /// </remarks>
    public static TSource ElementAt<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        int index)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (TryTakeRemainingSpan<TEnumerator, TSource>(ref enumerator, out var span))
        {
            if ((uint)index < (uint)span.Length)
            {
                return span[index];
            }
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var count = 0;
        while (enumerator.TryGetNext(out var current))
        {
            if (count == index)
            {
                return current;
            }
            count++;
        }
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    /// <summary>
    /// Returns the element at a specified index in a stack-allocated sequence or a default value if the index is out of range.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence to return an element from.</param>
    /// <param name="index">The zero-based index of the element to retrieve.</param>
    /// <returns>The default value of <typeparamref name="TSource"/> if <paramref name="index"/> is outside the bounds of <paramref name="source"/>; otherwise, the element at the specified position.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> $O(1)$ indexing for span/array sources; otherwise $O(N)$ with zero heap allocations.</para>
    /// </remarks>
    public static TSource? ElementAtOrDefault<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        int index)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (TryTakeRemainingSpan<TEnumerator, TSource>(ref enumerator, out var span))
        {
            if ((uint)index < (uint)span.Length)
            {
                return span[index];
            }
            return default;
        }

        var count = 0;
        while (enumerator.TryGetNext(out var current))
        {
            if (count == index)
            {
                return current;
            }
            count++;
        }
        return default;
    }

    /// <summary>
    /// Returns the only element of a stack-allocated sequence, and throws an exception if there is not exactly one element in the sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence to return the single element of.</param>
    /// <returns>The single element of the input sequence.</returns>
    /// <exception cref="InvalidOperationException">The input sequence contains no elements or more than one element.</exception>
    public static TSource Single<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (!enumerator.TryGetNext(out var result))
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }
        if (enumerator.TryGetNext(out _))
        {
            throw new InvalidOperationException("Sequence contains more than one element");
        }
        return result;
    }

    /// <summary>
    /// Returns the only element of a stack-allocated sequence that satisfies a specified condition, and throws an exception if more than one such element exists.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence to return a single element from.</param>
    /// <param name="predicate">A function to test an element for a condition.</param>
    /// <returns>The single element of the input sequence that satisfies the condition.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No element satisfies the condition in <paramref name="predicate"/>, or more than one element satisfies the condition.</exception>
    public static TSource Single<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var enumerator = source.Enumerator;
        while (enumerator.TryGetNext(out var current))
        {
            if (!predicate(current))
            {
                continue;
            }

            while (enumerator.TryGetNext(out var next))
            {
                if (predicate(next))
                {
                    throw new InvalidOperationException("Sequence contains more than one matching element");
                }
            }

            return current;
        }

        throw new InvalidOperationException("Sequence contains no matching element");
    }

    /// <summary>
    /// Returns the only element of a stack-allocated sequence, or a default value if the sequence is empty; this method throws an exception if there is more than one element in the sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence to return the single element of.</param>
    /// <returns>The single element of the input sequence, or default value if the sequence contains no elements.</returns>
    /// <exception cref="InvalidOperationException">The input sequence contains more than one element.</exception>
    public static TSource? SingleOrDefault<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (!enumerator.TryGetNext(out var result))
        {
            return default;
        }
        if (enumerator.TryGetNext(out _))
        {
            throw new InvalidOperationException("Sequence contains more than one element");
        }
        return result;
    }

    /// <summary>
    /// Returns the only element of a stack-allocated sequence that satisfies a specified condition, or a default value if no such element exists; this method throws an exception if more than one element satisfies the condition.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence to return a single element from.</param>
    /// <param name="predicate">A function to test an element for a condition.</param>
    /// <returns>The single element of the input sequence that satisfies the condition, or default value if no such element is found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">More than one element satisfies the condition in <paramref name="predicate"/>.</exception>
    public static TSource? SingleOrDefault<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var enumerator = source.Enumerator;
        TSource? result = default;
        var found = false;
        while (enumerator.TryGetNext(out var current))
        {
            if (predicate(current))
            {
                if (found)
                {
                    throw new InvalidOperationException("Sequence contains more than one matching element");
                }
                result = current;
                found = true;
            }
        }
        if (!found)
        {
            return default;
        }
        return result;
    }

    /// <summary>
    /// Returns the last element of a sequence that satisfies a specified condition.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">An sequence to return an element from.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns>The last element in the sequence that passes the test in the specified predicate function.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No element satisfies the condition in <paramref name="predicate"/>, or the source sequence is empty.</exception>
    public static TSource Last<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct =>
        source.Where(predicate).Last();

    /// <summary>
    /// Returns the last element of a stack-allocated sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence to return the last element of.</param>
    /// <returns>The value at the last position in the source sequence.</returns>
    /// <exception cref="InvalidOperationException">The source sequence is empty.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> $O(1)$ for span/array sources; otherwise $O(N)$ with zero heap allocations.</para>
    /// </remarks>
    public static TSource Last<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (TryTakeRemainingSpan<TEnumerator, TSource>(ref enumerator, out var span))
        {
            if (span.Length > 0)
            {
                return span[^1];
            }
            throw new InvalidOperationException("Sequence contains no elements");
        }

        if (!enumerator.TryGetNext(out var result))
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }
        while (enumerator.TryGetNext(out var current))
        {
            result = current;
        }
        return result;
    }

    /// <summary>
    /// Returns the last element of a sequence that satisfies a condition or a default value if no such element is found.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">An sequence to return an element from.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns><see langword="default"/>(TSource) if the sequence is empty or if no elements pass the test in the predicate function; otherwise, the last element that passes the test in the predicate function.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    public static TSource? LastOrDefault<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source,
        Func<TSource, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct =>
        source.Where(predicate).LastOrDefault();

    /// <summary>
    /// Returns the last element of a stack-allocated sequence, or a default value if the sequence contains no elements.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence to return the last element of.</param>
    /// <returns>The default value of <typeparamref name="TSource"/> if the source sequence is empty; otherwise, the last element in the sequence.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> $O(1)$ for span/array sources; otherwise $O(N)$ with zero heap allocations.</para>
    /// </remarks>
    public static TSource? LastOrDefault<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        var enumerator = source.Enumerator;
        if (TryTakeRemainingSpan<TEnumerator, TSource>(ref enumerator, out var span))
        {
            if (span.Length > 0)
            {
                return span[^1];
            }
            return default;
        }

        TSource? result = default;
        while (enumerator.TryGetNext(out var current))
        {
            result = current;
        }
        return result;
    }
}
